# Feature ownership and rollout

Commands and gateway adapters stay at the Discord boundary. Concrete feature workflows own database contexts and operation ordering. There is no shared repository, mediator or role-management engine.

| Feature | Ownership and pattern |
| --- | --- |
| Working Hours | Configuration and schedule application services share the guild gate owned by `WorkingHoursRuntime`. `WorkingHoursReconciler` runs the reconciliation loop. Gateway observations use only the guild state's short synchronous lock; no awaited work runs under it. |
| SylvanMail | Configuration, draft confirmation and relay use the same PostgreSQL advisory lock. The owning workflow creates the context and releases the lock. Conversation and reporting collaborators borrow that context. Delivery intent is saved before sending; uncertain sends are never automatically replayed. |
| Moderation logging | The bounded, ordered producer-consumer worker coordinates the write-through message cache and publisher. Scope resolution and Discord publication do not own or remove cached evidence. A successful publication precedes deletion completion. |
| Role Reactions | Configuration and reaction application share `RoleReactionRuntime`'s guild gate. Parsing and permission policy remain at the Discord boundary. |
| Role Colours | A plain-value selection policy chooses removals; the reconciler owns serialisation, live Discord effects and readback. Current and previous grants remain distinct. |
| Image spam | The settings service owns published settings and serialises database loads/updates. The detector owns observation history and receives settings as an ordinary value. |
| Emoji analytics | The existing worker owns its transient buffer and batching. The buffer remains unbounded and failed guild batches remain discarded, as before; this refactor does not change its loss policy. |
| Interactions | The handler alone owns completion deduplication. Registration, diagnostic enrichment and failure responses have separate concrete owners. |

Migrations run before dependent features. Consumers and readiness subscriptions start before gateway admission. Cache hydration still completes before gateway processing. Shutdown stops gateway admission first, then stops consumers; pending transient events retain their existing cancellation behaviour rather than gaining an outbox or indefinite drain.

## Configuration commands

All guild configuration commands are under administrator-only `/config`, protected by Discord registration permissions and runtime preconditions:

- `/config logs setup`, `exclude`, `include`, `exclusions`.
- `/config working-hours show`, `ping-role`, `eligible-role`, `exempt-role`, `enable`, `disable`.
- `/config colour upsert`, `delete`, `sync`.
- `/config image-spam`.
- Existing `/config sylvanmail` and `/config rolereact`.

The personal `/working-hours` and `/colour` commands retain their permissions. Deployment configuration remains in its existing environment files; no environment-variable changes are required by this refactor. Normal guild command registration removes the obsolete command routes on startup.

## Quorum retirement

`RemoveQuorum` drops `public.quorum_voter_roles` and `public.quorum_configurations`. Take a database backup before deployment. Historical migrations remain intact. The down migration recreates schema only; it cannot recover deleted Quorum settings. All other feature data is retained.

After deployment, check the new command routes with administrator and non-administrator accounts, then check representative Working Hours/role updates, SylvanMail confirmation and moderation logs in Discord. These are manual checks; local rule tests and source inspections do not establish live Discord behaviour.
