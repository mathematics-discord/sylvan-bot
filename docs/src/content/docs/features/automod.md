---
title: Automod
description: Match message text against rules and apply automatic moderation.
---

Automod checks members? new messages and content edits against the server?s rules. It ignores bots, webhooks and empty text. It checks message text, including in threads; it does not inspect the contents of attachments.

## How matching works

Plain patterns match whole words or phrases, ignoring case. Backtick-delimited patterns are regexes, also ignoring case. If several enabled rules match, the match appearing earliest in the message wins; ties go to the lowest rule ID. Only that rule is selected. The most severe action does not take priority.

| Action | What happens |
|---|---|
| Delete | Attempts to delete the message. |
| Mute | Attempts to delete the message and time out its author. An existing longer timeout is kept. |
| Ban | Attempts to delete the message and ban its author, permanently or for the configured duration. |
| Note | The rule is saved, but no action is executed yet. |

A selected Note rule can therefore prevent another matching rule from taking action. Disabled actions are excluded from matching. A regex that takes too long is skipped for that message; other rules can still match.

## Exemptions and settings

Members holding any configured exempt role are skipped. Staff are not automatically exempt: configure their roles if they should be excluded. If required member-role information cannot be obtained, Automod skips enforcement rather than guessing whether an exemption applies.

Use [/automod](../../commands/automod/) to add, remove and list rules. Use [/config automod](../../commands/automod-admin/) to switch actions on or off and manage exemptions. Omitting `action` changes the shared ticket-capture and Automod switch; existing restrictions continue to expire and renew when it is disabled. Saved changes normally take effect immediately. If a reply reports an unsuccessful refresh, the previous active configuration remains in use.

## Timed bans and failures

Timed bans are recorded across restarts. Sylvan removes a ban when it expires only when it can confirm that it still owns that ban. A later ban or unban can supersede it; missing or unreadable audit history can delay removal or require manual review. A ban reaching its nominal expiry is therefore not a guarantee that the member has already been unbanned.

Deleting a rule or disabling Ban does not cancel a recorded ban or its expiry handling. Messages received while Sylvan is offline are not replayed for moderation. Discord actions can fail independently: a failed deletion does not prevent an attempted timeout or ban. Ask staff to review an unresolved outcome.
