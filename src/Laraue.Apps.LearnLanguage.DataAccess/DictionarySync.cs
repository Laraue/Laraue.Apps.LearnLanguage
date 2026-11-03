using System.Security.Cryptography;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Laraue.Apps.LearnLanguage.DataAccess;

/// <summary>
/// Applies translations.json to the words, translations and word_topics tables. The dictionary used to be
/// seeded with HasData, which put it into every migration's model and made applying a migration need more
/// memory than the production container has. Words, translations and topic links missing from the file are
/// deleted, together with the users' history for them (the foreign keys cascade).
/// </summary>
public sealed class DictionarySync(DatabaseContext context, ILogger<DictionarySync> logger)
{
    /// <summary>
    /// Everything the synced rows are built from: the words file and the files its names are resolved against.
    /// </summary>
    private static readonly string[] SourceFiles =
        ["translations.json", "topics.json", "languages.json", "partsOfSpeech.json"];

    public async Task SyncAsync(CancellationToken ct = default)
    {
        var hash = ComputeSourceHash();

        var version = await context.DictionaryVersions.SingleOrDefaultAsync(ct);
        if (version?.Hash == hash)
        {
            logger.LogInformation("Dictionary is up to date, hash {Hash}", hash);
            return;
        }

        var words = DefaultContextData.GetWords();
        var partsOfSpeech = DefaultContextData.GetPartOfSpeeches();
        var topics = DefaultContextData.GetWordTopics();
        var languages = DefaultContextData.GetWordLanguages();

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        // Primary keys make a duplicate in the file fail the sync instead of being applied twice.
        await ExecuteAsync(connection, """
            CREATE TEMP TABLE sync_words (
                id bigint PRIMARY KEY,
                text text NOT NULL,
                transcription text,
                cefr_level_id bigint,
                part_of_speech_id bigint NOT NULL
            ) ON COMMIT DROP;
            CREATE TEMP TABLE sync_translations (
                word_id bigint,
                language_id bigint,
                text text NOT NULL,
                transcription text,
                PRIMARY KEY (word_id, language_id)
            ) ON COMMIT DROP;
            CREATE TEMP TABLE sync_word_topics (
                word_id bigint,
                topic_id bigint,
                PRIMARY KEY (word_id, topic_id)
            ) ON COMMIT DROP;
            """, ct);

        await using (var import = await connection.BeginBinaryImportAsync(
            "COPY sync_words (id, text, transcription, cefr_level_id, part_of_speech_id) FROM STDIN (FORMAT BINARY)",
            ct))
        {
            foreach (var word in words)
            {
                await import.StartRowAsync(ct);
                await import.WriteAsync((long)word.Id, NpgsqlDbType.Bigint, ct);
                await import.WriteAsync(word.Word, NpgsqlDbType.Text, ct);
                await WriteNullableAsync(import, word.Transcription, NpgsqlDbType.Text, ct);
                await WriteNullableAsync(
                    import,
                    word.CefrLevel is not null ? DefaultContextData.CefrLevels.GetId(word.CefrLevel) : null,
                    NpgsqlDbType.Bigint,
                    ct);
                await import.WriteAsync(partsOfSpeech.GetId(word.PartOfSpeech), NpgsqlDbType.Bigint, ct);
            }

            await import.CompleteAsync(ct);
        }

        await using (var import = await connection.BeginBinaryImportAsync(
            "COPY sync_translations (word_id, language_id, text, transcription) FROM STDIN (FORMAT BINARY)",
            ct))
        {
            foreach (var word in words)
            {
                foreach (var translation in word.Translations)
                {
                    await import.StartRowAsync(ct);
                    await import.WriteAsync((long)word.Id, NpgsqlDbType.Bigint, ct);
                    await import.WriteAsync(languages.GetId(translation.Language), NpgsqlDbType.Bigint, ct);
                    await import.WriteAsync(translation.Text, NpgsqlDbType.Text, ct);
                    await WriteNullableAsync(import, translation.Transcription, NpgsqlDbType.Text, ct);
                }
            }

            await import.CompleteAsync(ct);
        }

        await using (var import = await connection.BeginBinaryImportAsync(
            "COPY sync_word_topics (word_id, topic_id) FROM STDIN (FORMAT BINARY)",
            ct))
        {
            foreach (var word in words)
            {
                foreach (var topic in word.Topics)
                {
                    await import.StartRowAsync(ct);
                    await import.WriteAsync((long)word.Id, NpgsqlDbType.Bigint, ct);
                    await import.WriteAsync(topics.GetId(topic), NpgsqlDbType.Bigint, ct);
                }
            }

            await import.CompleteAsync(ct);
        }

        var deletedWordTopics = await ExecuteAsync(connection, """
            DELETE FROM word_topics t
            WHERE NOT EXISTS (SELECT 1 FROM sync_word_topics s WHERE s.word_id = t.word_id AND s.topic_id = t.topic_id)
            """, ct);

        var deletedTranslations = await ExecuteAsync(connection, """
            DELETE FROM translations t
            WHERE NOT EXISTS (SELECT 1 FROM sync_translations s WHERE s.word_id = t.word_id AND s.language_id = t.language_id)
            """, ct);

        var deletedWords = await ExecuteAsync(connection, """
            DELETE FROM words w
            WHERE NOT EXISTS (SELECT 1 FROM sync_words s WHERE s.id = w.id)
            """, ct);

        // Only the columns that come from the file are written, so meaning and the computed translation
        // columns (average_attempts, difficulty) keep their values. Unchanged rows are not touched.
        var upsertedWords = await ExecuteAsync(connection, """
            INSERT INTO words (id, text, transcription, cefr_level_id, part_of_speech_id)
            SELECT id, text, transcription, cefr_level_id, part_of_speech_id FROM sync_words
            ON CONFLICT (id) DO UPDATE SET
                text = EXCLUDED.text,
                transcription = EXCLUDED.transcription,
                cefr_level_id = EXCLUDED.cefr_level_id,
                part_of_speech_id = EXCLUDED.part_of_speech_id
            WHERE (words.text, words.transcription, words.cefr_level_id, words.part_of_speech_id)
                IS DISTINCT FROM (EXCLUDED.text, EXCLUDED.transcription, EXCLUDED.cefr_level_id, EXCLUDED.part_of_speech_id)
            """, ct);

        var upsertedTranslations = await ExecuteAsync(connection, """
            INSERT INTO translations (word_id, language_id, text, transcription)
            SELECT word_id, language_id, text, transcription FROM sync_translations
            ON CONFLICT (word_id, language_id) DO UPDATE SET
                text = EXCLUDED.text,
                transcription = EXCLUDED.transcription
            WHERE (translations.text, translations.transcription)
                IS DISTINCT FROM (EXCLUDED.text, EXCLUDED.transcription)
            """, ct);

        var insertedWordTopics = await ExecuteAsync(connection, """
            INSERT INTO word_topics (word_id, topic_id)
            SELECT word_id, topic_id FROM sync_word_topics
            ON CONFLICT DO NOTHING
            """, ct);

        if (version is null)
        {
            version = new DictionaryVersion { Id = DictionaryVersion.SingleRowId, Hash = hash };
            context.DictionaryVersions.Add(version);
        }
        else
        {
            version.Hash = hash;
        }

        version.SyncedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Dictionary synced, hash {Hash}. Words: {UpsertedWords} inserted or updated, {DeletedWords} deleted. " +
            "Translations: {UpsertedTranslations} inserted or updated, {DeletedTranslations} deleted. " +
            "Word topics: {InsertedWordTopics} inserted, {DeletedWordTopics} deleted",
            hash,
            upsertedWords,
            deletedWords,
            upsertedTranslations,
            deletedTranslations,
            insertedWordTopics,
            deletedWordTopics);
    }

    private static string ComputeSourceHash()
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in SourceFiles)
        {
            sha.AppendData(File.ReadAllBytes(DefaultContextData.GetJsonFilePath(file)));
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static Task WriteNullableAsync(
        NpgsqlBinaryImporter import,
        string? value,
        NpgsqlDbType type,
        CancellationToken ct)
    {
        return value is null ? import.WriteNullAsync(ct) : import.WriteAsync(value, type, ct);
    }

    private static Task WriteNullableAsync(
        NpgsqlBinaryImporter import,
        long? value,
        NpgsqlDbType type,
        CancellationToken ct)
    {
        return value is null ? import.WriteNullAsync(ct) : import.WriteAsync(value.Value, type, ct);
    }
}
