---
title: SylvanMail
description: Read private messages from members and reply on behalf of staff.
sidebar:
  hidden: true
---

SylvanMail lets people contact staff by sending Sylvan a direct message,
even after leaving the server. Each conversation appears as a post in the
private staff mail forum. Read the conversation and reply from that post.

## Read incoming mail

Sylvan opens a case post named after the sender and notifies the configured
staff role. The opening message includes the case number, username and user
ID so you can identify the person contacting you.

Incoming messages and attachments appear in the post with their original
time in UTC. Messages from current server members show their user mention;
messages from people outside the server show their username.

If the person sends another message after a gap of at least three hours,
Sylvan notifies the staff role again. Staff replies do not reset that gap.

The sender receives a delivery acknowledgement once Discord accepts all
parts of their message. This confirms delivery, not that staff have read it.

## Reply to a conversation

You can reply if you have permission to write in the case post. The role
Sylvan pings does not control who can send replies.

### Reply anonymously

1. Write your reply in the case post and **mention Sylvan in the message**.
2. Wait for Sylvan to add 📤 to your message.
3. Click 📤 on your own message to send it.

The recipient sees **From staff:** followed by your reply. Sylvan removes
its own mention from the delivered text.

### Reply with your username

1. Use Discord's **Reply** action on a message from Sylvan in the case post.
2. Keep the reply ping enabled and write your reply.
3. Wait for 📤, then click it on your own message to send.

The recipient sees **From &lt;username&gt;:** followed by your reply.

If you also mention Sylvan in the message body, the reply is anonymous.
Replying with the ping disabled does not prepare a reply unless you mention
Sylvan in the message body.

### Check whether it was sent

| What you see | What it means |
| --- | --- |
| 📤 | Your draft is ready for you to confirm. It has not been sent. |
| ☑️ | Discord accepted every part of the reply. |
| An error reply | Delivery failed, was partial, or could not be confirmed. Read the explanation before trying again. |

Posting or editing a draft does not send it. Only the author can confirm
it, and Sylvan sends the latest text and attachments when they do. Removing
the mention or reply ping can make the draft ineligible to send.

Ordinary discussion without either reply trigger stays in the staff post.
Other mentions in a delivered reply do not notify anyone. Editing or
deleting a message after sending it does not change the recipient's copy.

## Handle a delivery problem

Follow the error message in the case post. If text or attachments were
rejected, correct them before preparing another reply. If Sylvan is busy,
try again later. Editing a draft without 📤 asks Sylvan to check it again.

**If delivery was partial or uncertain, check what arrived before sending
another message.** A missing ☑️ does not prove that nothing was delivered.
Clicking 📤 again does not resend an attempt Sylvan has already recorded.

People sending DMs receive instructions when delivery fails. For an
unresolved problem, they are directed to contact user `203864799870844928`
or another staff member directly.

Archived case posts are reopened when needed. A deleted or inaccessible
post is replaced when the person next sends a DM; a locked post produces
an error. Messages interrupted by a shutdown are not automatically replayed,
and uncertain deliveries are not automatically retried.

## Supported messages

You can send text and ordinary attachments, including an attachment with
no accompanying text. Staff drafts still need a reply trigger and confirmation.

| Limit | Maximum |
| --- | --- |
| Text per message | 16,000 characters |
| Attachments | 10 |
| Each attachment | 8 MiB |
| All attachments together | 24 MiB |

Long text is split into Discord messages. Stickers, polls, voice messages,
forwarded-message attribution and interactive content are not supported.

## Administrator configuration

An administrator can select the private forum and notification role:

```text
/config sylvanmail enable:<forum-channel> role:<ping-role>
```

Both options are required. The entire `/config` group requires Administrator.

### Set forum access

Explicitly deny **View Channel** to `@everyone` on the mail forum, then grant
the staff roles or members who should read and answer mail the access they
need. Selecting a notification role does not grant forum permissions.

Sylvan needs **View Channel**, **Send Messages (Create Posts)**,
**Send Messages in Threads**, **Read Message History**, **Attach Files**,
**Embed Links**, **Manage Threads** and **Add Reactions**. If the forum
requires tags, Sylvan uses the first available tag.

The notification role can be any server role, including a managed role or
`@everyone`. Sylvan must be able to mention it; notifying `@everyone`
requires **Mention Everyone**.

### Change the destination

Settings take effect immediately. Changing the forum leaves old case posts
in place and stops replies being sent from them. Subsequent DMs use the new
forum; old messages are not moved or replayed.

Only one server can configure SylvanMail for a bot database. There is no
disable command. Without a configured destination, DM senders receive an
unavailable notice.
