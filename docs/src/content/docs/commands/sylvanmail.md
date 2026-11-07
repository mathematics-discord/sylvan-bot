---
title: /config sylvanmail
description: Choose the private mail forum and notification role.
---

Run this command inside the server. The configuration reply is private.

Read the [ModMail guide](../../features/sylvanmail/) for contacting staff and sending replies.

## Administrator configuration

An administrator can select the private forum and notification role:

```text
/config sylvanmail enable:<forum-channel> role:<ping-role>
```

Both options are required.

### Set forum access

Explicitly deny **View Channel** to `@everyone` on the mail forum, then grant
the staff roles or members who should read and answer mail the access they
need. Selecting a notification role does not grant forum permissions.

If the forum requires tags, Sylvan uses the first available tag.

The notification role can be any server role, including a managed role or
`@everyone`.

### Change the destination

Settings take effect immediately. Changing the forum leaves old case posts
in place and stops replies being sent from them. Subsequent DMs use the new
forum; old messages are not moved or replayed.

Only one server can configure ModMail for a bot database. There is no
disable command. Without a configured destination, DM senders receive an
unavailable notice.
