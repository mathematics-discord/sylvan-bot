# Sylvan Bot

A Discord bot for server moderation, role colours, emoji tools, and quorum checks. Built with C#, Discord.NET, and PostgreSQL.

## Run locally

You need the .NET SDK specified in [global.json](global.json), Docker with Compose, and a development bot invited to a test server. In the [Discord Developer Portal](https://discord.com/developers/applications), enable **Server Members Intent** and **Message Content Intent** for that bot.

From the repository root, copy the example configuration:

```sh
cp env/local.env.example .env
```

Set `DISCORD__TOKEN` in `.env` to your bot's token. **That's the only value you need to change.** The example includes the local database settings, and Git ignores `.env`.

For an existing local database, keep its `DB__DATABASE` value in `.env` and set `SYLVAN_BOT_POSTGRES_VOLUME` to its current volume name (for example, `ratbot_postgres_data`). This reuses the stored data instead of selecting a new volume.

Start Docker, then choose how to run the bot:

| Editor | Setup and launch |
| --- | --- |
| Rider | Open `SylvanBot.slnx`, select **Sylvan Bot Local with database**, then Run or Debug. |
| VS Code | Open the repository folder, install the recommended **C# Dev Kit** extension, then launch **Sylvan Bot Local with database** from Run and Debug. |
| Visual Studio | Open `SylvanBot.slnx`, set `SylvanBot` as the startup project, select **Debug** and **SylvanBot.Local**, then press F5. Use a version that supports the SDK in `global.json`. |

These setups start PostgreSQL and wait until it is ready. Visual Studio does this during Debug builds, so keep build-before-run enabled; an ordinary Debug build in Visual Studio also starts the database.

To run from a terminal instead:

```sh
docker compose --env-file .env -f docker-compose.yml up -d --wait db
dotnet run --project SylvanBot --launch-profile SylvanBot.Local
```

Sylvan Bot applies database migrations and registers commands automatically on startup.

Stopping the bot leaves PostgreSQL running. `docker compose down` stops the local stack and keeps its data for next time.

## Contributing

Keep pull requests focused, follow the existing formatting rules in `.editorconfig`, and include tests for changed behaviour.

Code is organised by feature under `SylvanBot/Features/`. Each feature keeps its commands, gateway handlers, operations, and models together; database stores and EF mappings live in its `Persistence/` subfolder. Shared Discord interaction code lives in `SylvanBot/Interactions/`, gateway lifecycle code in `SylvanBot/Gateway/`, and general helpers in `SylvanBot/Common/`. The database context and connection setup live in `SylvanBot/Infrastructure/Data/`, with migration history in `SylvanBot/Infrastructure/Migrations/`. Registrations are explicit in `SylvanBot/DependencyInjection.cs`.

Build and test from the repository root:

```sh
dotnet build SylvanBot.slnx --configuration Release
dotnet test SylvanBot.slnx --configuration Release --results-directory ./TestResults
```

The PostgreSQL integration tests use Testcontainers and need Docker running. They create their own test databases; the development bot does not need to be running. For a focused change, run the affected test project or use `--filter`.

See the [changelog](CHANGELOG.md) for release history.

Write changelog entries around what users will notice, need to do differently, or can no longer rely on. Use the `Internal` component for refactors, build tooling, and other changes without user-facing effects, with the usual Added/Changed/Fixed kinds. Internal entries remain in the full changelog; Discord announcements link to them in a small-text footer instead of listing them. Releases containing only internal changes are announced as maintenance releases with no user-facing changes.
