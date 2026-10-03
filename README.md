# Laraue.Apps.LearnLanguage

The repository contains the backend of Telegram bot allows to learn translations of the top 5000+ most used english words
passing quizes. Deployed bot can be found in [Telegram](https://t.me/learn_lang_bot?start=source-github).

Each word has it's own CEFR level and related topics. That allows to setup quizes 
to learn preffered words first of all.

### Bot interface example
<img width="215" height="217" alt="Quiz is ready View" src="https://github.com/user-attachments/assets/ffa94a55-ad04-4db2-8525-ec02204b019d" />
<img width="250" height="257" alt="Select the quiz answer View" src="https://github.com/user-attachments/assets/59822e62-0328-4a35-a70b-b543856961ab" />
<img width="236" height="451" alt="Quiz results View" src="https://github.com/user-attachments/assets/ebbe43c2-289d-481b-840c-779c3beecbb1" />

### How to add new or edit old words
1. Edit [translations.json](src/Laraue.Apps.LearnLanguage.DataAccess/translations.json)
2. Create new migration: `cd src && dotnet ef migrations add MigrationName -p Laraue.Apps.LearnLanguage.DataAccess -s Laraue.Apps.LearnLanguage.Host -v`
3. At the next application run translations will be added to database automatically

### How to add new language
1. Edit [languages.json](src/Laraue.Apps.LearnLanguage.DataAccess/languages.json)
2. Create new migration: `cd src && dotnet ef migrations add MigrationName -p Laraue.Apps.LearnLanguage.DataAccess -s Laraue.Apps.LearnLanguage.Host -v`

## Local run (long-pooling mode)
1. Create new telegram bot with @BotFather and get a token
2. Create file `appsettings.Development.json` in the project `Laraue.Apps.LearnLanguage.Host` and fill it with taken telegram token
```json
{
    "Telegram": {
        "Token": "tg_token"
    }
}
```
3. Run `Laraue.Apps.LearnLanguage.Host`. Write `/start` to your bot and wait for answer.

## License

The source code is available under the [PolyForm Noncommercial License 1.0.0](LICENSE): free for personal
and other noncommercial use. For commercial use see [COMMERCIAL.md](COMMERCIAL.md).
