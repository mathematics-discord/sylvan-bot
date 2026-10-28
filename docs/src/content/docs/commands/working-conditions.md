---
title: /config working-hours
description: Configure the roles and availability rules used by Working Hours.
---

Run these commands in the server you want to configure. Replies are visible
only to you.

Members set their own schedules with [/working-hours](../working-hours/).
These commands control which roles can participate and which ping role
Sylvan adds or removes.

## Check the current configuration

```text
/config working-hours show
```

Sylvan lists the enabled state, ping role, eligible roles and exemptions.
It also explains any configuration problems or pending role updates.

## Set up the roles

While scheduling is disabled, choose the ping role and add at least one
eligible staff role:

```text
/config working-hours disable
/config working-hours ping-role role:@Moderators
/config working-hours eligible-role action:add role:@CommunityModerators
```

- The ping role is the role Sylvan adds and removes as availability changes.
  It cannot be `@everyone`, integration-managed or one of the eligible roles.
- Eligible roles let their members set a schedule. They cannot be
  `@everyone` or the ping role.

Changing the ping role leaves the old role unchanged. Review the old role
separately if you no longer want members to keep it.

To remove an eligible role:

```text
/config working-hours eligible-role action:remove role:@CommunityModerators
```

Remove its exemption first, if it has one. Disable scheduling before
removing the last eligible role.

## Set exemptions for staff without a schedule

An eligible role can also be exempt:

```text
/config working-hours exempt-role action:add role:@SeniorModerators
/config working-hours exempt-role action:remove role:@SeniorModerators
```

Add the role to the eligible list before adding an exemption. For eligible
staff without an active schedule, exemptions leave their ping role unchanged.
Other eligible staff receive the ping role by default. An enabled personal
schedule still controls the ping role for someone with an exemption.

## Enable or suspend scheduling

```text
/config working-hours enable
/config working-hours disable
```

Enabling requires a valid ping role and at least one eligible role. Disabling
suspends schedule-based changes and restores the normal defaults described
above; it does not erase members' saved schedules. Read Sylvan's reply for
any role updates still waiting to complete.
