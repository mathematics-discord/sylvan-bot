---
title: Hide or restore a ticket
description: Choose between lifting a restriction, hiding its record, and making a hidden ticket visible again.
---

Decide what should happen to **both** the member and the record before
choosing an action.

| What you want | Action | Result |
| --- | --- | --- |
| End the restriction and keep its explanation visible | **Revert Ticket** | Lifts the active restriction. The ticket stays visible. |
| Withdraw a record | **Hide Ticket** | Lifts an active restriction first, then hides the ticket and removes Sylvan’s recorded posts. |
| Show a hidden record again | `/ticket set` with `hidden:False` | Restores the record to ordinary lookups and allows a new post. The restriction stays lifted. |

## Hide a ticket

Choose **Hide** in the controls or **Apps → Hide Ticket** on the ticket
message. If you have the ticket number, use `/ticket hide`.

```text
/ticket hide id:1234
```

You can hide your own visible **notes** with Timeout Members. Editing or
hiding someone else’s note requires View Audit Log. The note
editor also has a **Hidden ticket** checkbox. Hiding an **action ticket**
requires **View Audit Log** or **Administrator**, including for your own
action or a kick. If the restriction is active, the
[requirements for lifting it](../change-a-restriction/#lift-a-restriction-now)
also apply. If the reversal is blocked or unconfirmed, the record stays
visible. Voice restrictions must first be lifted manually in Discord; wait
for Sylvan to observe the reversal. Check the member’s state before issuing
another action.

A hidden ticket remains stored. Senior staff can
[look it up by number](../find-a-ticket/#find-a-hidden-ticket).
Hiding removes ticket posts managed by Sylvan. Earlier lookup replies and
copies shared by staff stay where they were posted. If removal of a known
post fails, Sylvan retries it.

## Restore visibility

Use `hidden:False` with the ticket number. Restoring visibility requires
**View Audit Log** or **Administrator**.

```text
/ticket set id:1234 hidden:False
```

Check the result with [`/ticket show`](../../../commands/tickets/#ticket-show).
Use `hidden:True` to hide through the same command. For action tickets, make
visibility changes separately from changes to the comment or duration.
