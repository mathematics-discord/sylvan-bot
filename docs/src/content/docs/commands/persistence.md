---
title: /persistence
description: Restore selected roles when members rejoin.
---

Run these commands inside the server. Replies are private.

```text
/persistence add role:@Example
/persistence remove role:@Example
/persistence list
```

`role` is required for add and remove. Add remembers the role on future departures; list shows configured roles and any currently blocked from restoration. Remove stops remembering the role and clears pending restorations, leaving current members? roles unchanged.

Only ordinary roles without staff or moderation permissions can be persisted. Channel overrides count too. The role must be below your highest role. Safety is checked again before restoration, so granting a configured role moderation powers blocks restoration.

Sylvan must observe the departure with the member's roles cached. Departures missed while it is offline cannot be recovered. Failed restorations remain pending for a later rejoin; deleted or unsafe roles are discarded. Replies are private.

See the [Role persistence](../../features/persistence/) guide for automatic behaviour and limitations.
