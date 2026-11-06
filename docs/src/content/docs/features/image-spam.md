---
title: Image-spam detection
description: Automatic bans for attachment bursts across several channels.
---

Image-spam detection watches for one member posting messages with enough attachments across several distinct server text channels within a short window. Repeated messages in the same channel do not count as additional channels. When the threshold is reached, Sylvan attempts to ban the member.

Despite the name, **this counts attachments; it does not check whether they are images**, inspect their contents or require the attachments to be identical. Threads are not included. Bots and webhooks are ignored.

## Thresholds

The defaults are four distinct channels, at least two attachments on each qualifying message, within 45 seconds. Administrators can view or change them with [/config image-spam](../../commands/spam/). Detection needs an enabled configuration; viewing settings alone does not create one. Supplying a setting creates a configuration if needed.

## Exemptions and outcomes

Members with Administrator, Manage Server, Manage Messages, Ban Members, Kick Members or Timeout Members are exempt. The operator can also configure user and role allowlists. These are separate from Automod?s role exemptions.

This feature issues a ban, rather than a warning or timeout. Recent message history may be removed according to the bot?s configured ban settings. If Discord rejects the ban, detection alone does not prove that moderation succeeded. Contact staff about an unresolved incident.
