# Changelog

Release notes are curated by hand with [Changie](https://changie.dev/).

# v0.2.0 - 2026-10-28
## General
### Added
- You can now DM @Sylvan to contact the moderators as an alternative to @ModMail. SylvanMail is out of beta and will replace @ModMail as the sole way to send a private message to the moderation team.
## Moderation
### Changed
- SylvanMail is out of beta. Authors can cancel pending replies with ❌ and edit them to prepare them again. Successful delivery clears all reactions and leaves a single ✅. Explicit mentions remain anonymous; replies with the reply ping enabled include the author's username.
# v0.1.19 - 2026-10-28
## Moderation
### Added
- Request a ticket summary after the opening message and attachments in each new SylvanMail post. Lookup failures do not affect mail delivery.
# v0.1.18 - 2026-10-28
## General
### Removed
- Remove /emoji usage and automatic emoji usage tracking. The database migration drops EmojiUsageCounts and permanently deletes the stored counts.
## Administration
### Changed
- Move logging, Working Hours, role-colour and image-spam settings under /config. All configuration commands now require Administrator permission. Logging setup is available at /config logs setup; personal /working-hours and /colour commands are unchanged.
### Removed
- Remove the Quorum commands and their saved configuration. The database migration deletes Quorum settings and voter-role mappings; back up the database before upgrading if these records are needed.
## Internal
### Changed
- Replace ErrorOr and the custom Option type with DotNext results and optional values, preserving descriptive failures and explicit-null message edits.
# v0.1.17 - 2026-10-28
## Moderation
### Added
- Retain eligible message snapshots for 24 hours across bot restarts so edit and deletion logs can preserve recent evidence after deployments.
# v0.1.16 - 2026-10-27
## Administration
### Added
- Add administrator-only `/config rolereact` with `add` and `remove` actions for configuring message reaction roles. Members gain the selected role when reacting and lose it when unreacting. Removing a mapping keeps existing roles. Roles with staff or moderation permissions, including channel overrides, are rejected.
# v0.1.15 - 2026-10-27
## Internal
### Fixed
- Remove redundant permission preflights from outgoing SylvanMail replies. Trust Discord to enforce forum access while preserving mapped-case validation, author-only confirmation and duplicate-send protection. Stop reopening threads for outgoing DMs and surface unexpected draft failures in logs.
# v0.1.14 - 2026-10-27
## Internal
### Changed
- Require author confirmation for SylvanMail reply drafts, re-read edited content at confirmation, support anonymous and signed replies, and separate thread write permissions from the ping role. Replace routine thread receipts with success reactions and delivery errors, retain DM outcome notices, format inbound member mentions, and derive three-hour gap pings from DM history.
### Fixed
- Create a new SylvanMail case for incoming DMs when the mapped forum post is deleted, inaccessible or outside the configured forum, retaining historical mappings.
- Include recovery instructions with every SylvanMail DM error: correct rejected content, retry a busy service later, or contact user 203864799870844928 directly by DM or another staff member. Ask staff to reconcile partial and uncertain deliveries before resending.
# v0.1.13 - 2026-10-27
## Internal
### Fixed
- Allow SylvanMail forums to grant View Channel access to additional non-administrator roles.
# v0.1.12 - 2026-10-27
## Internal
### Changed
- SylvanMail is now available as an untested beta for private Senior Moderator correspondence. Bugs, incomplete delivery and lost messages are possible; live Discord behaviour has not been verified.
# v0.1.11 - 2026-10-26
## General
### Changed
- Move colour selection to /colour set and make configured colour mappings available to everyone through /colour list, replacing /colour-admin list.
# v0.1.10 - 2026-10-26
## Internal
### Changed
- Add five-minute message-cache occupancy samples and write general file logs as JSONL with rendered messages and structured context. Retain 'human-readable' console output.
# v0.1.9 - 2026-10-26
## Moderation
### Added
- Attribute bulk deletions using matching audit-log entries, including ban-related deletions when identifiable.
### Changed
- Replace bulk deletion summaries and media attachments with JSONL exports containing cached message authors, channels, and full content.
### Fixed
- Skip deletion logs for messages missing from the cache (this preserves legacy bot behaviour).
# v0.1.8 - 2026-10-25
## Moderation
### Added
- Add /working-hours for recurring daily availability and administrator-only /working-conditions for persisted per-guild settings. Global disable restores ordinary staff ping-role defaults while exempt staff retain manual control; saved schedules resume when re-enabled. Changing the ping role requires disabling first and leaves old role assignments unchanged. Time input uses the documented [RFC 3339 time-only profile](https://www.rfc-editor.org/rfc/rfc3339.html#section-5.6), with fixed offsets or named zones from the [IANA Time Zone Database](https://www.iana.org/time-zones).
# v0.1.7 - 2026-10-25
## Internal
### Removed
- Remove Bridge, including its WebSocket API and /bridge commands. Remove the stored Bridge channel configuration and the requirement for a dedicated Bridge credential.
# v0.1.6 - 2026-10-23
## Internal
### Added
- Add Bridge, an authenticated WebSocket API for explicitly bot-mentioned messages in configured channels, with scoped typing and replies. Manage channels using /bridge add, /bridge remove, and /bridge list. Set a dedicated BRIDGE__CREDENTIAL before starting the bot. Discord intents and automatic member downloading can now be configured for deployments without privileged intents.
### Changed
- Downversion SDK from net11.0-(preview) to net10.0
# v0.1.5 - 2026-10-21
## General
### Fixed
- Failed commands now complete pending responses where possible, explain expected failures, and show a generic message for unexpected errors.
## Moderation
### Fixed
- Concurrent quorum voter-role edits now preserve other roles and proportion changes.
- Quorum commands now acknowledge requests before database queries and member lookups, reducing interaction timeouts during slow operations.
## Internal
### Changed
- Simplified quorum commands, configuration and member counting using Discord.NET member access.
- Moved shared persistence models, the database context, and quorum logic into a separate application project, keeping Discord integration in the bot project.
- Mapped existing quorum tables in EF Core and replaced the Dapper configuration store.
- Replaced Meziantou.Analyzer with StyleCop, updated Roslynator and standardised C# formatting across the codebase.
# v0.1.4 - 2026-10-16
## Moderation
### Changed
- Edit and deletion logs can only show message history the bot still remembers; restarts or older messages may leave it unavailable. Deleted-message attachments are included only when they can still be retrieved.
### Fixed
- Fixed failures when displaying long exclusion lists or large bulk-deletion summaries.
- Fixed moderation logging holding up other bot activity.
### Removed
- Removed the retention-period option from `/logging config`.
## Internal
### Changed
- Consolidated bot and test projects and organised code by feature.
# v0.1.3 - 2026-10-15
## General
### Removed
- Removed the `/meta` and associated proposals commands due to lack of usage.
## Moderation
### Fixed
- Use cached Discord text in single-delete logs.
- Use cached Discord text in bulk-delete logs.
- Preserve empty and whitespace-only message text.
- Refresh edit evidence before log delivery.
- Send deletion logs only once.
- Unsubscribe the registered message handler.
- Capture text and attachment metadata before downloads.
- Preserve text evidence when metadata writes fail.
- Prevent delayed creates from overwriting edit evidence.
- Ignore stale attachment downloads.
- Preserve text when the attachment cache is full.
- Reuse downloaded attachments across text edits.
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
