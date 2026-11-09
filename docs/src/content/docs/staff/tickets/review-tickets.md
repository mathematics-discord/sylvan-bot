---
title: Review the ticket queue
description: Approve, correct, or skip moderation tickets in your private review queue.
---

Choose **Review tickets** on the summary at the bottom of the tickets channel,
or run `/ticket review`. The summary is a shared post. The queue that opens
is visible only to you. Reviewing requires **View Audit Log** or **Administrator**.

## Work through the queue

Read the action, subject, moderator, reason, and expiry on the first ticket.
The queue counts tickets **ready for review** separately from those awaiting
details or a pending change in Discord.

| Control | What happens |
| --- | --- |
| **Approve & next** | Records your approval and opens the next eligible ticket. |
| **Edit** | Opens the details for correction. Saving a restriction edit takes you back to the queue to decide. After a kick edit, reopen the queue with `/ticket review`. |
| **Skip** | Moves past this ticket for this pass. It still awaits approval. |
| **Refresh** | Checks for changes while you have the queue open. |
| **Start again** | Returns to the beginning after you reach the end of a pass, including tickets you skipped. |

## Approve one ticket

On a ticket message, choose **Apps → Approve Ticket**, or choose **Approve**
in the ticket controls. The recorded approval identifies the reviewer and
survives later edits to the comment or duration. The member’s restriction
continues according to its own expiry.

## Why a ticket is missing

Notes, hidden records, approved tickets, and review-exempt actions stay out
of the queue. An action can be exempt because of a role the **acting moderator**
held when Sylvan captured it. [Changing the exemption list](../../../setup/tickets/#choose-which-actions-need-review)
applies to future captures.

A ticket still needing a comment, a timeout without a finite duration, or an
action with a pending Discord change counts as waiting. Complete the details
or [resolve the pending change](../../../help/tickets/#a-discord-change-is-pending-or-unconfirmed),
then refresh.
