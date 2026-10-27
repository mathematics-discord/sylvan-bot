# Sylvan Bot

A Discord bot built with C#, Discord.NET and PostgreSQL.

## Run locally

Install the .NET SDK specified in [global.json](global.json) and Docker with Compose. Invite a development bot to a test
server and enable **Server Members Intent** and **Message Content Intent** in
the [Discord Developer Portal](https://discord.com/developers/applications).

From the repository root:

```sh
cp env/local.env.example .env
```

Set `DISCORD__TOKEN` in `.env`. The example supplies the local database settings; Git ignores `.env`. To reuse a
database, keep its `DB__DATABASE` value and set `SYLVAN_BOT_POSTGRES_VOLUME` to its volume name.

| Editor        | Launch                                                                                                                            |
|---------------|-----------------------------------------------------------------------------------------------------------------------------------|
| Rider         | Open `SylvanBot.slnx` and run **Sylvan Bot Local with database**.                                                                 |
| VS Code       | Install the recommended **C# Dev Kit** extension and launch **Sylvan Bot Local with database**.                                   |
| Visual Studio | Open `SylvanBot.slnx`, set `SylvanBot` as the startup project, and run **Debug / SylvanBot.Local** with build-before-run enabled. |

These configurations start PostgreSQL and wait for it to be ready. Visual Studio also starts the database during
ordinary Debug builds.

To run from a terminal:

```sh
docker compose --env-file .env -f docker-compose.yml up -d --wait db
dotnet run --project SylvanBot --launch-profile SylvanBot.Local
```

The bot applies database migrations and registers commands on startup. Stopping it leaves PostgreSQL running.
`docker compose down` stops the local stack and preserves its data.

[SylvanMail](docs/src/content/docs/commands/sylvanmail.md) relays DMs to a private staff forum. Configure it with
`/config sylvanmail enable:<forum-channel> role:<role>` (Administrator only). The role is a ping target; anyone with thread
write access can draft an anonymous or signed reply and confirm it with the send reaction. Incoming DMs relay automatically.

## Development

Code is grouped under `SylvanBot/Features/`, with feature persistence in `Persistence/` subfolders. Shared interactions,
gateway lifecycle and helpers live in `Interactions/`, `Gateway/` and `Common/`. Database setup is in
`Infrastructure/Data/`; migrations are in `Infrastructure/Migrations/`. Registrations are explicit in
`DependencyInjection.cs`.

Follow `.editorconfig`, keep changes focused and run relevant checks from the repository root:

```sh
dotnet build SylvanBot.slnx --configuration Release
dotnet test SylvanBot.slnx --configuration Release --results-directory ./TestResults
```

Use `--filter` for focused tests. Existing PostgreSQL integration tests require Docker and create their own databases;
the bot need not be running.

See [CHANGELOG.md](CHANGELOG.md) for release history. Changie fragments describe user-visible changes; use the
`Internal` component for maintenance. Internal entries appear in the full changelog and are linked from Discord
announcements rather than listed as user-facing changes.
