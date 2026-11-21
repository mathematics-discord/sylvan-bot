# Sylvan code review

For review requests, leave the tree and index untouched. Report regressions introduced or worsened by the diff. Give the trigger, the changed line responsible, and the observable consequence. Follow a caller or check the installed framework version when the finding depends on it. An empty findings list is valid.

## Review guidelines

- Sylvan uses `Optional<T>` when absence carries meaning in an internal contract. Convert nullable boundary inputs where appropriate. Preserve distinctions such as omitted versus explicitly cleared. Ordinary C# idioms such as `TryGet` are fine.
- For changes to moderation or persistence, preserve the contracts the change touches, including guild isolation, permission checks, and audit history. Removing an EF model does not authorise dropping deployed data. For changes spanning Discord and the database, account for what has already succeeded when the other operation fails.
- Respect the requested scope and accepted trade-offs. A direct workflow, a concrete dependency, or a change without a new test needs no finding of its own. Report architectural or stylistic preferences only when that feedback is requested.
