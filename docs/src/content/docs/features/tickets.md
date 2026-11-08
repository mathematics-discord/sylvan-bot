---
title: Moderation tickets
description: Complete, find and review a moderation ticket, change a restriction or restore a hidden record.
---

If you have just taken a moderation action, look for a DM from Sylvan. Choose **Edit**, check the duration if one applies, and record why you took the action. If you already have the ticket open in the staff channel, use the message’s **Apps → Manage Ticket** menu. Both routes edit the same record.

## Where to find a ticket

On Mathematics, Sylvan posts new tickets in **〖tickets-new〗**. Imported notes and restrictions that were active with a future expiry when the channels were split also appear there. They stay there after expiry. Other visible legacy tickets, including permanent bans, kicks and expired restrictions, appear in **〖tickets-legacy〗**. Sylvan posts imported tickets in number order within each channel. Newly created tickets can appear between them. Use the same ticket number for lookups, edits and reviews in either channel.

## What gets a ticket?

Sylvan reads the server audit log for bans, kicks, timeouts, server voice mutes and deafens, and grants of roles an administrator has selected for tracking. Each ticket has a number, a subject and a responsible moderator. Staff can also create a note with `/ticket note`. A note is useful when the context matters but there was no Discord sanction.

For example, after kicking someone for repeatedly posting invite links, open the kick ticket and enter the reason and any evidence staff will need later. For a warning given in chat, use a note instead. When a member contacts SylvanMail, their new staff thread includes a list of their visible tickets.

## Complete or correct a ticket

1. Choose **Edit** in Sylvan’s DM, or run `/ticket edit id:1234` in the server.
2. Give enough context for another moderator to understand what happened. Include a message link if it will help them find the evidence. The comment limit is 3,000 characters.
3. For a timed restriction, check the **total duration from the original action**. Changing a two-day ban to seven days moves its expiry to seven days after the ban began. The expiry field shows the full date and time in your Discord locale and how long remains.

Use `/ticket append` to add a later development while keeping the original comment. Use `/ticket set` to replace a field. Ticket approval survives subsequent comment and duration edits. If Sylvan says the ticket has changed since you opened it, reopen the editor and check the latest details before saving.

Prompts work newest first. Use `/ticket top` in the server to request your newest outstanding DM again. If DMs are closed, you can still use the server editor. A ban or timeout can prompt you even when its audit reason already supplied the details.

## Find a record

Use `/ticket show user:@member` for a member’s history. A Discord user ID also works after the member has left. Use `id:` when you have a ticket number, or `message:` when you have a Sylvan ticket message link. Supply one selector at a time. A user lookup supports `page:`.

The ticket shows recent changes and indicates when earlier entries were omitted. A senior moderator can use `/ticket showhidden` to inspect hidden records. Legacy ticket numbers carry over, so use the number when you have an old bot’s message. Hidden records stay out of the tickets channel.

## Review the queue

Choose **Review tickets** on the summary at the bottom of the tickets channel, or run `/ticket review`. Your queue is private. It distinguishes tickets ready for a decision from tickets waiting for details or a pending Discord change.

- **Approve & next** records your approval and opens the next eligible ticket.
- **Edit** lets you correct the details before deciding. **Skip** leaves the ticket for another pass.
- **Refresh** picks up changes made while the queue was open. **Start again** returns to tickets you skipped.

For a single channel message, use **Apps → Approve Ticket**. Notes and review-exempt actions stay out of the queue. An exemption is determined from the acting moderator’s roles when a new action is captured. A later change to the exemption list applies to future captures.

## Revert, hide or restore

| What staff need | Action | Result |
| --- | --- | --- |
| Lift an active restriction and leave its explanation available | **Apps → Revert Ticket** or `/ticket revert id:1234` | Sylvan checks the current action, lifts it when permitted and retains the visible ticket. |
| Withdraw a record from the tickets channel | **Apps → Hide Ticket** or `/ticket hide id:1234` | Sylvan first lifts any active restriction. Once that succeeds, it hides the record and removes its own channel posts. If it cannot establish that the restriction was lifted, the ticket stays visible. |
| Make a hidden record visible again | `/ticket set id:1234 hidden:False` | Restores the record and allows a new channel publication. The restriction stays lifted. |

A note and a kick have no ongoing restriction to reverse. For a moderation ticket, change visibility separately from editing its comment or duration.

## Timeouts and other timed restrictions

A timeout ticket can extend beyond Discord’s 28-day native window. Set its full duration on the ticket. Sylvan renews the native timeout as needed and checks remaining time when the member rejoins. A direct edit or removal in Discord changes the managed timeout. Permanent timeouts are unsupported.

Timed bans, voice restrictions and tracked role grants can also expire. If a ticket shows **Pending**, check the ticket and the member’s current state before repeating an action. If enforcement is paused because the audit history is incomplete, verify the intended restriction, let audit recovery finish, then confirm the duration through the ticket editor. Escalate an unresolved mismatch to a senior moderator.

## Who can do what?

Staff need access to the relevant tickets channel. Sylvan checks the caller’s current Discord permissions when they act.

| Task | Permission |
| --- | --- |
| Create or edit a note | Moderate Members, View Audit Log or Administrator |
| Correct your own action ticket | The permission for that action, such as Ban Members for a ban or Manage Roles for a tracked role |
| Correct another moderator’s action, review tickets, view hidden details or restore a hidden record | View Audit Log or Administrator |
| Hide a visible note | Permission to edit the note |
| Hide an action ticket | View Audit Log or Administrator. Lifting an active restriction also requires the matching Discord permission. |
| Revert an active restriction | Permission to edit the ticket and the matching Discord permission, or Administrator. Sylvan must also be able to act on the member or role. |
| Configure tickets | Administrator |

See the [ticket command reference](../../commands/tickets/) for syntax, duration formats and administrator settings.
