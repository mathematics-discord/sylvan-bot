# GitHub Actions Deploy Notes

## Workflows

- `.github/workflows/ci-tests.yml`
    - Runs restore, build, and tests on `ubuntu-latest`
    - Uses the runner Docker daemon for Testcontainers-based integration tests
    - Triggered on pull requests targeting `master`, pushes to `master`, and manual dispatch

- `.github/workflows/deploy-vps.yml`
    - Builds and pushes the bot container image from `Dockerfile` after `CI Tests` succeeds on `master`
    - Publishes tags:
        - immutable: `sha-<full_commit_sha>`
        - branch: `master`
    - Auto deploys `production` with the same immutable `sha-<full_commit_sha>` image
    - Manual dispatch supports:
        - `shared`
        - `production`
    - Uses explicit Compose project names:
        - `ratbot-shared`
        - `ratbot-production`
    - Prevents overlap with per-target concurrency groups
    - Runs automatic production deploy jobs independently, so one failed target does not cancel the other

## PR Merge Protection

To prevent merges to `master` when tests fail, configure a branch protection rule or repository ruleset in GitHub:

- Target branch: `master`
- Require status checks to pass before merging
- Required check: `Build and test` from `.github/workflows/ci-tests.yml`
- Require branches to be up to date before merging, if you want every PR tested against the latest `master`

## Required GitHub Secrets and Variables

Recommended names:

- Secret: `VPS_SSH_PRIVATE_KEY`
    - Private key for CI/CD SSH access
    - Key should match `/home/deploy/.ssh/authorized_keys` on VPS
- Secret: `VPS_SSH_KNOWN_HOSTS` (recommended)
    - Output of `ssh-keyscan -H <host>` for strict host key pinning
- Secret: `VPS_HOST`
    - VPS hostname or IP
- Secret: `VPS_PORT` (optional)
    - SSH port, defaults to `22` when empty
- Secret: `VPS_USER` (optional)
    - Defaults to `deploy` when empty

You can store these at repository scope, or per GitHub Environment (`shared`, `production`) for stronger
isolation/approval controls.

If the GitHub `production` environment has required reviewers, automatic production deployment from `master` will pause
until that protection rule is changed or approved.

## VPS-side Prerequisites

- Directories must already exist on the VPS:
    - `/opt/ratbot/shared`
    - `/opt/ratbot/production`
- The deploy helper syncs non-secret compose/config assets from the repository to the VPS before running Docker Compose.
- The workflow still does not create or upload real `.env` files.
- Each stack directory must therefore already contain a valid server-side `.env` file before deployment can succeed.
- `deploy` user must:
    - Be allowed to SSH
    - Be able to run Docker/Compose (typically via `docker` group)
- Docker Compose plugin must be installed on the VPS.

## GHCR Pull Authentication Model

The deploy workflow does not send GHCR credentials from GitHub Actions to the VPS.  
Expected model: the VPS `deploy` user is already authenticated for pulls.

One-time setup on VPS (as `deploy`):

```bash
docker login ghcr.io
```

This stores credentials for future `docker compose pull`.

For private images, use a GitHub token/PAT with `read:packages` scope for that login.

## Manual Deploy Usage

Use **Actions -> Deploy RatBot to VPS -> Run workflow**:

- `target=shared`: deploy shared stack only
- `target=production`: deploy production bot stack
    - Requires `image_tag`
- `image_tag` for production:
    - Tag only: `sha-<commit_sha>`
    - Or full image ref: `ghcr.io/<owner>/<repo>:sha-<commit_sha>`

Automatic production flow:

- Let `CI Tests` pass on a `master` push
- The deployment workflow builds and publishes the immutable `sha-<commit_sha>` image
- The deployment workflow applies that exact image to production

For bot stacks, the deployment helper also writes the deployed `RATBOT_IMAGE` back to the server-side `.env` so later
manual
`docker compose` runs do not drift to an older moving tag.

The deployment helper writes a simple rollback state on the VPS per stack:

- `.deploy-state/current-image.txt`
- `.deploy-state/previous-image.txt`
