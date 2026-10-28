---
title: /config image-spam
description: View or adjust detection of image bursts across several channels.
---

Run this command in a server. Replies are visible only to you.

## View the settings

```text
/config image-spam
```

Sylvan shows the current settings, or says that image spam detection has
not been configured for the server.

## Change the settings

```text
/config image-spam number-of-channels:3 attachment-count:2 burst-duration:10
```

| Option               | Meaning                                                                  |
|----------------------|--------------------------------------------------------------------------|
| `number-of-channels` | How many distinct channels must be involved in a burst.                  |
| `attachment-count`   | How many attachments a message needs before it counts towards detection. |
| `burst-duration`     | The time window for a burst, in seconds.                                 |

Every supplied value must be a whole number greater than zero. All options
are optional: supply only the settings you want to change. If no settings
exist yet, supplying an option creates a configuration and uses defaults
for the omitted values.
