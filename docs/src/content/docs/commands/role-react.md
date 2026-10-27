---
title: /config rolereact
description: Let members choose ordinary server roles by reacting to a message.
---

Configure a message reaction that gives members a role when they react. Removing an individual reaction removes the role
again.

## Add a reaction role

1. Create or choose an ordinary member role, such as Art.
2. Copy the link to the message members should react to. In Discord, open
   the message's menu and choose **Copy Message Link**.
3. Run `/config rolereact`, select **add** from the `action` dropdown,
   and supply the message link, emoji and role.

```text
/config rolereact action:add message_link:<copied-message-link> emoji:🎨 role_mention:@Art
```

| Option         | What to enter                                                                     |
|----------------|-----------------------------------------------------------------------------------|
| `action`       | Choose `add` or `remove` from the dropdown.                                       |
| `message_link` | A full Discord message link from this server. A message ID alone is not accepted. |
| `emoji`        | One Unicode emoji, or a custom emoji Sylvan can use.                              |
| `role_mention` | Choose the role to grant. Required for `add`; leave it out for `remove`.          |

The linked message can be in another channel or a thread in this server.

Sylvan adds the emoji to the message. Members can then select that reaction to receive the role and select it again to
remove their reaction and role. Existing reactions are not processed when you create the mapping.

You can configure several different emoji on one message and reuse an emoji on other messages. Each message and emoji
pair grants one role. To change that role, remove the mapping and add it again with the new role. Repeating `add` with
the same message, emoji and role keeps the mapping and asks Sylvan to add its reaction again.

## Choose a safe role

The role must already exist in this server. It cannot be `@everyone` or an automatically managed role, such as an
integration role.

Roles with staff powers are rejected. This includes Administrator, View Audit Log, management permissions, member and
voice moderation, pinning, slowmode bypass, server-wide mentions and server analytics access. Permissions allowed
through channel or category overrides are checked too.

Unless you own the server, the target role must be below your highest role.

Sylvan checks the role's permissions again before each new grant. If the
role later gains staff powers, new grants stop. Existing members are not
automatically stripped of that role.

**Unreacting removes the role even if a moderator or another reaction
mapping also granted it.** Choose roles that members can safely give up
this way.

## Remove a reaction role

Select **remove** and supply the same message link and emoji. No role
option is needed.

```text
/config rolereact action:remove message_link:<copied-message-link> emoji:🎨
```

Sylvan removes the mapping and clears that emoji's reactions from the
message. Other emoji on the message are left alone. **Members keep roles
they already received**, and the Discord role itself is not deleted.

You can remove the mapping even if its message has been deleted. If Sylvan
cannot clear the visible reactions, its reply explains that the mapping is
disabled and asks you to clear those reactions manually.

## Incomplete updates

If the mapping is saved but Sylvan cannot add the emoji, the reply says so.
Read the error in Sylvan's reply, correct the problem, then repeat `add`
with the same options.

Role changes are processed while Sylvan is online. If someone reacted
before setup or while Sylvan was offline, ask them to remove and add their
reaction again. If a role still does not update, ask the person running
Sylvan to check its logs.
