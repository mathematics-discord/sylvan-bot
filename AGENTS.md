# Sylvan Bot: code review

Sylvan is a C# Discord bot using Discord.NET, EF Core, and PostgreSQL. Prefer straightforward implementations whose behaviour is easy to follow.

This file defines review policy and is self-contained. Apply the rules below when reviewing; they do not authorise implementation work. No other guidance, specification, or documentation file is required.

## Code Review Rules

### Report evidenced, actionable problems

- Establish intended behaviour from the request, PR description, supplied context, and code. Consult other documentation only if it exists and is relevant. Do not invent requirements or treat missing documentation as a defect or a blocker.
- Trace affected callers, registration, persistence, and lifecycle code before concluding that something is broken. Read the relevant surrounding code rather than inspecting the entire repository by default.
- A finding needs a concrete trigger, a traceable code path, and a meaningful consequence. Explain what fails and under which conditions. Verify framework or library assumptions against the version in use; do not invent API behaviour or requirements.
- Report issues introduced or made worse by the change. An existing problem is relevant when this change makes it reachable or increases its impact. Do not turn a PR review into an unrelated backlog.
- Point to the smallest relevant changed location. Group symptoms with the same cause into one finding. Match severity to demonstrated impact; an empty findings list is a valid result.

### Protect the bot's actual contracts

- Check authorisation, guild isolation, role hierarchy, and component ownership where the change affects them. Account for existing framework preconditions and shared checks before requesting duplicate validation.
- Preserve command and component contracts, option semantics, and honest user responses unless the task intentionally changes them. Check interaction acknowledgement, Discord payload limits, and unintended mentions where relevant.
- Check changed database work for data loss, incorrect queries, lost updates, and context lifetime or concurrency errors. Preserve deployed migration history and existing data unless removal is explicitly intended. Removing an EF model does not itself authorise dropping stored data.
- Check changed asynchronous work for lost events, shared mutable state, blocking gateway callbacks, unobserved failures, and incorrect resource ownership. A database transaction cannot roll back a Discord operation; assess the actual partial-failure path.

### Keep the review proportionate

- Respect the agreed scope and explicitly accepted trade-offs. Distinguish an intentional limitation from a regression. Do not infer stronger delivery, durability, or abstraction guarantees merely because they would be desirable. Where intended behaviour is unclear, report only what the evidence establishes.
- Missing new tests is not a defect by itself. Identify the actual broken behaviour or concrete unprotected rule; do not request tests merely for changed methods, wiring, generated migrations, or coverage. Do not demand production scaffolding just to make mocking easier.
- Direct command/event workflows, direct EF Core access, concrete constructor dependencies, and clearly owned mutation are acceptable. Do not request extra layers, interfaces, wrappers, factories, or files solely to satisfy an architectural pattern.
- Leave formatting and mechanical checks to tooling. Do not present naming preferences, `var` usage, stylistic alternatives, or optional cleanup as correctness findings. If explicitly asked for readability or architecture feedback, distinguish that feedback from defects.
- Recommend the smallest correction that addresses the evidenced problem. Review without modifying files, staging changes, or starting a rewrite unless separately asked to implement fixes.
