---
title: /config automod
description: Control Automod actions and role exemptions.
---

Run these commands inside the server. All three commands reply publicly, including exemption changes and the settings list.

## Enable or disable actions

```text
/config automod set enabled:false action:Ban
/config automod set enabled:true
```

`enabled` is required. `action` is optional: choose Delete, Note, Mute or Ban, or omit it to change all four. Actions are enabled by default. Disabling an action retains its rules and exemptions. Enabling Note still does not execute Note rules.

## Change an exemption

```text
/config automod exempt role:@Example enabled:true
/config automod exempt role:@Example enabled:false
```

Both options are required. Choose a role in this server. `true` exempts members with that role; `false` removes that exemption. Other exemptions still apply. Exempting `@everyone` exempts the whole server.

## Show settings

```text
/config automod show
```

Shows all four action switches and the exempt roles, including saved references to deleted roles. It does not list rules; use [/automod list](../automod/#list-rules).

A reply may say the settings were saved but could not be applied immediately. The previous active settings remain in use until a successful refresh. See [Automod](../../features/automod/) for enforcement and timed-ban behaviour.
