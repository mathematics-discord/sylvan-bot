---
title: /yoink
description: Copy a custom emoji into your server.
---

Use this feature in the server that should receive the emoji.

## Copy an emoji with a command

Run `/yoink` and put one custom emoji in its required `emoji` option:

```text
/yoink emoji:<:example:123456789012345678>
```

Use the actual custom emoji you want to copy, rather than the example ID.
The option accepts a single custom emoji, including animated emoji. Ordinary
Unicode emoji, image URLs and surrounding text are not accepted.

## Copy an emoji from a message

Open a message's context menu, choose **Apps**, then **Yoink emoji**.
The message must contain exactly one custom emoji, and it must not already
belong to this server. Text around that emoji is allowed. The action reads
the message body, not its reactions or attachments.

Sylvan posts the newly added emoji when the import succeeds. If the server
has no available emoji slots, the source is unavailable, or Discord rejects
the image, the reply explains the problem. Downloads larger than 256 KiB
are rejected.
