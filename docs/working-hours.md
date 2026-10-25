# Working Hours

`/working-hours` sets the invoking staff member's daily roster. Responses are ephemeral and suppress mentions. There
is no command for editing another member. Administrators configure the guild through `/working-conditions`.
A guild starts disabled; legacy environment/appsettings configuration is ignored.

```text
/working-hours enable:true start:09:00:00+01:00 end:17:00:00+01:00
/working-hours enable:true start:09:00:00 end:17:00:00 timezone:Europe/London
/working-hours enable:true start:22:00:00Z end:07:00:00Z
/working-hours enable:false
```

Enabling requires both endpoints every time. Disabling accepts **only** `enable:false`; the saved window remains
available in storage but is disabled.

## Input contract

This application uses the `full-time` and `partial-time` productions
from [RFC 3339 §5.6](https://www.rfc-editor.org/rfc/rfc3339.html#section-5.6), rather than complete timestamps or ISO
8601 generally. Dates are forbidden. Use ASCII `HH:MM:SS`, optionally followed by `.` and any positive number of ASCII
fractional digits. Seconds are mandatory. Hours are 00–23; minutes and seconds are 00–59. Whitespace, a leading `T`,
comma fractions and trailing text are rejected.

Choose exactly one mode:

| Mode         | Endpoints                                                         | Timezone option                              |
|--------------|-------------------------------------------------------------------|----------------------------------------------|
| Fixed offset | Both include `Z`, `z` or `±HH:MM`, with numerically equal offsets | Absent                                       |
| Named zone   | Both omit offsets                                                 | A supported IANA ID, such as `Europe/London` |

`Z`, `z` and `+00:00` are equivalent. Fixed offsets through `±23:59` are accepted; they remain fixed throughout the
year. Equal offsets are an application rule. Named zones follow the application's bundled NodaTime TZDB, including
supported aliases. Autocomplete filters that catalogue and returns at most 25 suggestions; final IDs are validated
independently. Windows timezone names are rejected. The authoritative catalogue is
the [IANA Time Zone Database](https://www.iana.org/time-zones).

Scheduler restrictions: leap-second boundaries are rejected because a date-free recurrence cannot establish their
validity ([RFC §5.7](https://www.rfc-editor.org/rfc/rfc3339.html#section-5.7)). `-00:00` is rejected because it denotes
an unknown offset ([RFC §4.3](https://www.rfc-editor.org/rfc/rfc3339.html#section-4.3)).

Fractional seconds are stored as exact, canonical decimal text, without a tick or nanosecond precision cap. Equivalent
fractions compare equally. Duration, ordering, night overlap and policy thresholds use those exact values. Only dated
execution boundaries round upward to **100-nanosecond ticks**, carrying into the next second/date where needed; rounding
never acts early.

## Daily recurrence and recovery

Schedules repeat every day, seven days a week. The start is inclusive and the end exclusive. An earlier end means the
next local day; equal endpoints mean zero duration, never 24 hours. Nominal wall-clock duration determines all response
policies, regardless of DST.

Named-zone ambiguous boundaries use the earlier occurrence. Nonexistent boundaries move forward by the gap. Both
boundaries are resolved as a dated pair; empty or inverted occurrences are skipped. A completed shift does not
reactivate during the second occurrence of a repeated hour. Overnight evaluation includes the preceding local date.

One cancellable worker evaluates current desired state after startup/reconnect and every five seconds between passes.
The interval is internal, not configurable. It never replays missed transitions. Discord rate limits, outages and pass
duration can delay delivery. Commands save first and immediately attempt reconciliation.

A ready gateway member cache is required. Relevant staff and ping-role holders are indexed once at readiness and after
role-configuration changes; gateway member events maintain that index. Polls combine it with saved enabled preferences,
without downloading or scanning the whole guild every time. Normal role decisions use gateway state. A targeted REST
read is used only for a relevant member missing from the ready cache; only an explicit Unknown Member response confirms
departure. Cache misses, nulls and failed lookups are unknown, not permission to discard preferences.

Commands and worker passes hold one asynchronous gate per guild across database work and awaited role writes. A command
waits for an earlier pass, so after a disable or configuration change reports success that pass cannot issue stale
writes.
The runtime assumes one bot process owns each guild; the gate is not distributed coordination. Gateway callbacks update
only a small index and pending confirmed-loss set, without database work or fire-and-forget reconciliation.

Role requests are idempotent. A database save must succeed before command-driven role updates begin. If Discord fails,
the settings/preferences stay saved and the response reports reconciliation pending. Subsequent passes retry. Successful
requests are not followed by verification downloads and do not claim an atomic snapshot of Discord state.

## Runtime administration

All `/working-conditions` subcommands are guild-only and ephemeral. The group has Discord's default Administrator
permission and an execution-time Administrator precondition. Manage Roles or staff eligibility alone does not authorise
these commands. They remain available while disabled or unconfigured.

| Command                                          | Effect                                                                                                |
|--------------------------------------------------|-------------------------------------------------------------------------------------------------------|
| `show`                                           | Read-only: show enabled state, ping role, eligible/exempt roles and missing or invalid configuration. |
| `ping-role role:<role>`                          | Set the one target while disabled. **Assignments of the old role remain unchanged.**                  |
| `eligible-role action:<add\|remove> role:<role>` | Maintain participation roles.                                                                         |
| `exempt-role action:<add\|remove> role:<role>`   | Maintain exemptions among eligible roles.                                                             |
| `enable`                                         | Validate configuration and bot permissions, enable, and reconcile the current time.                   |
| `disable`                                        | Save disabled state and attempt ordinary staff default-role restoration.                              |

Role options are typed Discord roles. Start with a ping role and at least one eligible role; add exemptions as needed,
then enable scheduling. Incomplete configuration is allowed while disabled and never mutates roles. Once configuration
is complete, disabled-mode reconciliation applies the baseline below, even before the first enable.

The target cannot be @everyone, integration-managed, eligible or exempt. Eligible roles cannot be @everyone. Exemption
requires an existing eligible role and is never inferred from permissions, names or hierarchy. The bot needs Manage
Roles
and a highest role above the target. Only the configured ping role is changed; power, colour and participation roles are
untouched. Invalid submissions leave settings unchanged. Repeated additions/removals are harmless.

Remove a role's exemption before removing its eligibility. Disable before removing the last eligible role or replacing
the target. A target replacement **does not bulk-remove or migrate the old role**. When ready guild metadata confirms a
role was deleted in Discord, the worker removes its eligibility/exemption reference or clears the target. If required
configuration becomes incomplete, the worker saves disabled state. `show` never performs this cleanup. Missing bot
permissions block writes until repaired; no substitute role is modified.

## Guild switch and staff policy

| Current state                                             | Ping role                                        |
|-----------------------------------------------------------|--------------------------------------------------|
| Disabled guild; eligible, non-exempt                      | Grant, regardless of personal schedule           |
| Disabled guild; eligible, exempt                          | Leave unchanged, regardless of personal schedule |
| Enabled guild; eligible, enabled schedule, inside window  | Grant                                            |
| Enabled guild; eligible, enabled schedule, outside window | Remove                                           |
| Enabled guild; eligible, unscheduled, non-exempt          | Grant                                            |
| Enabled guild; eligible, unscheduled, exempt              | Leave unchanged                                  |
| Confirmed ineligible member                               | Remove; disable any enabled personal schedule    |
| Unknown/unavailable member                                | Preserve state and retry                         |

Default grants apply only to configured eligible staff, never ordinary server members. Unscheduled includes members who
have never used `/working-hours`. When the guild is enabled, schedules override manual changes to the target, including
for exempt staff. Losing exemption activates the ordinary unscheduled default. Every grant checks current eligibility.

Global disable retains settings, windows and personal enabled flags. `/working-hours` then reports scheduling disabled
without changing preferences or roles. Disable responses distinguish the saved switch from completed restoration: failed
grants or unavailable member data are reported as pending and retried. Saved schedules cannot remove roles while
disabled.
Re-enabling resumes saved enabled schedules at the present time. By contrast, `/working-hours enable:false` while the
guild is enabled disables just that person's schedule and applies their ordinary/exempt default.

Confirmed departure or loss of eligibility disables the old personal schedule but preserves its window. An observed loss
followed by reappointment between polls still invalidates that schedule; a later explicit enabling command can establish
it again. Reappointed unscheduled staff get their ordinary/exempt defaults. Events missed while offline cannot be
reconstructed: recovery can establish current ineligibility, not an unseen loss and reappointment.

## Storage and upgrade

Guild settings and eligible roles are database-backed; exemptions are a flag on eligible roles. There is no environment
configuration fallback. Existing personal rows and exact endpoint text remain unchanged. The additive settings migration
preserves the original Working Hours migration and data. Existing installations start disabled until administrators
configure them. Normal application startup applies migrations before the worker starts.

## Responses and statutory notices

Durations below one minute receive the time-file rejection; one to under ten minutes receive
`VERY FUNNY. AREN'T YOU FUNNY. HEHEHEHE.`; ten to under twenty receive `Okay nah that's too low vro`. Rejected
submissions change neither preferences nor roles and contain no accepted notices. Twenty minutes to under an hour
receive the People & Culture reflection. Longer shifts are accepted. Entire shifts within the continuous 22:00–07:00
night receive thanks; otherwise finishes strictly after 22:00 or before 07:00 receive the bedtime response. Midnight
counts; exact 22:00 and 07:00 do not.

Accepted daily rosters accumulate these Working Time Regulations 1998 (SI 1998/1833) notices:

- More than six
  hours: [20-minute rest break, regulation 12 (1), (3)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/12).
- Exact daily duration × seven greater than 48
  hours: [average weekly limit, regulation 4 (1)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/4), with the
  normal 17-week reference period.
- More than thirteen
  hours: [eleven consecutive hours of daily rest, regulation 10 (1)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/10).
- Every accepted daily
  roster: [weekly rest, regulation 11 (1), (2)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/11).
- Regular night work with more than eight hours of whole daily
  duty: [night-worker hours, regulation 6 (1)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/6).
- Regular night
  work: [opportunity for a free health assessment, regulation 7 (1)(a), (b)](https://www.legislation.gov.uk/uksi/1998/1833/regulation/7).

At least three hours of overlap with the agreed 22:00–07:00 night period defines regular night work for these responses,
under [regulation 2 (1)'s definitions](https://www.legislation.gov.uk/uksi/1998/1833/regulation/2). This is the agreed
period, not the statutory default. Notices create no actual breaks, health records or personnel workflow.

The [architecture decision](decisions/0001-working-hours.md) explains time precision, storage and
reconciliation ownership.