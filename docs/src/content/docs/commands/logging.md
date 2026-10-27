---
title: /logging
description: Configure edit and deletion logs and choose which channels to exclude.
---

Run these commands in a server text channel or thread. Replies are visible
only to you.

## View or change log destinations

Run `/logging config` without options to see the current settings.

To set destinations and enable logging:

```text
/logging config enabled:true delete-log-channel:#deleted-messages edit-log-channel:#edited-messages
```

| Option | What it changes |
| --- | --- |
| `enabled` | Enables or disables moderation logging. |
| `delete-log-channel` | Destination for deleted messages and bulk deletions. |
| `edit-log-channel` | Destination for message edits. |

All options are optional. Omitted settings stay as they are. Destinations
can be text or announcement channels. Set at least one destination before
enabling logging; each kind of log needs its own configured destination.

To pause logging without discarding its destinations:

```text
/logging config enabled:false
```

## Exclude a channel

```text
/logging exclude
/logging exclude channel:#private-discussion
```

Leave `channel` out to exclude the current channel. You can select a channel,
thread, forum or category. An exclusion on a parent channel or category
also applies to messages beneath it.

## Remove an exclusion

```text
/logging include channel:#private-discussion
```

This removes the exclusion for the selected channel. Leave `channel` out
to use the current channel. A parent channel or category exclusion can
still prevent messages being logged; remove that exclusion separately.

## List exclusions

```text
/logging exclusions
```

Sylvan lists the excluded channels, or confirms that there are none. Log
destination channels and their threads are not logged back into themselves.
