---
title: Change a duration or lift a restriction
description: Set the total duration of a moderation action, check its expiry, or revert it while retaining the ticket.
---

To change **when** a restriction ends, edit its duration. To lift it **now**
and keep its ticket visible, use **Revert Ticket**. Start by checking the
ticket number, member, and current restriction.

## Change the duration

Choose **Edit** and enter the new **Duration**. To make the same change
by command, replace `1234` with the ticket number.

```text
/ticket set id:1234 duration:7 days
```

Duration is the **total time from the original action**. A ban issued on
Monday at noon and changed on Wednesday to `7 days` expires the following
Monday at noon. Check **Expires** on the ticket after saving. A total that
has already elapsed can end an active restriction.

| Action | Accepted duration |
| --- | --- |
| Timeout | A finite duration. For a timeout longer than 28 days, enter the full duration. Sylvan renews Discord’s timeout as needed and checks the remaining time when the member rejoins. |
| Ban or tracked role | A finite duration or `permanent`. |
| Server voice mute or deafen | A descriptive duration or `permanent`. Sylvan does not lift voice restrictions automatically. |
| Kick or note | No duration to set. |

Use positive whole units such as `90 minutes`, `2 days 6 hours`, or `3 weeks`.
A month means 30 days and a year means 365 days. Changing the duration on an
ended ticket corrects its record without reapplying the restriction.

Timeout extensions and reductions update the same active ticket, including
changes made directly in Discord. A new timeout after the previous one has
ended gets a new ticket.

Ban and role expiry can wait for Sylvan to check Discord's audit log. If the
expiry has passed but the restriction is still active, [check the ticket status](../../../help/tickets/).
Voice durations describe the record only: remove the mute or deafen manually
in Discord, then wait for the ticket to reflect that change.

## Lift a restriction now

1. On the ticket message choose **Apps → Revert Ticket**, or run
   `/ticket revert id:1234`.
2. Check the reply and the member’s actual state. A confirmed reversal
   unbans the user, clears the timeout, or
   removes the tracked role. The visible ticket records who lifted it.
3. If Sylvan reports an unconfirmed or newer action, reopen the ticket
   before trying again. Discord may already have accepted the change.

A kick cannot be undone. A note has no restriction to lift, and a restriction
that has already ended cannot be reverted again.

You need permission to correct the ticket **and** the Discord permission
for that restriction, or **Administrator**. Where the member is still
present, their highest role must be below yours and Sylvan’s. The server
owner is exempt from the caller’s role-position check. A moderator cannot
reverse a restriction on themselves or the server owner.

Sylvan also needs the corresponding action permission. A tracked role must
be an ordinary role below Sylvan and below the moderator, subject to the same
owner exception. Voice restrictions must be reversed manually in Discord.
Wait for Sylvan to observe that reversal before hiding their tickets.
See [who can correct each action](../../../features/tickets/#who-can-do-what).
