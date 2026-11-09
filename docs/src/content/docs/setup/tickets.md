---
title: Set up moderation tickets
description: Choose where Sylvan publishes tickets, enable capture, and configure reminders, review exemptions, and tracked roles.
---

You need **Administrator** to configure tickets. Start by running
`/config tickets` with no options. Only you see the reply. It lists the
destination, enabled state, reminder interval, review-exempt roles, and
tracked roles.

## Enable ticket capture

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

## Choose which actions need review

To exempt actions taken by members of a particular role, supply that role
in `exempt-role-add`. Use `exempt-role-remove` to end the exemption.
Sylvan checks the **acting moderator’s roles** when it captures the action.
New settings apply to future captures. Existing tickets keep their review
status, and the exemption grants no review permissions.

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

Run `/config tickets enabled:False` to pause ticket capture and management,
including lookups, edits, automatic expiry, and timeout renewals. Pausing
does not lift existing restrictions. To resume with the saved destination, run
`/config tickets enabled:True`. Capture resumes from that point. Review any
outstanding restrictions and actions that happened during the pause.

See the [configuration command](../../commands/config-tickets/) for all
accepted options and limits.
