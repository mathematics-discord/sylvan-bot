---
title: SylvanMail (beta)
description: Beta private correspondence with Senior Moderators through Sylvan.
---

**SylvanMail is an untested beta. Bugs, incomplete delivery and lost messages are possible. Live Discord behaviour has not
been verified.**

DM Sylvan normally to contact Senior Moderators, including for appeals after leaving the server. Sylvan opens or reuses a
private forum post titled with your username. The opening embed contains the case number, separate code blocks with your
raw Discord user ID and username for copying on desktop or mobile, and your user mention if you are still a guild member.
The configured Senior Moderator role is pinged in the same opening message; the user mention does not notify you.

The second post message relays your DM as `[HH:MM:SS] username: text`, using the original message time in UTC. Later inbound
messages and attachment captions use the same format. Delivery receipts follow the relayed message.

An initial DM explains where messages go and the beta limitations. After Discord accepts every relayed part, Sylvan replies
to your first delivered DM in that conversation with **Delivered to staff. Please be patient, someone will get to you
soon.** Subsequent successful DMs receive **Delivered.** Acknowledgement attempts and accepted message IDs are saved;
ambiguous acknowledgements are not automatically resent.

Staff can discuss a case normally. **Only a new message containing an explicit textual mention of Sylvan is sent to the
user.** A reply's automatic author ping does not count. Sylvan removes its own mention and sends the remaining text and
ordinary attachments under **Senior Moderators**, without the staff member's identity, message links or forwarded-message
attribution. Bots and webhooks are ignored. Other mentions do not notify anyone.

## Administrator configuration

```text
/config sylvanmail enable:<forum-channel> role:<Senior-Moderator-role>
```

The entire `/config` group is Administrator-only, both in Discord command visibility and at runtime. Both options are
required; `enable` selects a forum, not a Boolean. The selected role authorises staff replies and is pinged once when a post
opens. Only members holding that role can send replies through SylvanMail.

Before enabling, explicitly deny **View Channel** to `@everyone` and allow it to the selected role and Sylvan. Other roles
or member overrides must not expose the forum outside that role, Sylvan and administrators. Sylvan rejects unsafe access
without changing any permissions. Member data must be ready. The bot needs **View Channel**, **Send Messages (Create
Posts)**, **Send Messages in Threads**, **Read Message History**, **Attach Files**, **Embed Links** and **Manage Threads**.
The staff role must be mentionable by Sylvan. A forum requiring tags uses its first available tag.

Settings persist and take effect immediately. Reapplying the same selection is idempotent. Only one guild may enable
SylvanMail per bot database; conflicting saved destinations fail closed. Without a destination, DMs are not relayed.
Changing forums retains historical posts and mappings, stops sends from the old forum, and creates or reuses conversations
in the new forum on subsequent DMs. Nothing is moved or replayed. There is no disable command in this version.

## Content and delivery

Text is split in order to respect Discord's 2,000-character message limit. Attachments retain their order and are
reuploaded, including attachment-only staff messages containing Sylvan's mention. Limits are **16,000 text characters,
10 attachments, 8 MiB per file and 24 MiB total**. Stickers, polls, voice messages, attributed forwards and interactive
content are refused. Failed downloads or unsupported content are reported rather than silently omitted.

Auto-archived posts are reopened. Locked or inaccessible posts fail without creating a replacement. Only a confirmed
deleted post can be replaced; historical linkage remains. Interrupted post creation is reconciled using the saved case
number in Sylvan's opening message across active and archived posts. Titles may be renamed. An unresolved attempt remains
blocked rather than creating another post; retain the case number in the opening message while reconciliation is needed.

Thread receipts distinguish pending/unconfirmed, delivered, refused, saved-but-unpublished, partial and uncertain
delivery. Success means Discord accepted every part. Source/author IDs, mappings and accepted destination IDs are retained
for accountability; message bodies and attachment bytes/URLs are not stored in the database or logged by SylvanMail.
Confirmed source messages are not resent across restarts. Ambiguous requests are never blindly retried, and delivery is
not exactly once. A message without a delivery receipt, or a pending receipt left by interruption, is unconfirmed and
requires staff reconciliation. Receipt or acknowledgement delivery can itself fail when Discord is unavailable.

Only new messages are relayed: **edits and deletions are not synchronised**. Gateway work is bounded and transient; events
not processed before shutdown are not replayed. No background recovery, automatic resend or legacy-history migration is
provided. A full work buffer refuses new messages with a busy notice; that notice can itself fail during an outage.

SylvanMail adds only the ordinary Direct Messages intent. It uses Discord's documented
[DM and bot-mention exceptions to Message Content](https://docs.discord.com/developers/events/gateway#message-content-intent),
so it needs no privileged Message Content intent, even though other Sylvan features currently enable it. It never fetches
ordinary guild discussion over REST to obtain content.
