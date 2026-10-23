# RatBot

A Discord bot for server moderation, role colours, emoji tools, and quorum checks. Built with C#, Discord.NET, and PostgreSQL.

## Run locally

You need the .NET SDK specified in [global.json](global.json), Docker with Compose, and a development bot invited to a test server. In the [Discord Developer Portal](https://discord.com/developers/applications), enable **Server Members Intent** and **Message Content Intent** for that bot.

From the repository root, copy the example configuration:

```sh
cp env/local.env.example .env
```

Set `DISCORD__TOKEN` in `.env` to your bot's token and `BRIDGE__CREDENTIAL` to a separate random credential of at least 32 characters. The example includes the local database settings, and Git ignores `.env`.

Bridge exposes an authenticated WebSocket at `ws://127.0.0.1:8765/bridge`. See [Bridge configuration and protocol](docs/bridge.md), including operation without privileged Discord intents.

Start Docker, then choose how to run the bot:

| Editor | Setup and launch |
| --- | --- |
| Rider | Open `RatBot.slnx`, select **RatBot Local with database**, then Run or Debug. |
| VS Code | Open the repository folder, install the recommended **C# Dev Kit** extension, then launch **RatBot Local with database** from Run and Debug. |
| Visual Studio | Open `RatBot.slnx`, set `RatBot` as the startup project, select **Debug** and **RatBot.Local**, then press F5. Use a version that supports the SDK in `global.json`. |

These setups start PostgreSQL and wait until it is ready. Visual Studio does this during Debug builds, so keep build-before-run enabled; an ordinary Debug build in Visual Studio also starts the database.

To run from a terminal instead:

```sh
docker compose --env-file .env -f docker-compose.yml up -d --wait db
dotnet run --project RatBot --launch-profile RatBot.Local
```

RatBot applies database migrations and registers commands automatically on startup.

Stopping the bot leaves PostgreSQL running. `docker compose down` stops the local stack and keeps its data for next time.

## Contributing

Keep pull requests focused, follow the existing formatting rules in `.editorconfig`, and include tests for changed behaviour.

Code is organised by feature under `RatBot/Features/`. Each feature keeps its commands, gateway handlers, operations, and models together; database stores and EF mappings live in its `Persistence/` subfolder. Shared Discord interaction code lives in `RatBot/Interactions/`, gateway lifecycle code in `RatBot/Gateway/`, and general helpers in `RatBot/Common/`. The database context and connection setup live in `RatBot/Infrastructure/Data/`, with migration history in `RatBot/Infrastructure/Migrations/`. Registrations are explicit in `RatBot/DependencyInjection.cs`.

Build and test from the repository root:

```sh
dotnet build RatBot.slnx --configuration Release
dotnet test RatBot.slnx --configuration Release --results-directory ./TestResults
```

The PostgreSQL integration tests use Testcontainers and need Docker running. They create their own test databases; the development bot does not need to be running. For a focused change, run the affected test project or use `--filter`.

See the [changelog](CHANGELOG.md) for release history.

Write changelog entries around what users will notice, need to do differently, or can no longer rely on. Use the `Internal` component for refactors, build tooling, and other changes without user-facing effects, with the usual Added/Changed/Fixed kinds. Internal entries remain in the full changelog; Discord announcements link to them in a small-text footer instead of listing them. Releases containing only internal changes are announced as maintenance releases with no user-facing changes.
