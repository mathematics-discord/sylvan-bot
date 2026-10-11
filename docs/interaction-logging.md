# Interaction Logging

RatBot interaction logs use one ambient Serilog `LogContext` scope per Discord interaction. Logs written while a command, component, or modal is executing inherit the same correlation fields automatically.

## Common Fields

Every interaction-originated log should carry these fields:

- `log_area`: always `interaction`
- `interaction_id`, `interaction_type`, `interaction_name`, `interaction_created_at_utc`
- `user_id`, `guild_id`, `channel_id`
- `command_name` for application commands, using the full slash path
- `service_instance_id`, `process_id`

Execution logs add command metadata when Discord.Net has resolved the command:

- `command_group`
- `command_module`
- `command_leaf_name`
- `command_method`
- `command_run_mode`

Event-specific logs should set:

- `event_kind`: stable event name, such as `interaction.command_invoked`
- `component`: workflow component, such as `interaction_dispatch`
- `outcome`: stable result, such as `success`, `failed`, or `received`

## Data Policy

Log Discord IDs, command names, outcomes, counts, timings, and status values by default.

Do not log message content, modal content, free-form option text, usernames, channel names, or role names unless a future change explicitly opts in for a narrow reason.

## File Logs

Daily rolling text files are written to `logs/` relative to the bot's working directory. Production keeps them in `/opt/ratbot/production/logs` through a bind mount. `info-YYYYMMDD.log` includes Information, Warning, Error, and Fatal events; `error-YYYYMMDD.log` includes Error and Fatal events.