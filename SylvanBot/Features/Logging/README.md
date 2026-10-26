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
shows an unavailable before version; uncached deletions are skipped. Bulk deletion
logs export only cached qualifying messages, and send nothing when none remain.
Identical-content updates and bot, webhook, and system messages are skipped.
A deletion may link to the preceding surviving message when that bounded lookup
succeeds.

Attachments download sequentially when constructing single-message deletion logs. Expired URLs,
HTTP failures, timeouts, and byte/count limits omit attachments while preserving
the text log. Displayed single-message content explicitly marks truncation.
Delivery failures are logged; there is no durable delivery or retry workflow.

Bulk deletions attach only `bulk-delete.jsonl`, with one JSON object per cached
qualifying message. Each object contains exactly `author_id`, `channel_id` (decimal
strings), and `content` (the full original text, including empty content). Message
line breaks are escaped within each JSON line. Deleted media/files are not downloaded
for bulk logs.

The bulk log notice names the moderator when a recent audit entry can be matched.
This requires the bot's `View Audit Log` permission. The lookup prefers a unique
bulk-delete entry matching the channel and original event count; a unique ban of
the sole cached author can also identify the actor. Entries must be within ten
seconds of the deletion event. The lookup makes up to three attempts within five
seconds; attribution is best effort because deletion events contain no actor ID.
Missing permission, failed or timed-out lookups, and ambiguous matches retain the
JSONL export with "Moderator could not be determined." Message authors in the
export always come from cached message data, independently of the audit actor.

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
- Delete cached text and attachment-only messages; check the evidence and the
  predecessor link when available. Delete an uncached message and confirm no log.
- Delete a message whose attachment download fails; confirm the text log and
  attachment omission notice still arrive.
- Bulk-delete a mix of cached and uncached messages; check the short notice and
  JSONL attachment. Verify exactly three fields per line and full content for every
  cached qualifying message, with no media/file downloads. Confirm an entirely
  uncached batch produces no log.
- Ban a user through Discord with message deletion enabled, then purge messages
  from multiple authors. Use distinct moderator/author accounts and verify that
  the notice names the moderator while JSONL rows retain their original authors.
- Check attribution when audit entries arrive late, multiple entries match, or the
  bot lacks `View Audit Log`. Confirm unmatched events still deliver the JSONL with
  the explicit unattributed notice.
- Exclude a category, a thread parent/forum, and a thread; confirm inherited
  exclusions suppress logs. Confirm both configured output channels are skipped.
- Remove a configured destination; confirm logging skips it with a diagnostic.
  Confirm edits and deletes never fall back to the other destination.
