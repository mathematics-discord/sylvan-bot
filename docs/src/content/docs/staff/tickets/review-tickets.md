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
survives comment edits. A duration change by a moderator without **View Audit
Log** returns the ticket to review. A reviewer’s duration edit preserves any
existing approval, but does not approve a ticket that is still awaiting review.
The member’s restriction continues according to its own expiry.

## Why a ticket is missing

Notes, hidden records, and accepted tickets stay out of the queue. Actions
created by staff with **View Audit Log**, including administrators, are
accepted automatically. Sylvan's Automod action tickets still require staff
approval; image-detector bans keep automatic acceptance. Notes never require
approval. There is no separate exemption category or role list.

A ticket still needing a comment, a timeout without a finite duration, or an
action with a pending Discord change counts as waiting. Complete the details
or [resolve the pending change](../../../help/tickets/#a-discord-change-is-pending-or-unconfirmed),
then refresh.
