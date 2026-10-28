---
title: /config colour
description: Configure which roles grant access to display colours.
---

Manage the display colours available to members through `/colour set`.

Each mapping connects a source role, which grants access to a colour,
to a display role, which supplies that colour. For example, mapping
Members to Purple lets members with the Members role choose Purple.

Use these commands inside a server. Replies are visible only to you.

## upsert

Create a mapping, or change an existing mapping by supplying its ID.
Both roles must already exist in the server.

```text
/config colour upsert source:@Members display:@Purple
/config colour upsert source:@Members display:@Purple id:12
```

- `source` is required. Choose the role that grants access. It must
  have no colour set. Use @everyone to make the colour available to everyone.
- `display` is required. Choose the role that supplies the colour.
  It must have a colour set and must be different from the source role.
- `id` is optional. Leave it out to create a new mapping. To change
  a mapping, enter its ID from `/colour list`. This is the
  mapping's number, not a Discord role ID.

An ID must be a positive number belonging to an existing mapping in
this server. Supplying an unknown ID does not create a new mapping.
You cannot register the same source and display pair twice, but one
source can grant several colours and several sources can grant the same colour.

Sylvan replies with the saved mapping's ID and roles, then reports
any incomplete role updates from its automatic check of affected members.
Members choose their colour through `/colour set`; creating or changing
a mapping does not automatically give them a new display role.

## delete

Remove a mapping using its ID from `/colour list`.

```text
/config colour delete id:12
```

`id` is required and must be a positive number belonging to a mapping
in this server. Sylvan reports an error if the mapping does not exist.

This removes the connection between the source and display roles.
It does not delete either Discord role. Sylvan checks affected members
and removes display colours they are no longer allowed to use. A member
can keep a valid colour if another mapping still grants them access to it.

## sync

Check display colour roles for members who currently hold a configured
source role. There are no options.

```text
/config colour sync
```

Sylvan keeps a member's display colour if they have exactly one configured
display role and their source roles still grant access to it. Otherwise,
it removes their configured display colour roles so they can choose again
through `/colour set`. It does not choose or assign a replacement colour.

Members who do not hold any configured source role are not included in
this manual check. If no mappings exist, Sylvan tells you there is nothing
to check.

## Incomplete updates

After `upsert`, `delete`, or `sync`, the reply may say that some updates
are incomplete. This means Sylvan could not finish checking or updating
roles. A saved or deleted mapping is not undone by an incomplete role check.
Ask the person running the bot to check its logs for the cause.

## Public colour list

Anyone in the server can use [/colour list](../color/#list) to see all
configured colour mappings and their IDs. Use a mapping ID with `upsert`
to change it or `delete` to remove it.
