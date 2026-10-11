# Changelog

Release notes are curated by hand with [Changie](https://changie.dev/).

## v0.1.0 - 2026-10-11

### Changed

- **Administrators:** The “Yoink emoji” message command now accepts a single custom server emoji anywhere in the selected message, including among surrounding text, and rejects messages with multiple custom emojis.
- **All:** RatBot now registers its commands separately in each server at startup and when it joins a new server.
- **Administrators:** `/colour-admin upsert` replaces `/colour-admin add` for creating grants from source roles to colour roles. Use the numeric `id` shown by `/colour-admin list` to update or delete a grant. A source role can grant several colours, several sources can grant the same colour, and `@everyone` can be a source.
- **All:** `/colour` replaces `/colour swap` and `/colour remove`. Choose an available colour to wear it immediately, or use Reset to remove it. Choices combine the grants from all your source roles and use the colour roles' names. Your current Discord roles determine which colour you wear; losing access to that colour removes it.
