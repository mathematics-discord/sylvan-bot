---
title: Working Hours
description: Let your daily availability control the staff ping role.
---

Working Hours adds the configured ping role during your daily availability and removes it outside those hours. On Mathematics, this is the Moderators role. Your moderation powers and colour roles are unaffected.

Use [/working-hours](../../commands/working-hours/) to set or disable your schedule. You must hold a configured eligible role as well as have access to the command. A named timezone follows daylight-saving changes; a fixed UTC offset stays fixed all year. Overnight windows are supported, and windows must last at least 20 minutes. Start times are included; end times are excluded.

While your schedule is enabled, Sylvan controls the ping role and corrects manual changes. An eligible role?s exemption affects staff without an active personal schedule; enabling a personal schedule still hands control to Sylvan.

## Defaults and suspension

Eligible staff without an active schedule receive the ping role by default. Staff with a configured exemption retain manual control instead. Disabling your schedule restores those defaults.

Administrators use [/config working-hours](../../commands/working-conditions/) to choose the ping role, eligible roles and exemptions, or suspend scheduling. Suspending restores ordinary defaults without deleting saved schedules. Changing the ping role leaves the old role unchanged.

Role changes may take a short time. A reply saying your schedule was saved but role updates are pending means the schedule is retained and the role change still needs to finish.
