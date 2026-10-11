# GitHub Actions Deploy Notes

## Production workflow

`.github/workflows/deploy-vps.yml` is manually dispatched from GitHub Actions. It serializes deployments with the `deploy-ratbot` concurrency group.

- `target=production`: dispatch from `master`, choose a release type (or a custom version), batch the Changie release, and build/publish the bot image. Tags include `sha-<commit>`, `v<version>`, and `master`. Deploy the immutable SHA image, then announce the successful release.
- `target=production-image`: supply an existing image tag or full image reference to deploy that image without creating a release.

The helper uses Compose project `production` in `/opt/ratbot/production`. It uploads `compose.yaml` and `.env.example`, preserves the server-side `.env`, and runs Compose pull/up. PostgreSQL and the bot are the only services; no shared telemetry stack is required.

## Required GitHub secrets

- `VPS_HOST`: SSH hostname or address.
- `VPS_PORT`: optional SSH port, default `22`.
- `VPS_USER`: optional SSH user, default `deploy`.
- `VPS_SSH_PRIVATE_KEY`: deployment SSH key.
- `VPS_SSH_KNOWN_HOSTS`: pinned SSH host keys, recommended.
- `DISCORD_RELEASE_WEBHOOK_URL`: webhook used to announce a successful production release.

Production deployment jobs use the GitHub `production` environment and its configured protection rules. GitHub supplies `GITHUB_TOKEN` for repository/package operations.

## VPS setup and persistent logs

The deployment user needs SSH access, Docker/Compose access, and write access to `/opt/ratbot/production`. Create its server-side `.env` from `env/production.env.example` with real credentials; the workflow does not upload secrets.

Authenticate the deployment user to GHCR using `docker login ghcr.io` and a token with `read:packages` if the image is private. The workflow does not send registry credentials to the VPS.

The bot mounts `/opt/ratbot/production/logs` into `/app/logs`. Daily logfiles survive container replacement. Keep this directory writable by the container process. When adding the mount to an existing installation, stop only the bot and copy its `/app/logs` contents out before recreating it, then verify new entries appear in the host directory.

```bash
cd /opt/ratbot/production
docker compose -p production -f compose.yaml --env-file .env ps
tail -F logs/info-$(date +%Y%m%d).log
```

PostgreSQL data remains in `ratbot_production_postgres_data`.

## Image tracking and rollback

After deployment, the helper persists `RATBOT_IMAGE` in the server-side `.env` and records:

- `.deploy-state/current-image.txt`
- `.deploy-state/previous-image.txt`

Use `target=production-image` with the desired existing immutable image reference for a rollback. Retain the current Compose log mount when rolling back an image.
