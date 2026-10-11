# RatBot

A Discord bot built with .NET 11.0 and C# 15, using Discord.Net and optional observability.

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

## Role colours

Administrators can use `/colour-admin upsert source display` to create a grant from an existing uncoloured source role to an existing coloured role. It returns a numeric mapping ID. Pass `id` to `upsert` to update that mapping, or to `/colour-admin delete` to remove it; `/colour-admin list` shows the IDs. Sources and colours can each occur in several grants; the exact guild/source/colour combination is unique. An `@everyone` source applies implicitly to every member.

Members can use `/colour` to select and immediately equip one colour from the distinct union granted by their held sources, or press the red Reset button to wear none. Discord's current roles are the equipped state; choices are not persisted or restored. Switching removes other coloured roles before adding the selection, including colours outside configured grants, and preserves uncoloured roles. A failed removal stops the switch.

Member-role events, startup member downloads, mapping edits and `/colour-admin sync` check the same rules. A sole eligible colour is kept; loss of its last grant removes it. Gaining access equips nothing. Participating members wearing several colours lose all of them. Members without configured sources are otherwise outside automatic enforcement. Invalid mappings are unavailable independently. Mapping edits recheck former participants in the current process and report incomplete checks; there is no periodic worker or persistent cleanup queue. Discord's server setup controls role presentation.

## Optional: Observability

The project includes an optional observability stack (Grafana, Loki, OpenTelemetry) for monitoring and logging.

### Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `OTEL__Logs__Endpoint` | OpenTelemetry Collector endpoint | (Disabled if empty) |
| `GRAFANA__ADMIN__USER` | Grafana admin username | `admin` |
| `GRAFANA__ADMIN__PASSWORD` | Grafana admin password | `admin` |

To enable logs export, set `OTEL__Logs__Endpoint` to `http://localhost:4317` (if running bot locally) or `http://otel-collector:4317` (if running bot in docker).

### Start Observability Stack

```bash
docker compose --env-file .env up loki grafana otel-collector -d
```

Grafana will be accessible at `http://localhost:3000` (Default: `admin`/`admin`).

## Development

### Release notes and deployment announcements

Release notes are manually curated with [Changie 1.26.0](https://github.com/miniscruff/changie/releases/tag/v1.26.0), independently of commit messages. Install that version's [Windows ZIP or Linux tarball](https://github.com/miniscruff/changie/releases/tag/v1.26.0), extract `changie` (or `changie.exe`), put it on your `PATH`, and verify with `changie --version`. Changie is a local release tool; it is not part of the bot or production image.

From the repository root, create each entry with `changie new`. Choose Security, Removed, Deprecated, Added, Changed, or Fixed. The YAML fragments live in `.changes/unreleased/`. Preview the pending changelog with `changie merge --dry-run --include-unreleased "## Unreleased"`; preview a proposed batch with `changie batch 0.5.0 --dry-run` (substitute your intended version). Neither preview consumes fragments or updates `version.json`.

To prepare a release, choose an explicit version newer than `version.json` and run `bash scripts/prepare-release.sh 0.5.0` from the repository root (substitute that version). The command batches pending entries into `.changes/v0.5.0.md`, regenerates `CHANGELOG.md`, and updates only the `version` setting in `version.json`. It leaves all changes local. Review `git diff`, `git status`, the version file, and the full changelog; edit curated notes if needed, then rerun `changie merge --include-unreleased "## Unreleased"` to reflect release-note edits in `CHANGELOG.md`. Commit the reviewed files on a review branch, bring that commit onto `master` by your normal review process, and push `master` yourself:

```bash
git add .changes CHANGELOG.md version.json
git commit -m "Prepare v0.5.0"
# Once the reviewed release commit is on local master:
git push origin master
```

Set the repository Actions secret `DISCORD_RELEASE_WEBHOOK_URL` to a Discord webhook URL to enable announcements. After a successful automatic production deployment of a push to `master`, the workflow compares the push's before and after commits. It posts only if `version.json` advanced and `.changes/v<version>.md` was newly added in that push. It reads those committed notes at the deployed commit, so CI does not regenerate release notes. Manual and shared deployments, failed production deployments, ordinary pushes, and this migration do not announce. Without the secret, the workflow records a skip in its summary and deployment still succeeds. Rerunning a successful announcement job, or retrying after some messages were delivered, can repeat messages.

### Project Structure

- `RatBot`: Executable, Discord integration, features, and shared persistence.
- `RatBot.Domain`: Models and rules for features that have not yet been consolidated.

### Running Tests

The infrastructure integration tests use Testcontainers and require access to a running Docker daemon.

```bash
dotnet test
```
