# Changelog

Release notes are curated by hand with [Changie](https://changie.dev/).

# v1.0.3 - 2026-11-09
## Moderation
### Added
- <@&1328097118644338779> and above can approve an unapproved ticket by reacting with ✅, 👍, or :ratified: to its current ticket-channel entry. Sylvan records the reacting moderator as the approver and clears all reactions once after approval. Later reactions do nothing; reactions on old copies or DM reminders do not approve tickets.
### Changed
- Releases with moderation changes now ping <@&268886789983436800>. Staff-role mentions in release announcements also notify the named roles, once per role for each release.
### Fixed
- <@&1522665207225127034> and above can save ticket comments while leaving the duration unchanged, including when a timeout has changed on Discord. Duration changes remain available in the same editor. Comment edits preserve expiry times and paused expiry. Notes in DMs show only a comment field. Working on one ticket no longer holds up unrelated tickets, and DM reminders retain the latest saved details. SylvanMail's anonymous, signed, and cancel controls avoid unnecessary pauses.
## Internal
### Added
- Record ticket lock wait and hold times with the resource and operation identifiers, plus reaction approval queue delays and processing stages.
### Changed
- Submit independent SylvanMail draft-reaction changes concurrently and use Discord's precise relative rate-limit reset instead of the SDK's timestamp-based estimate. Keep per-reaction timings and log the SDK's estimated lag.
# v1.0.2 - 2026-11-08
## General
### Fixed
- SylvanMail now handles separate conversations concurrently, so a slow reply or reaction in one conversation no longer holds up every other conversation.
## Moderation
### Fixed
- <@&1328097118644338779> and above can read hidden tickets, review the queue, and approve tickets without access to their publication channels. Reviewing tickets avoids repeated access checks, and the approval summary includes pending tickets from other ticket channels.
- <@&1522665207225127034> and above can look up tickets, create notes, correct or revert tickets they are authorised to change, and receive ticket reminders without access to the ticket publication channel. Apps -> List Tickets replies only to you; /ticket show remains public in the channel where it is used.
## Internal
### Added
- Add correlated end-to-end interaction timings and stage timings for ticket processing and SylvanMail, including queue and lock waits, database work, Discord requests, publication, attachment downloads, and reaction rate-limit diagnostics.
# v1.0.1 - 2026-11-08
## Moderation
### Changed
- Make /ticket show and /ticket showhidden responses public, including comment attachments.
- ModMail drafts now offer three reactions: levitate sends anonymously, person_standing sends with your username, and x cancels. Mentioning Sylvan or replying with the reply ping enabled prepares the same draft.
# v1.0.0-rc.9 - 2026-11-08
## Moderation
### Fixed
- Ticket numbers in the approval summary link to their messages again. An imported ticket links to its original legacy post until its Sylvan copy is available.
# v1.0.0-rc.8 - 2026-11-08
## Moderation
### Fixed
- The approval summary now counts the same pending tickets as /ticket review, including archived cases. Each entry shows the ticket number, member, action and responsible moderator.
# v1.0.0-rc.7 - 2026-11-08
## Moderation
### Changed
- New ticket embeds show the user and moderator IDs as plain text. Only bans show the thumbnail GIF. The 250 most recent Mathematics tickets also use this format.
# v1.0.0-rc.6 - 2026-11-08
## Moderation
### Changed
- For the November cutover, the live channel is reserved for new tickets and recent legacy notes or active temporary restrictions issued in the preceding 24 months. Other visible legacy records belong in the archive.
# v1.0.0-rc.5 - 2026-11-08
## Moderation
### Fixed
- Staff notes now use the same embed layout as the other moderation tickets, with the legacy white colour, original timestamp, subject, moderator and comment. Use Apps → Manage Ticket to edit a note.
# v1.0.0-rc.4 - 2026-11-08
## Moderation
### Changed
- For the November ticket cutover, Sylvan posts the remaining imported notes and active temporary restrictions in the live staff channel first, then resumes the archive. Newly issued tickets continue to appear immediately.
### Fixed
- Omit empty code blocks from attachment-only message deletion logs.
# v1.0.0-rc.3 - 2026-11-08
## Moderation
### Changed
- For the November Mathematics cutover, visible legacy notes and restrictions still active with a future expiry were assigned to the live channel. They keep their place after expiry. Other visible records from the old bot were assigned to the archive.

# v1.0.0-rc.2 - 2026-11-08
## Moderation
### Changed
- During the Mathematics cutover, newly issued tickets can reach staff while Sylvan posts the legacy records in number order.

# v1.0.0-rc.1 - 2026-11-08
## Administration
### Added
- Use /config tickets to choose the staff channel, set the DM reminder interval, track role grants and exempt moderators with selected roles from review.
## Moderation
### Added
- Sylvan records bans, kicks, timeouts, voice mutes, voice deafens and configured role grants. Use /ticket note to add a staff note, including for a member who has left. Look up a case by ticket number, user or Sylvan message link.
- Complete a ticket from the Edit button in your DM or use /ticket edit. Newest prompts arrive first. Use /ticket append to add a follow-up while keeping the original comment.
- Open the review queue from the summary at the bottom of the tickets channel or with /ticket review. Senior moderators can approve, edit or skip a ticket. Approval survives later comment and duration edits.
- Use Revert Ticket to lift an active restriction and keep the record visible. Hide Ticket lifts an active restriction before removing the channel post. Restoring the record leaves the sanction lifted.
- Timed restrictions show the full expiry date and time alongside a relative timestamp. Timeout tickets support durations beyond 28 days and preserve the intended end time across rejoins. Direct Discord edits update the ticket.
- Automod ban tickets retain the rule’s original deadline. Editing the ticket changes when Sylvan lifts the ban.
- New SylvanMail staff threads include the member’s visible ticket list. The staff guide covers ticket lookup, corrections, review, reversals and troubleshooting.
# v0.4.0-rc.5 - 2026-11-07
## Administration
### Changed
- Administrators now manage role persistence with /config persistence add, remove and list. The role list is public and uses bullet points, including blocked or deleted roles.
# v0.4.0-rc.4 - 2026-11-06
## Moderation
### Changed
- Bulk deletion logs attach a readable text chat log with UTC message times, channel names and usernames instead of a JSONL file.
# v0.4.0-rc.3 - 2026-11-06
## Moderation
### Fixed
- Automod bans and mutes use the legacy reason text: the matched pattern number followed by a quoted excerpt of the message.
# v0.4.0-rc.2 - 2026-11-06
## Internal
### Fixed
- Validate SylvanMail's authenticated application before connecting and its bot identity after gateway readiness, avoiding a startup failure in Discord.NET's socket REST wrapper.
# v0.4.0-rc.1 - 2026-11-06
## General
### Changed
- Contact staff by messaging the dedicated SylvanMail bot. Main Sylvan no longer relays DMs. Existing cases and staff reply controls are preserved; Discord privacy settings and shared-server eligibility may limit who can contact the new bot.
## Internal
### Changed
- Run SylvanMail's DM connection alongside main Sylvan with a separately provisioned token and verified bot ID. Preserve protected deployment environment-file ownership and permissions when updating the image.
# v0.4.0-alpha.2 - 2026-11-03
## Moderation
### Added
- Senior Moderators: automod Ban rules now ban users whose messages match a rule. Set a duration for a temporary ban or leave it blank for a permanent ban.
### Changed
- Senior Moderators: show automod rules in a public paginated list with up to 20 entries per page and spoilered patterns. Administrators: split automod configuration into set, exempt and show; choose an action from the selector or omit it to enable or disable all four actions together.
## Internal
### Changed
- Deploy committed release batches automatically on master; prepare releases locally instead of batching changes in CD.
# v0.4.0-alpha.1 - 2026-11-02
## Administration
### Fixed
- Allow valid Working Hours ping, eligible and exempt role changes to save successfully.
## Moderation
### Added
- Add configurable word and regex automod rules, message deletion and timeouts, with role exemptions. Viewing and managing rules requires View Audit Log; administrators manage role exemptions and can enable or disable each action separately without losing rules.
## Internal
### Changed
- Use CsCheck properties for Working Hours interval membership and equivalent fractional times, replacing repetitive fixed-offset examples.
- Support numbered alpha, beta and release-candidate versions with optional build metadata in the deployment workflow, and colour release announcements by release channel.
# v0.3.0 - 2026-10-29
## Administration
### Added
- Add senior moderator commands to persist safe roles across member departures and rejoins.
# v0.2.1 - 2026-10-28
## Moderation
### Fixed
- Respect Discord reaction rate limits so pending SylvanMail replies show both 📤 to send and ❌ to cancel, and successful delivery leaves ✅ on the original forum message after clearing its reactions.
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
