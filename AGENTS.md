# Agent instructions for Sylvan Bot

These instructions apply throughout the repository, including future refactors. Explicit task instructions take precedence. For architectural choices, this file supersedes older guidance that requires Discord isolation, layered architecture, CQRS, custom result frameworks, or mandatory request/response models. Existing code is evidence of current behaviour, not a requirement to reproduce its architecture.

## Architectural rule

Build the smallest clear implementation of the requested behaviour. Minimise concepts, indirection, duplicated state, and the number of files a reader must follow. Line count alone is not simplicity: retain code that makes permissions, concurrency, ownership, or failure handling explicit.

This is a Discord bot. Discord.NET is an ordinary dependency of bot code, not an external system to hide behind a platform-independent application API. Do not design for another chat platform, another database, or a hypothetical future consumer.

## Direct code is the default

- A command module or gateway handler may contain the complete workflow: validate, check permissions, query or update the database, call Discord, and respond. Do not extract it merely because it contains "business logic".
- Start with ordinary methods and direct calls. Extract private methods for readability, pure functions for substantial calculations, and concrete feature classes for genuinely shared behaviour or independently owned state/lifetimes.
- Do not create a service, handler, operation, command/query object, mapper, DTO, factory, or interface for each step. Do not split a short workflow into forwarding classes.
- Do not add mediators (including MediatR and forks), command buses, generic pipelines, generic repositories, generic units of work, or a home-grown application framework.
- An abstraction must solve a present, specific problem more clearly than direct code. "Clean Architecture", "SOLID", "decoupling", "future-proofing", and "it can be mocked" are not sufficient reasons.
- Custom interfaces are exceptional. A single production implementation plus a test substitute does not, by itself, justify one. Prefer a concrete class or an existing framework interface. A small seam is acceptable when a meaningful test cannot otherwise control an actual effect; explain that concrete need.
- Do not build a shared framework because two features have vaguely similar code. Share identical rules that must stay consistent; small incidental duplication can be clearer than an abstraction with modes and callbacks.

## Use Discord.NET directly

- Accept, return, and use Discord.NET types wherever they naturally express the operation: `IGuild`, `IGuildUser`, `IRole`, `IMessage`, `IThreadChannel`, interaction contexts, clients, builders, and library enums are all valid feature-level types.
- Use Discord.NET's own interfaces where useful. Do not add a bot-owned `IDiscord...` interface just to rename library operations or convert their results.
- Pass IDs when the operation only needs IDs, and objects when it needs the objects. Do not replace an available Discord object with its ID only to look it up again in a lower layer for architectural purity.
- Do not mirror Discord types in records, wrap every snowflake in a value object, or translate between equivalent models solely to cross a folder or namespace boundary.
- Create a snapshot or projection only when it captures required historical evidence, limits retained data, supports persistence, or simplifies a real computation. Give it that concrete purpose.
- Cache completeness, missing objects, stale permissions, role hierarchy, rate limits, and API failures are real constraints. Keep the checks needed by the feature; direct library use does not waive them.

## Organise around features

- Prefer `RatBot/Features/<Feature>/` for feature-specific modules, rules, data access, entities, configuration, and workers. Small features need not have separate `Commands`, `Gateway`, `Application`, or `Persistence` subfolders.
- Keep genuinely shared startup, interaction dispatch, hosting, configuration, and `BotDbContext` infrastructure together. Do not duplicate these per feature.
- `RatBot/Application`, `RatBot/Infrastructure`, and `RatBot.Domain` are existing locations, not mandatory layers. There is no required domain-to-application-to-adapter call chain or ban on framework references in feature code.
- During an explicitly requested consolidation, move useful code into its feature and remove empty legacy layers/projects when their callers and build references have been updated. Do not introduce replacement layer projects.
- Names and files should explain behaviour. Do not rename everything or enforce a suffix convention as a substitute for removing indirection. Related small types may share a file when that improves navigation.

## Persistence and state

- Use `BotDbContext` directly. Scoped code may inject it; singleton gateway handlers and workers must create short-lived contexts with `IDbContextFactory<BotDbContext>` or resolve scoped work inside an explicit scope.
- Never retain a scoped context in a singleton or use one context concurrently. Dispose contexts/scopes you create. Keep transaction ownership visible.
- Prefer EF Core for ordinary persistence. Do not put forwarding repositories or store interfaces over it. Existing Dapper/Npgsql code may remain where it has a concrete purpose; keep SQL parameterised and do not duplicate persistence paths for the same operation.
- Use `AsNoTracking` for read-only entity queries. Project only the data needed, avoid repeated lookups and per-item database/API calls where batching is practical, and preserve guild scoping in every relevant query.
- Enforce important uniqueness and relationship rules with database constraints. Application checks should provide useful feedback, not replace constraints under concurrent writes.
- Use migrations for schema changes. Preserve deployed migration history and existing data; do not reset databases or delete old migrations to make a refactor easier.
- Use existing identifiers unless requirements demand a change. There is no rule that an internal primary key cannot be used by bot commands, nor a requirement for additional GUIDs or external-key wrappers.
- Give each fact one authoritative owner. Do not persist a second copy of live Discord state unless restart recovery or a specified feature genuinely requires it. Temporary evidence, queues, and derived caches need an explicit purpose and lifetime.
- Immutability is not a driving principle. Use mutable entities, ordinary setters, `List<T>`, and `Dictionary<TKey, TValue>` when they are the clearest solution. Do not require records, `init`/`readonly` members, immutable collections, copy-on-write updates, defensive copies, or factory methods by default.
- Use immutability, snapshots, or restricted mutation only for a concrete need, such as preserving historical evidence or controlling shared access. Preserve actual invariants with the simplest appropriate model; a public setter is not automatically an architectural defect. Choose collections for the operation, not to satisfy a house ideology.

## Outcomes, runtime, and operational behaviour

- Return the smallest useful outcome: a value, nullable value, boolean, tuple, or feature result. Reuse the existing `ErrorOr<T>` where it helps callers distinguish expected failures; do not introduce a competing generic result/error system for architectural uniformity.
- Validation and permission failures should produce controlled responses. Catch recoverable failures where a response or retry can be decided. Log unexpected failures with relevant identifiers; never expose raw internal exceptions to users or claim a failed action succeeded.
- Keep permission checks at the action they protect. Revalidate mutable eligibility where necessary, including component ownership. Preserve mention controls, audit reasons, and guild isolation.
- Respect interaction acknowledgement deadlines; defer before potentially slow work and complete the deferred response. Preserve slash/component/modal registration and user-visible command contracts unless the task changes them.
- Keep DI registration explicit in `RatBot/DependencyInjection.cs`. `IDiscordGatewayHandler` currently provides a shared subscribe/unsubscribe lifecycle; it is not a mandate for another dispatch abstraction. Keep subscriptions balanced and shutdown ownership clear.
- Database migrations currently run before Discord connects. Preserve that ordering. Starting the socket client is not proof that guild/member caches are ready.
- Use hosted workers only for work that actually needs independent scheduling or buffering. Define cancellation, retry policy, failure reporting, and restart behaviour; do not add periodic reconciliation, durable queues, feature flags, or compatibility modes speculatively.
- Pass cancellation to database, HTTP, Discord requests, and waits where available. Do not swallow cancellation or accept a token only to discard it.
- Synchronise necessary shared mutable state explicitly. Preserve ordering needed for invariants, including partial Discord failures and overlapping events; do not remove locks or retries just to reduce lines.

## Tests and verification

- Test observable rules and meaningful failure paths. Prefer real feature code, pure functions, and substitutes for Discord.NET interfaces over a parallel bot-owned model/API designed for tests. Tests must not call live Discord.
- Use the existing NUnit, Shouldly, and NSubstitute conventions. Use PostgreSQL/Testcontainers when verifying SQL translation, migrations, relational constraints, or transactions; EF's in-memory provider is not evidence of PostgreSQL correctness.
- When removing an abstraction, update tests to exercise the resulting behaviour instead of preserving the abstraction for their convenience. Do not add tests for forwarding methods or registration trivia solely to increase coverage.
- Keep regression coverage for permissions, guild isolation, event handling, and partial failure where the change affects them. Do not introduce a test framework or reorganise every test project as part of a narrow feature change.
- For code changes, restore tools as needed, build, run affected tests, and check formatting. Typical commands are:

  ```sh
  dotnet tool restore
  dotnet build RatBot.slnx --configuration Release
  dotnet test RatBot.slnx --configuration Release
  dotnet csharpier check .
  ```

- Infrastructure integration tests require Docker. State exactly what could not run and why; do not report unexecuted checks as passing. Documentation-only changes require a diff/content review, not a bot startup or live deployment.
- Use the repository's SDK/target/language settings, central package versions, `.editorconfig`, and formatter. Do not upgrade the SDK, runtime, packages, or deployment configuration incidentally during an architectural refactor.

## Refactor discipline

- First trace the actual callers, DI lifetimes, event subscriptions, stored state, and tests. Describe the behaviour to preserve and the concrete simplification before a broad change.
- Treat the current README as documentation to verify against code/tests, not authority when they disagree. Resolve discrepancies explicitly rather than accidentally changing behaviour to match a stale sentence.
- Work feature by feature unless a wider refactor is requested. Preserve command names, custom IDs, schema compatibility, permissions, and failure semantics within that scope.
- Remove obsolete implementations, registrations, wrappers, mappings, and tests together. Do not leave parallel "new" and "legacy" implementations or compatibility scaffolding without an actual migration requirement.
- Do not turn this guidance into another mandatory architecture process. Explain substantial trade-offs in the change description; no ADR is required for routine extraction, inlining, or feature consolidation.
- The current deployment workflow deploys pushes to `master` to production. Prepare changes on a branch for review; do not use a direct `master` push as a verification step.

Before finishing, ask: Can a reader follow the feature without traversing forwarding layers? Does every new type carry behaviour, state, or a necessary contract? Did this remove concepts, or merely move them? Delete anything whose only justification is the architectural pattern itself.
