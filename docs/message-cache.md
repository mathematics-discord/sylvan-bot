# Persistent message cache

Moderation edit, delete and bulk-delete logs use immutable snapshots in memory, backed by PostgreSQL. Only guild
messages from human users in enabled logging scopes are captured. Output channels, excluded channels, parent channels
and categories keep their existing exclusions. No historical Discord messages are fetched to populate the cache.
Single-deletion attachment downloads still happen only when building the existing deletion log; the cache stores URLs,
content types and sizes, never attachment bytes. Bulk transcripts retain their existing JSONL format.

Each snapshot expires at its original message snowflake creation time plus **24 hours in UTC**. Edits and restarts
cannot renew it. Reads and publication reject expired snapshots; a worker removes expired memory entries and database
rows every minute, including while the gateway is disconnected. Database outages can delay physical deletion, but never
make expired snapshots available to logs. This lifecycle does not expire published moderation logs.

## Startup and rollout

The normal migration hosted service applies `AddPersistentMessageCache` before gateway startup. It creates only
`cached_messages`, keyed by application ID, guild ID and message ID, with expiry indexes and a 24-hour constraint.
Existing evidence tables and deployed migrations remain unchanged. Review/apply this forward migration through the
normal rollout; no separate backfill is required. The first deployment starts with an empty cache.

After login, the host obtains the stable Discord application identity and current guild memberships through REST.
It hydrates unexpired rows only for that identity and enabled configured guilds before starting the gateway. A failed
hydration aborts startup and never reports readiness. Separate bot applications and the existing separate deployment
databases provide isolation. Run one gateway process per application/database; concurrent replicas and distributed
event replay are not supported.

`DISCORD__MESSAGECACHESIZE=0` disables Discord.NET's client-wide message cache and is now the default. The production
Compose file forwards this variable; explicit existing values remain honoured. It is read when constructing the client,
so changing it requires a restart. Member and other Discord.NET caches and gateway intents are unchanged.

Discord.NET's detached update objects lose the distinction between omitted and cleared fields. A small adapter around
its default WebSocket provider preserves update-field presence during typed event dispatch. It requests Discord's
uncompressed JSON transport rather than implementing another streaming decompressor. This increases gateway bandwidth;
it introduces no REST fetch for edits and stores no raw frames. Missing fields retain known snapshot values; explicit
empty content or attachment lists clear them. An eligible edit can seed a miss only when author, content, attachments and timestamp state are supplied.
Omitted attachments are never treated as empty. A seed cannot supply an invented before version.

Application inputs distinguish a complete `MessageReceived` from a partial `MessageEdited`. Edited fields use
`Option<T>` consistently: `None` preserves known state, while `Some("")` or `Some(emptyAttachments)` explicitly clears
content or attachments. Retained `CachedMessageSnapshot` values are complete and contain no omission markers.
The wire timestamp is optional and nullable, represented by `Option<DateTimeOffset?>`: omission preserves it,
an explicit null reports no edit timestamp, and a supplied timestamp sets it. A null or older timestamp cannot roll
back a newer known edit.

Logging configuration and exclusions apply on each processed observation and publication. Changes take effect on
subsequent handling without restarting. Existing snapshots from a newly excluded/disabled scope remain inaccessible to
logs and expire on their original schedule. Re-enabling a scope captures new observations, without backfilling gaps.

## Ordering and failures

One hosted reader owns the moderation event workflow. Gateway callbacks project values immediately and await admission
to a 128-event FIFO. Full queues apply backpressure instead of dropping events or accumulating unbounded pending tasks.
Sustained overload can delay gateway processing and cause reconnects; this cache is not a durable event queue. Each
work item has a 30-second budget. Shutdown stops admission and cancels work; events not yet persisted are not replayed.

Creates and edits await PostgreSQL persistence before memory changes. Failed writes preserve prior state, increment
the handling-failure count and log identifiers and exception type without content or attachment URLs. There is no
write-behind/retry buffer: a failed new observation is lost, and a later eligible observation can recover that message.
An edit log is skipped if its cache write fails. Database command cancellation or connection loss can leave commit
status ambiguous; the current process conservatively keeps its previous memory snapshot.

Deletion freezes the available snapshot before logging. It remains available for another deletion event if publication
fails, until original expiry. There is no automatic log-redelivery worker; Discord.NET's existing request retry handling
is retained. Successful publication removes content from memory and records an ID-only tombstone through original expiry,
preventing duplicate deletion logs and delayed creates/edits from recreating it in this process. Failed database removal
is retried by the minute maintenance pass. A crash between Discord publication and database removal can still cause a
duplicate after restart. This is not an outbox or an exactly-once delivery guarantee.

## Telemetry and manual verification

The existing five-minute `MessageCacheSample` JSONL record and ordinary summary now describe eligible unexpired snapshots.
Decimal-string guild/channel IDs, per-channel count/oldest creation time and process/member-cache measurements remain.
`OldestMessageExpiresAtUtc`, `RetentionHours`, `CacheReady`, `HandlingFailures` and `MaintenanceFailures` replace the old
per-channel capacity/fullness assumptions. Snapshots contain no authors, text or attachment URLs. Counts represent a
sampling pass rather than atomic eviction evidence. Storage and memory usage scale with eligible traffic over 24 hours;
there is no per-channel count eviction.

Use a development Discord application to confirm create/edit/delete and mixed bulk-delete presentation with
`MessageCacheSize=0`, including an edit that clears attachments and a restart between receipt and deletion. These are
manual framework/client checks. Automated cache tests use plain values and a narrow persistence fake; they do not mock
Discord.NET or EF Core or run production migrations.

Maintenance and sampling have independent hosted workers and execution budgets. Cleanup failures do not suppress the
five-minute sample. The sampler receives only per-channel counts and oldest creation/expiry times, never snapshots.
Deletion uses three explicit states: awaiting publication, awaiting database removal, and removed. Only the latter two
suppress repeat publication; all expire at the original deadline. The event worker distinguishes processing timeouts
from shutdown cancellation and owns unexpected publication failures.

Complete receive/snapshot records require explicit values. Gateway extraction and hydration validate external input;
malformed supplied content is rejected rather than treated as a clear. Hydration corruption aborts readiness. Cache edits
return an optional successful change with a required current snapshot and an optional previous snapshot. Persistence
failures remain exceptions and preserve prior memory state. The existing Option implementation and stored JSON format
are unchanged.
