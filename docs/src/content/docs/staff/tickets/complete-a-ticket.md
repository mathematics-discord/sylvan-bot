---
title: Complete or correct a ticket
description: Fill in a captured moderation action, recover a missed DM, or update an existing comment.
---

After taking a moderation action in Discord, look for the numbered ticket
in the tickets channel. Sylvan can use the action’s audit reason as its
comment. Check what was recorded before adding anything else about the incident.

## Complete the record

1. Choose **Edit** in Sylvan’s DM, or open the ticket message’s **Apps →
   Manage Ticket** menu and choose **Edit**. You can also open the editor by
   number with `/ticket edit id:1234`. Another moderator’s DM controls belong
   to them, so use the ticket message or the command for their record.
2. In **Comment**, describe what happened and why you acted. Include a
   message link if it will help the next moderator find the evidence.
3. For a restriction, check **Duration**. Leave it blank to keep the current
   value. If you change it, enter the total time from the original action.
4. Submit the editor. Check the saved comment and the displayed expiry.
   If Sylvan also reports a pending or rejected Discord change,
   [check the outcome](../../../help/tickets/#a-discord-change-is-pending-or-unconfirmed).

Only you see the editor. Saved changes update the ticket that staff use.
The original moderator stays on the record. If Sylvan says someone changed
the ticket while your editor was open, reopen it and read their update first.

## Add a later development

Use `/ticket append` to keep the original comment and add a new line.
Replace `1234` with the ticket number.

```text
/ticket append id:1234 comment:Member acknowledged the warning in ModMail.
```

Use [`/ticket set`](../../../commands/tickets/#ticket-set) to replace an
incorrect comment. A comment must contain text and fit within 3,000
characters. An append must fit together with the existing comment and the
separating newline. If an existing comment exceeds the editor limit, use
`/ticket set` to supply its replacement.

Comment and duration edits keep any existing approval. **Modification
history** shows recent staff changes and indicates when earlier entries have
been omitted.

## If the DM is missing

Run [`/ticket top`](../../../commands/tickets/#ticket-top) in the server to
request your newest outstanding prompt. Open **Edit** in the DM to complete
it. Typing an ordinary DM reply leaves the ticket unchanged.

A ban or timeout can prompt you even when its audit reason supplied all the
details. If that first DM fails, a completed ticket may have no reminder to
resend. [Find it by member or number](../find-a-ticket/) and open its editor
in the server.
