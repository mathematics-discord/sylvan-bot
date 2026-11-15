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
| Server voice mute or deafen | A descriptive duration or `permanent`. Expiry does not lift the restriction; use Revert Ticket or Hide Ticket. |
| Kick or note | No duration to set. |

Use positive whole units such as `90 minutes`, `2 days 6 hours`, or `3 weeks`.
A month means 30 days and a year means 365 days. Changing the duration on an
ended ticket corrects its record without reapplying the restriction.

Timeout extensions and reductions update the same active ticket, including
changes made directly in Discord. A new timeout after the previous one has
ended gets a new ticket.

Ban and role expiry can wait for Sylvan to check Discord's audit log. If the
expiry has passed but the restriction is still active, [check the ticket status](../../../help/tickets/).
Voice durations describe the record only. Use **Revert Ticket** or **Hide Ticket**
to lift the mute or deafen before its descriptive end.

## Lift a restriction now

1. On the ticket message choose **Apps → Revert Ticket**, or run
   `/ticket revert id:1234`.
2. Check the reply and the member’s actual state. A confirmed reversal
   unbans the user, clears the timeout, removes the tracked role, or lifts the
   voice mute or deafen. Sylvan checks Discord again before reporting success. The visible ticket records
   who lifted it and shows **(REVERTED)**. It no longer needs approval.
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
owner exception. Lifting voice restrictions requires Mute Members or Deafen
Members, respectively; Discord may reject the change if the member is not
connected to voice.
See [who can correct each action](../../../features/tickets/#who-can-do-what).


## Members who have left

You can create notes and edit, approve, revert, or hide tickets using the saved
user ID after the member leaves. Creating a note, editing comments or historical
details, approving, and changing historical visibility do not check membership.

Changing an active timeout duration checks whether there is a live timeout to
change. If the member has left, Sylvan saves the details; the remaining timeout
can still be enforced if they rejoin. Reverting or hiding the ticket ends that
future enforcement. Reverting or hiding a role ticket also cancels its saved
role restoration. Unbanning works by user ID, without requiring membership.

A failed Discord request does not count as confirmation that someone left.
If a live change cannot be confirmed, Sylvan reports the failure rather than
marking the ticket reverted or hidden.
