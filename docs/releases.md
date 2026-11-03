# Preparing a release

From the repository root, with Changie 1.26.0, Git, Bash and Python installed:

```sh
bash scripts/prepare-release.sh patch
# Or: bash scripts/prepare-release.sh custom 0.5.0-alpha.1
```

The helper batches pending fragments, merges `CHANGELOG.md` and updates
`version.json`. Review and commit those changes together, including the new
`.changes/v<version>.md` and consumed fragments. Push the release to `master`
when ready to deploy. CD does not run Changie or create commits.

Each master push checks its first-parent history. A batch must advance the
version and add matching, nonempty release notes in the same commit. Ordinary
changes, pending fragments and edits to existing notes do not deploy. Invalid
batches or unavailable history fail detection rather than guessing a release.
Merge commits are compared with their first parent.

If a push contains several batches, CD selects the newest valid one. It scans,
builds and deploys that exact commit; later changes in the push are excluded.
All three CodeQL scans must succeed before image or documentation publication.
Production deployment uses the SHA-tagged image and announces the selected
batch after success. Documentation deployment runs independently after scanning.

Deployment runs queue (up to 100 pending runs) without cancelling earlier runs.
GitHub orders them by when they enter the queue, not by push time. Rerunning a failed run
keeps its original push range and batch SHA; it does not pick up the latest
master. A rerun can redeploy that older release, and rerunning a successful
announcement can post it again. There is no manual arbitrary-image route.

Verify release tooling locally with:

```sh
python -m unittest discover -s scripts/tests
actionlint
```

Actionlint 1.7.12 does not yet recognise GitHub's documented `queue: max`
property; its syntax warning for that property is a known tooling limitation.
