---
title: Slash commands and Apps shortcuts
description: Find a Sylvan command by its name in Discord. Look up the fields, access requirements and result in the linked guide.
---

Type `/` in a server channel to find a slash command. For an **Apps** shortcut, open the menu on a member or message. Each guide explains who can use the command and where its reply appears.

<span id="user-commands"></span>

## Member commands

| Command | Use it to |
| --- | --- |
| [`/colour list`](./color/#list) | See the server’s display colours and the role needed for each. |
| [`/colour set`](./color/#set) | Choose a colour or reset your preference. |
| [`/hello`](./hello/) | Ask Sylvan for a greeting. |

To [contact the moderators](../features/sylvanmail/), send a direct message to ModMail.

<span id="moderator-commands"></span>
<span id="senior-moderator-commands"></span>

## Staff commands

Ticket access depends on the action, the original moderator and your Discord permissions. Check [who can do what](../features/tickets/#who-can-do-what) if a command is unavailable.

| Commands | Use them to |
| --- | --- |
| `/ticket show`, `/ticket showhidden` | [Look up a ticket or a member’s history](./tickets/#look-up-tickets). Both commands post successful lookups in the channel. |
| `/ticket note`, `/ticket edit`, `/ticket set`, `/ticket append`, `/ticket top` | [Create a note, correct a record or work through your incomplete tickets](./tickets/#create-and-edit). |
| `/ticket review`, `/ticket revert`, `/ticket hide` | [Review the queue, lift a restriction or change a record’s visibility](./tickets/#review-and-reverse). |
| `/working-hours` | [Set your daily ping schedule](../staff/working-hours/). You need **Mute Members** and an eligible staff role to set hours. |
| `/automod add`, `/automod list`, `/automod remove` | [Manage message-matching rules](./automod/). You need **View Audit Log**. The rule list posts in the channel. |
| `/yoink` | [Copy a custom emoji](./yoink/). You need **View Audit Log**. |

## Apps shortcuts

| Open the menu on | Choose |
| --- | --- |
| A server member | [**List Tickets** or **Add Moderation Note**](./tickets/#discord-apps-menu). |
| A ticket message | [**Manage Ticket**, **Approve Ticket**, **Revert Ticket** or **Hide Ticket**](./tickets/#discord-apps-menu). |
| A message containing a custom emoji | [**Yoink emoji**](./yoink/). |

<span id="administrator-commands"></span>

## Server configuration

These commands require **Administrator**. When a command has subcommands, choose one of the names in the second column.

| Command | Subcommands | Guide |
| --- | --- | --- |
| `/config automod` | `show`, `set`, `exempt` | [Set Automod actions and exemptions](./automod-admin/). |
| `/config colour` | `upsert`, `delete`, `sync` | [Offer a display colour or change who can use it](./color-admin/). |
| `/config image-spam` | — | [Set the thresholds for attachment-spam bans](./spam/). |
| `/config logs` | `setup`, `exclude`, `include`, `exclusions` | [Choose where edit and deletion logs go](./logging/). |
| `/config persistence` | `add`, `list`, `remove` | [Choose which roles return when a member rejoins](./persistence/). |
| `/config rolereact` | — | [Attach a role to a reaction](./role-react/). |
| `/config sylvanmail` | — | [Set the private forum and notification role](../setup/sylvanmail/). |
| `/config tickets` | — | [Look up ticket configuration options](./config-tickets/). |
| `/config working-hours` | `show`, `ping-role`, `eligible-role`, `exempt-role`, `enable`, `disable` | [Configure moderator ping schedules](../setup/working-hours/). |
