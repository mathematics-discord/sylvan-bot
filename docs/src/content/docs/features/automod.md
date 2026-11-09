---
title: Investigate an Automod action
description: Identify the rule behind a deletion, timeout or ban and check what happened to the member.
---

For an Automod timeout or ban, start with Sylvan’s entry in the server audit log. The reason includes the rule ID and an excerpt from the message. A deletion reason gives the rule ID. To look up the pattern and action, use [`/automod list`](../../commands/automod/#list-rules) in a staff channel.

## How matching works

Sylvan checks a member’s message text when they post or edit it, including in threads. Words and phrases match at word boundaries. Regular expressions match the pattern you supplied. Both ignore case.

Only one rule wins. Find the match that starts earliest in the message. If two matches start at the same character, the lower rule ID wins. For example, if a Delete pattern appears before a Ban pattern, Sylvan selects Delete.

| Winning action | Expected outcome |
| --- | --- |
| Delete | Sylvan attempts to delete the message. |
| Mute | Sylvan attempts to delete the message and time out its author. A longer existing timeout keeps its expiry. |
| Ban | Sylvan attempts to delete the message and ban its author for the rule’s duration, or permanently. |
| Note | Sylvan takes no action and creates no ticket. A Note rule can still win the match. |

For Mute and Ban, a failed deletion does not prevent the restriction attempt. Check the member’s timeout or the server ban list to confirm the result.

## Exemptions and settings

If a rule you expected to match was skipped, check the following.

- **Message.** Sylvan ignores bots, webhooks, empty text and attachment contents. Messages sent while Sylvan was offline are never replayed for moderation.
- **Role.** Any exempt role skips the member, including `@everyone`. Staff can match rules unless they have an exempt role. If Sylvan needs to check a member’s roles and cannot retrieve them, it skips that member.
- **Rule.** An administrator can [check which actions are enabled](../../commands/automod-admin/#show-settings). Disabled actions drop out of matching. A regular expression that times out drops out for that message only.

## Timed bans and failures

Removing a rule, disabling Ban or restarting Sylvan does not cancel a recorded ban’s expiry. Once Sylvan matches the ban to its audit entry, use [the ticket to change the duration or lift the restriction](../../staff/tickets/change-a-restriction/).

At expiry Sylvan checks that the ban still belongs to that action. A later ban or unban can supersede it. If the audit history is missing or ambiguous, expiry can be delayed or paused for review. Check the current ban and [the ticket’s status](../../help/tickets/) before acting.
