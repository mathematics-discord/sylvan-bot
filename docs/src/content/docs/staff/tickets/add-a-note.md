---
title: Add a moderation note
description: Record a warning or other context in a member’s history without applying a restriction.
---

Use a note for something another moderator should know, such as a warning
you gave in chat. A note records information in the tickets channel and
the member’s ticket history. The member receives no DM or restriction.
If Discord already recorded a moderation action, [complete its ticket](../complete-a-ticket/)
to keep the reason with that action.

## Write the note

1. Open the member’s menu and choose **Apps → Add Moderation Note**. You can
   also run `/ticket note user:@member`. For someone who has left the server,
   enter their Discord user ID in `user`.
2. In **Comment**, describe what happened. Record any warning you gave and
   include a message link where it helps.
3. Submit the note. Sylvan confirms the new ticket number privately and
   publishes the record in the configured tickets channel.

To save the note immediately, include `comment` in the command.

```text
/ticket note user:@member comment:Asked the member to keep recruitment posts in the designated thread.
```

A note can contain up to 3,000 characters. It has no restriction to time or
reverse, and it stays out of the review queue.

## Correct or withdraw a note

Use [`/ticket append`](../../../commands/tickets/#ticket-append) to add context
or **Edit** to correct the original text. Staff who can edit a visible note
can also [hide it](../hide-or-restore/). Timeout Members permits editing your
own notes; View Audit Log permits editing anyone’s. Restoring a hidden note
requires senior staff.
