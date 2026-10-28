# Architectural Guidelines for Sylvan Bot

Sylvan is a Discord bot. Its architecture should make its commands, events, and background work easy to follow and
change. Minimise the code, concepts, duplicated state, and files required to understand a feature. Keep whitespace and
names that make the remaining code readable.

These are defaults. Feature requirements and explicit tasks can change them. Preserve unrelated work; do not turn a
small change into a repository-wide reorganisation. Agent workflow and detailed formatting rules live
in [AGENTS.md](../AGENTS.md).

## Build the feature directly

A settings command can check permissions, open a context, validate and update a row, save, and reply in one method. A
gateway handler can own the corresponding event workflow. Neither needs a separate command object, application handler,
service, repository, mapper, and outcome hierarchy.

Start with ordinary code. Extract a method when a substantial step benefits from a name. Extract a concrete class when
it owns meaningful shared behaviour, substantial independent logic, or state with its own lifetime. Introduce an
interface for a real interchangeable implementation or useful external boundary. Being mockable is insufficient reason
on its own.

Keep feature-specific commands, event handling, formatting, and persistence implementations in
`SylvanBot/Features/<Feature>/`. Application workflows, rules, values, and deliberate external contracts belong in
`SylvanBot.Application/Features/<Feature>/` when a feature uses that boundary. The application project may use EF Core
directly; Discord.NET stays in Discord-facing code, and the host implements its external contracts. Small features may
fit in one file. Tiny supporting types may live with their owner. Keep genuinely shared hosting, dispatch,
configuration, and `BotDbContext` infrastructure shared. Migrate other features only when requested.

Do not build mediators, command buses, generic repositories, units of work, or chains of forwarding services. Extract
abstractions when existing work demonstrates their value.

## Let the libraries work

Use available Discord.NET objects directly inside Discord-facing implementations. Keep framework metadata, context
validation, acknowledgement, and response formatting at that edge. Application workflows receive the values they need
through deliberate external contracts; project SDK objects once at that boundary. Do not convert a member into an ID
simply to fetch it again, mirror the entire library model, or duplicate a library cache.

Keep dependency injection simple: declare concrete dependencies in constructors and register them explicitly in
`SylvanBot/DependencyInjection.cs`. Resolve from a container at genuine framework or lifetime boundaries. Create a scope
because a dependency needs one, not because an operation is asynchronous. An alternative container needs a specific
benefit.

Use ordinary classes and mutation when ownership is clear. Pick collections by their operations; a local growing list is
fine. Prefer `ImmutableArray<T>` for completed results where immutability is useful. Keep existing option/result types
when they clarify a contract; do not wrap every nullable property or add a type for every branch. Native unions should
represent meaningful alternatives without losing simultaneous changes or explicit clearing.

## Keep code readable

Use `var` when the type is evident; spell it out when that helps. Keep descriptive names, explicit construction, and
visible stages of work. Name complicated decisions before control flow, lay LINQ/EF pipelines out vertically, put split
Boolean operators first, and separate multiline statements with blank lines. Preserve short-circuiting and null safety.

Do not compress code to hit a line target or manufacture helpers for obvious expressions. Do not add blanket
`ConfigureAwait(false)` in application code. Follow `.editorconfig` and the detailed layout rules in `AGENTS.md`;
CSharpier is retired.

## Preserve data and Discord behaviour

Use EF Core directly for ordinary persistence without repository or unit-of-work wrappers. A scoped workflow may inject
`BotDbContext`; a singleton or worker normally creates a context through `IDbContextFactory<BotDbContext>`. Dispose
contexts you create and never share one across concurrent operations. Keep queries bounded, guild-scoped, and untracked
when entity tracking is unnecessary. Retain useful existing Dapper/Npgsql code and parameterise SQL.

Preserve deployed migration history and data. Use forward migrations for schema/model changes and review their actual
effects. Retiring application use of a table does not authorise deleting its rows. Use transactions and constraints for
concrete invariants. A Discord action and a database write cannot be made atomic by a database transaction; handle
partial failure proportionately.

Keep permissions, role hierarchy, component ownership, mention controls, audit reasons, and guild isolation explicit.
Preserve meaningful evidence and IDs when live Discord objects disappear. Choose cache/REST access deliberately, defer
slow interactions, and report what actually happened.

Every background task or mutable shared state needs clear ownership, cancellation, failure handling, and restart
behaviour. Use one ordinary loop when sufficient. Add custom queues, retries, clocks, and locks only for a demonstrated
requirement. Respect an explicitly best-effort feature; do not silently turn it into a durable delivery system. Balance
subscriptions and own shutdown. Keep secrets out of Git and validate important configuration at startup.

## Test proportionately

Tests are useful when they protect an actual rule or plausible regression. There is no obligation to test every method
or add tests to every change. Routine wiring, generated migrations, pass-through methods, and structural cleanup usually
need a build, existing checks, and review.

Keep useful existing tests. Adapt them when their public contract changes and delete those for removed behaviour. Do not
retain production scaffolding to satisfy tests or add elaborate fixtures for private structure. Avoid source-text
assertions, reflection policing, file/type existence tests, routine registration tests, and tests that repeat the
implementation.

Use the existing NUnit, Shouldly, and NSubstitute tooling when a test earns its place. Make the scenario and expected
result obvious, with readable Arrange/Act/Assert sections. Use PostgreSQL/Testcontainers when relational/provider
behaviour matters; no live Discord calls. Build Release and run the affected existing checks. Report unavailable checks
honestly without upgrading dependencies or weakening diagnostics.

## Review checklist

- Does the change implement the requested behaviour and preserve permissions, guild boundaries, data, and meaningful
  failures?
- Can a reader follow the operation locally? Did an extraction remove complexity, or merely add another place to visit?
- Does each new class, interface, cache, queue, and result type have a present job?
- Are context lifetimes, concurrent state, subscriptions, cancellation, and partial failures handled correctly?
- Do tests protect useful behaviour, and were appropriate checks actually run?

Explain significant operational, data, or dependency decisions briefly beside the feature or in a decision record when a
lasting rationale is needed. Routine refactors require no ADR. Keep this guide short; feature-specific requirements
belong with their feature.
