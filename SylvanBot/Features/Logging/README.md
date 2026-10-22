# Moderation logging

Administrators can use `/logging config` in a guild text channel or thread to view
settings or change `enabled`, `delete-log-channel`, and `edit-log-channel`. Omitted
options preserve existing values. Edit and delete destinations are separate, with
no fallback between them. Both output channels are excluded from logging.
`exclude`, `include`, and `exclusions` manage guild-scoped exclusions; these extend
from categories to their channels and from thread parents (including forums) and
their categories to threads.

Evidence is best effort and comes only from Discord.NET's message cache. Eviction,
expiry, and bot restarts lose historical content and attachments. An uncached edit
shows an unavailable before version; an uncached deletion keeps the known IDs with
unknown author/content. Identical-content updates and identifiable bot, webhook,
and system messages are skipped. A deletion may link to the preceding surviving
message when that bounded lookup succeeds.

Attachments download sequentially when constructing deletion logs. Expired URLs,
HTTP failures, timeouts, and byte/count limits omit attachments while preserving
the text log. Bulk summaries and displayed content explicitly mark truncation.
Delivery failures are logged; there is no durable delivery or retry workflow.

The `retention-period` slash option is removed. Its old database column remains
inert, preserving stored values. The legacy `observed_messages` and
`message_log_entries` tables and rows remain physically present but unmanaged by
the active EF model; logging no longer writes or cleans them. Automatic retention
of posted Discord logs is outside this change.

## Later manual staging checks

These checks are instructions for a human in a staging guild, not automated or
live verification performed by this change:

- Edit a cached user message and check the ANSI before/after output in the edit
  destination. Trigger an identical-content update and confirm no extra log.
- Delete cached and uncached messages; check available evidence versus explicit
  unknown content/author and known IDs. Check the predecessor link when available.
- Delete a message whose attachment download fails; confirm the text log and
  attachment omission notice still arrive.
- Bulk-delete cached and uncached messages with attachments; check honest counts,
  readable bounded details, and visible omission/truncation notices.
- Exclude a category, a thread parent/forum, and a thread; confirm inherited
  exclusions suppress logs. Confirm both configured output channels are skipped.
- Remove a configured destination; confirm logging skips it with a diagnostic.
  Confirm edits and deletes never fall back to the other destination.
