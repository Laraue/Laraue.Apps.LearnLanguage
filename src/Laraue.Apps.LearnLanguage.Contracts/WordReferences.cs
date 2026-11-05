namespace Laraue.Apps.LearnLanguage.Contracts;

public interface IHasWordReference
{
    public long WordId { get; }
}

public interface IHasTranslationReference : IHasWordReference
{
    public long LanguageId { get; }
}