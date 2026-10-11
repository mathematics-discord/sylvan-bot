# Changelog

Release notes are curated by hand with [Changie](https://changie.dev/).

# v0.1.2 - 2026-10-11
## General
### Added
- WE .NET 11 NOW. UNION TYPE `Option<T>` IS NOW MANDATORY.
### Removed
- Removed Grafana, Loki, and OpenTelemetry. This was... not being used.
# v0.1.1 - 2026-10-11

## General

### Changed

- `/colour` replaces `/colour swap` and `/colour remove`. Pick a colour to apply it, or choose Reset to clear it. Available colours depend on your server roles. If you lose access to your current colour, it’s removed automatically.

## Administration

### Changed

- `/colour-admin upsert` replaces `/colour-admin add` for creating grants from source roles to colour roles. Use the `id` shown by `/colour-admin list` to update or delete a grant. A source role can grant several colours, several sources can grant the same colour, and `@everyone` can be a source.

## Moderation

### Changed

- The `/yoink` command now accepts a single custom server emoji anywhere in the selected message, including among surrounding text, and rejects messages with multiple custom emojis

# v0.1.0 - 2026-10-11
