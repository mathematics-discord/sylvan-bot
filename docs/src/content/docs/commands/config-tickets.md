---
title: /config tickets
description: Look up the options for ticket capture, destination, reminders, review exemptions, and tracked roles.
---

**Administrator.** Run the command in the server. The configuration reply
is visible only to you. With no options, it shows the current settings.

```text
/config tickets
```

Supply only the fields you want to change. The reply shows the resulting
configuration.

| Option | What to enter |
| --- | --- |
| `channel` | A server text channel for new ticket posts and the review summary. A destination is required to enable tickets. |
| `enabled` | `True` to enable ticket capture and management, or `False` to pause them. |
| `reminder-minutes` | A whole number from 1 to 35,791,394. The default of 1,440 minutes is 24 hours. |
| `exempt-role-add` | A role whose holders’ newly captured actions are exempt from review. |
| `exempt-role-remove` | A role to remove from the review-exemption list. |
| `tracked-role-add` | An ordinary role in this server. Future grants of this role create tickets. |
| `tracked-role-remove` | A role whose new grants should stop creating tickets. |

For a given list, supply different roles if adding and removing in the same
command. Exemption changes apply to new tickets. Tracking changes affect
future grants.

This example sets the reminder interval to six hours.

```text
/config tickets reminder-minutes:360
```

Follow [Set up moderation tickets](../../setup/tickets/) to enable capture,
verify the first ticket, or check what pausing will affect.
