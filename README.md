# RatBot

A Discord bot built with .NET 11.0 and C# 15, using Discord.Net with console and daily file logging.

## Prerequisites

- [.NET 11.0 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) (RC 1 or later)
- [Docker](https://www.docker.com/get-started) and [Docker Compose](https://docs.docker.com/compose/install/)
- A Discord Bot Token (from the [Discord Developer Portal](https://discord.com/developers/applications))

## Local development

### 1. Create the local configuration

```bash
cp env/local.env.example .env
```

Set `DISCORD__TOKEN` in `.env`. The bot registers commands in every guild it joins.

### 2. Start PostgreSQL

```bash
docker compose --env-file .env up -d --wait db
```

The database is exposed at `localhost:5432` using the `DB__*` values in `.env`. RatBot applies pending EF migrations when it starts. To stop it later, run `docker compose down`

### 3. Run or debug the bot

```bash
dotnet run --project RatBot --launch-profile RatBot.Local
```

### Troubleshooting

```bash
docker compose --env-file .env ps
docker compose --env-file .env logs db
```

PostgreSQL only uses `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` when its data volume is first created. If you change those settings after initial startup, either change them inside PostgreSQL or intentionally recreate the local volume with `docker compose down --volumes`.

## Command registration and cleanup

When Discord reports the bot ready, RatBot replaces its complete command set in every guild it belongs to. It also registers the same definitions when added to a new guild. This updates renamed commands, parameters, and subcommands and removes deleted commands in each guild. The bot never registers commands globally. Discord keeps global and guild registrations separately, so an old global registration survives until explicitly cleared.

The following maintenance commands use Discord's REST API and require `DISCORD__TOKEN` in the **process environment** for the application you intend to inspect. They do not load `.env`, start the bot, or connect to PostgreSQL. Run them from this repository after setting that variable. In PowerShell, you can set it without putting the token in shell history:

```powershell
$secret = Read-Host "Bot token" -AsSecureString
$env:DISCORD__TOKEN = [System.Net.NetworkCredential]::new("", $secret).Password
```

Supply the production bot token to inspect the production application; check the authenticated bot name and ID in the output.

```powershell
$guildId = Read-Host "Production guild ID"
dotnet run --project RatBot --no-launch-profile -- commands inspect global
dotnet run --project RatBot --no-launch-profile -- commands inspect guild $guildId
dotnet run --project RatBot --no-launch-profile -- commands clear global
dotnet run --project RatBot --no-launch-profile -- commands inspect global
dotnet run --project RatBot --no-launch-profile -- commands inspect guild $guildId
```

Use `commands clear global` to retire any global commands previously registered by this application. To clear an abandoned guild scope, inspect its ID and then invoke `commands clear guild` with that ID:

```powershell
$abandonedGuildId = Read-Host "Abandoned guild ID"
dotnet run --project RatBot --no-launch-profile -- commands inspect guild $abandonedGuildId
dotnet run --project RatBot --no-launch-profile -- commands clear guild $abandonedGuildId
```

You can supply multiple explicit guild IDs to `commands clear guild`. A clear removes **all application commands in that selected scope**, including user and message context-menu commands. It exits nonzero if authentication, inspection, or any requested cleanup fails. Do not clear a guild where the running bot should keep its commands.

Start the updated bot before clearing old global commands, so an older process cannot register them again on reconnect. For local development, run `dotnet run --project RatBot --launch-profile RatBot.Local`; production registration happens when the updated bot starts after deployment. Confirm guild registration in the bot log, inspect both scopes, clear the old global scope, then inspect both again. A Discord client may still display a cached command menu until it reloads; the bot cannot force an immediate refresh of every client.

## Logs

The bot writes to the console and to daily rolling files in `logs/` relative to its working directory. The files are `verbose-YYYYMMDD.log`, `debug-YYYYMMDD.log`, `info-YYYYMMDD.log`, `warning-YYYYMMDD.log`, and `error-YYYYMMDD.log`. Each includes its named severity and all higher severities; EF Core messages start at Warning. Each file series retains the latest 31 files.

For local runs, look in the bot process's working directory. Production mounts `./logs` beside its Compose file into `/app/logs`, so logs survive container replacement. On the VPS, that directory is `/opt/ratbot/production/logs`.

```bash
tail -F /opt/ratbot/production/logs/info-$(date +%Y%m%d).log
```

See [interaction logging](docs/interaction-logging.md) for correlation fields and logfile locations.

## Development

### Project Structure

- `RatBot`: Executable, Discord integration, features, and shared persistence.
- `RatBot.Domain`: Models and rules for features that have not yet been consolidated.

### Running Tests

The infrastructure integration tests use Testcontainers and require access to a running Docker daemon.

```bash
dotnet test
```
