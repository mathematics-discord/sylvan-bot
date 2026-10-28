---
title: /colour
description: Choose, reset, or list the display colours available in your server.
---

Use `/colour` to choose a display colour or see how the server's colour
roles are set up.

## set

Open the colour picker. There are no command options to fill in.

```text
/colour set
```

Choose a colour from the menu to apply it immediately. Sylvan removes
your other configured display colour roles before applying your choice.
To remove your display colour without choosing another, press Reset.

The menu only shows colours that your current roles allow you to use.
Only you can use your menu. After a selection or reset, run `/colour set`
again if you want to make another change.

If no colours are available to you, Sylvan shows only the Reset button.
Ask a server administrator about getting access to a colour. If you have
more than 25 available colours, Sylvan asks you to contact an administrator
and still lets you reset your colour.

If a selection is no longer available or an update fails, Sylvan explains
the problem. Check your roles and run the command again to retry.

## list

Show all colour mappings configured for this server. There are no options.

```text
/colour list
```

A mapping connects a source role, which grants access to a colour, to a
display role, which supplies that colour. For example, a Members to Purple
mapping lets anyone with the Members role choose Purple.

Each line shows the mapping ID, the source role, and the display role.
The list includes all configured mappings, even those you cannot currently
use. Listing a colour does not give you access to it or apply it.

If there are no mappings, Sylvan tells you that no colour options are configured.

Administrators can use the mapping IDs to change or remove mappings with
[/config colour](../color-admin/).
