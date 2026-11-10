---
title: Set up moderation tickets
description: Choose where Sylvan publishes tickets, enable capture, and configure reminders and tracked roles.
---

You need **Administrator** to configure tickets. Start by running
`/config tickets` with no options. Only you see the reply. It lists the
destination, enabled state, reminder interval, and
tracked roles.

## Enable ticket capture and Automod

1. Choose a server text channel for ticket posts and the review summary.
   Give Sylvan **View Audit Log** so it can capture moderation actions.
   For restrictions you expect it to renew or lift, give it the corresponding
   action permissions and check its role position.
2. Set the destination and enable capture. Replace `#tickets` with your
   chosen channel.

   ```text
   /config tickets channel:#tickets enabled:True
   ```

3. Check the destination and enabled state in the reply. After the next
   moderation action, check that its numbered ticket appears. A manually
   created note can verify posting, while a captured action also verifies
   that Sylvan is reading the audit log.

## Set the reminder interval

By default, Sylvan reminds moderators about outstanding tickets every
24 hours. This example sets a 12-hour interval.

```text
/config tickets reminder-minutes:720
```

The setting is in whole minutes. A moderator can request their newest prompt
immediately with [`/ticket top`](../../commands/tickets/#ticket-top).

## Approval

Actions by staff with View Audit Log, including administrators, do not require
another moderator's approval. Other action tickets require approval by staff
with View Audit Log. There is no configurable review-exemption role list.

## Track grants of a role

Choose an ordinary role whose grants should create moderation tickets.
Replace `@Restricted` with that role.

```text
/config tickets tracked-role-add:@Restricted
```

Use `tracked-role-remove` to stop capturing new grants. Managed roles and
`@everyone` cannot be added. Tracking records the grant and allows staff
to set its duration. [Role persistence](../../features/persistence/) is a
separate setting for restoring selected roles when a member rejoins.

## Pause or resume tickets

Run `/config tickets enabled:False` to disable new ticket capture and Automod.
This is the same switch as `/config automod set enabled:false` without an
`action`. Saved Automod rules, action switches, and exemptions are retained.

Normal ticket lookups, edits, publication, and DMs pause. Existing restrictions
continue to be checked, expired, and renewed, including long timeouts. Voice
restrictions always need manual reversal in Discord. Administrator publication
repair remains available while disabled.

To resume with the saved destination, run `/config tickets enabled:True`.
New capture resumes from that point. Actions during the pause can still end
or update an existing restriction, but do not create ordinary new tickets.

See the [configuration command](../../commands/config-tickets/) for all
accepted options and limits.
