---
title: /persistence
description: Restore selected roles when members rejoin.
---

Use `/persistence add role` to remember a role when a member leaves and restore it when they return. `/persistence list` shows configured roles and any that are currently blocked.

Use `/persistence remove role` to stop remembering a role and clear its pending restorations. This leaves current members' roles unchanged.

Only ordinary roles without staff or moderation permissions can be persisted. Channel overrides count too. The role must be below your highest role and the bot's highest role, and the bot needs Manage Roles. Safety is checked again before restoration, so granting a configured role moderation powers blocks restoration.

Sylvan must observe the departure with the member's roles cached. Departures missed while it is offline cannot be recovered. Failed restorations remain pending for a later rejoin; deleted or unsafe roles are discarded. Replies are private.
