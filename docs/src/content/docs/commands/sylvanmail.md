---
title: SylvanMail (beta)
description: Private correspondence with staff through Sylvan.
---

**SylvanMail is a beta. Live Discord behaviour for this patch has not been verified.**

DM Sylvan normally to contact staff, including after leaving the server. Incoming DMs are relayed automatically to a
private forum case titled with your username. The opening embed contains the case number, your raw user ID and username,
and your user mention when you are a guild member. The configured role is pinged when the case opens.

Inbound text and attachment captions use the original message time in UTC:

- Guild member: `[HH:MM:SS] <@userid>: text` (displayed as a user mention without notifying them).
- Non-member: `[HH:MM:SS] username: text`.

After an inbound gap of at least three hours, the next delivered DM in an existing case is accompanied by a separate role
ping. The gap comes from the user's DM message timestamps, ignoring bot replies and acknowledgements. No timer or saved
last-ping state is used. A new case already has its opening ping and does not get a second one.

An initial DM explains the beta limitations. After Discord accepts every relayed part, the first successful acknowledgement
in the conversation says **Delivered to staff. Please be patient, someone will get to you soon.** Later successes say
**Delivered.** Failed, partial and uncertain deliveries receive a DM explaining the outcome. Forum threads contain delivery
errors when needed, without pending notices or routine success receipts. Acknowledgements themselves can fail if Discord
is unavailable; ambiguous sends are not automatically retried.

Every DM error includes resolution instructions. Correct rejected text or attachments before sending again, or retry later
if the service is busy. For partial or uncertain delivery, or a problem you cannot resolve, DM user `203864799870844928`
directly or contact another staff member. Ask staff to check what arrived before resending uncertain content.

## Drafting and sending staff replies

Anyone with write access to the mapped forum thread can send. The configured ping role does not authorise or restrict
sending. Bots and webhooks are ignored.

1. Mention Sylvan explicitly in the message body to draft an anonymous reply, labelled `From staff:`.
2. Alternatively, reply to a message from Sylvan with the reply ping enabled to draft a signed reply, labelled
   `From <username>:`. A reply with the ping disabled does not qualify on its own.
3. If both triggers are present, the explicit body mention wins and the reply is anonymous.
4. Sylvan marks a sendable draft with 📤. Posting or editing the draft does not send it. Only its author can click that
   reaction to send; other people's clicks are ignored.
5. At confirmation, Sylvan fetches the latest text and attachments, checks current thread write access, and re-evaluates
   anonymous versus signed mode. Removing the trigger or making the draft invalid prevents delivery.
6. Only after Discord accepts every text chunk and attachment does Sylvan add ☑️ to the author's original message.
   Failed, partial or uncertain deliveries receive an error reply in the thread instead. Repeated clicks do not resend
   a recorded attempt, including across restarts.

Sylvan removes its body mention before delivery. Other mentions are preserved as text without notification. Discussion
without a send trigger remains in the thread. Edits and deletions after delivery are not synchronised.

## Administrator configuration

```text
/config sylvanmail enable:<forum-channel> role:<ping-role>
```

The `/config` group is Administrator-only in command visibility and at runtime. Both options are required. The role is
only a ping target: it may be any guild role, including managed roles or @everyone, and does not need forum read access.
Configuring it never grants forum permissions. The bot must be able to mention the selected role; @everyone requires
Mention Everyone. Only the selected target is allowed to notify through ping messages.

Explicitly deny **View Channel** to `@everyone` on the private forum. Administrators control which other roles and members
can read and write there independently of the ping target. Sylvan needs **View Channel**, **Send Messages (Create Posts)**,
**Send Messages in Threads**, **Read Message History**, **Attach Files**, **Embed Links**, **Manage Threads** and
**Add Reactions**. A forum requiring tags uses its first available tag.

Only one guild may configure SylvanMail per bot database. Settings take effect immediately. Changing the forum keeps
historical mappings and posts, stops sending from old posts, and uses the new forum for subsequent DMs. Nothing is moved
or replayed. Without a configured destination, DM senders receive an unavailable notice. There is no disable command.

## Content and delivery

Text is split to respect Discord's 2,000-character limit. Ordinary attachments are reuploaded in order, including
attachment-only drafts with a send trigger. Limits are **16,000 text characters, 10 attachments, 8 MiB per file and 24 MiB
total**. Stickers, polls, voice messages, forwarded-message attribution and interactive content are unsupported. A draft
must pass validation before it receives the send reaction and again when confirmed.

Archived posts are reopened. If the mapped post is deleted, inaccessible, or no longer belongs to the configured forum,
the next inbound DM retires that mapping and creates a new case post, then relays the message there. Historical linkage
is retained. Locked posts report an error. Transient network failures do not prove a post is inaccessible. An interrupted
creation with no saved post ID is still reconciled using its case number before another post is created.

Success means Discord accepted every part. Source/author IDs, mappings and accepted destination IDs are retained; message
bodies and attachment bytes or URLs are not stored in the database or logged by SylvanMail. Ambiguous requests are not
blindly retried. An absent success reaction does not prove that nothing arrived; reconcile uncertain attempts before
resending. A failed reaction never changes the recorded payload outcome or resends the payload.

Gateway work is bounded and transient. Events not processed before shutdown are not replayed. A full buffer refuses DM
relays and confirmation attempts with a busy error when Discord is reachable. If a draft has no send reaction, it has not
been prepared for confirmation; editing it triggers another check. No background recovery or automatic resend is provided.

SylvanMail uses Direct Messages, guild message and reaction events. The bot currently enables Message Content; confirmation
fetches the mapped draft over REST so edits and attachments are read at sending time. It does not relay ordinary discussion.
