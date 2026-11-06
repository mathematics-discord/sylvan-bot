---
title: Moderation logging
description: Understand edit logs, deletion evidence and bulk transcripts.
---

Sylvan can publish message edits and deletions to separate log channels. Administrators choose destinations and exclusions with [/config logs](../../commands/logging/).

## Edits and single deletions

Edit logs identify the author and channel, link to the message and show before-and-after text with changes highlighted. Earlier text can be unavailable. Long text is truncated to fit the log.

A single-deletion log shows the cached author and text, with a link to the preceding message when one can be found. Sylvan attempts to download the deleted message?s attachments at deletion time and upload them with the log. Downloads can fail or exceed limits; the log reports omitted attachments. A deletion log is not a guarantee that every attachment was recovered.

## Bulk deletions

Bulk deletions produce a summary and a `bulk-delete.txt` attachment containing eligible cached messages in chronological order. Each line includes the original message time in UTC, channel name, username and text. Multiline messages appear on multiple lines. User IDs stand in for usernames that cannot be resolved.

The summary includes the deletion time and, when audit information can identify them, the moderator who performed it. That moderator is distinct from the authors in the transcript. Bulk transcripts contain message text; they do not recover attachments from each deleted message.

## What can be recovered

Eligible message snapshots last **24 hours from message creation**, including across restarts. Editing a message does not extend that window. Messages Sylvan did not observe, or whose snapshots have expired, cannot supply deletion evidence. If no eligible cached messages remain, no deletion log is published. A bulk transcript may therefore contain fewer messages than were deleted.

Channel, thread, forum and category exclusions apply to the relevant messages beneath them. Log destination channels and their threads are excluded from logging back into themselves. Published logs are not automatically deleted when the cache window ends.
