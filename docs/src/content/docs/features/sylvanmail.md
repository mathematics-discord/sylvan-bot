---
title: ModMail
description: Contact staff privately, or reply to members from the staff mail forum.
---

## For users: contact staff

Send a direct message to the **ModMail** bot. You can open its profile from
the server's member list or staff-contact link, then choose **Message**.
Write what you need help with; you can include screenshots or other attachments.
There are no commands to learn.

Your message goes to a private staff conversation, where moderators can
read it and reply. Their replies arrive in your DMs with ModMail, so you
can carry on the conversation there. Send your messages to ModMail rather
than the main Sylvan bot.

You'll receive an acknowledgement when your message is delivered. This
means it reached the staff conversation, not that someone has read it yet.
If Discord won't let you message ModMail, contact a staff member directly.

## For moderators: read and reply

Incoming mail appears in the private staff mail forum, with a separate
post for each person. The opening message identifies them by username and
user ID. Messages and attachments appear in the post, and the configured
staff role is notified of new mail. Another message after a gap of at
least three hours triggers a fresh notification.

Anyone with access to write in the post can reply. The notification role
doesn't decide who can answer.

### Send an anonymous reply

1. Write your reply in the post and **mention Sylvan in the message**.
2. Wait for Sylvan to add 📤, then click it on your own message to send.

The member sees **From staff:** followed by your reply. Sylvan removes
its own mention from the delivered text.

### Send a reply with your username

1. Use Discord's **Reply** action on a message from Sylvan in the post.
2. Keep the reply ping enabled and write your reply.
3. Wait for 📤, then click it on your own message to send.

The member sees **From &lt;username&gt;:** followed by your reply. If you also
mention Sylvan in the message body, the reply is anonymous instead.

### Drafts and staff discussion

Posting a draft doesn't send it. Only its author can click 📤 to send or
❌ to cancel. You can edit the text and attachments before sending; ✅
means Discord accepted the whole reply. Editing or deleting it afterwards
doesn't change the member's copy.

Ordinary discussion without a Sylvan mention or a ping-enabled reply stays
in the staff post. Other mentions in a delivered reply don't notify anyone.

### Technical details and delivery problems

Messages support up to 16,000 characters and 10 attachments, with limits
of 8 MiB per attachment and 24 MiB in total. Stickers, polls, voice messages
and interactive content aren't supported. Long text is split into messages.

If a reply reports a delivery problem, follow its explanation. For partial
or uncertain delivery, check what arrived before sending again. Messages
interrupted by a shutdown aren't automatically replayed. Administrators
can choose the forum and notification role with
[/config sylvanmail](../../commands/sylvanmail/#administrator-configuration).
