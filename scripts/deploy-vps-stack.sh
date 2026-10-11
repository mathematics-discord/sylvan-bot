#!/usr/bin/env bash
set -euo pipefail

project=""
remote_dir=""
image_ref=""

usage() {
  cat <<'EOF'
Usage:
  deploy-vps-stack.sh --project <name> --remote-dir <path> [--image-ref <ref>]

Behavior:
  - Syncs non-secret compose/config assets from the repository to the VPS
  - Leaves server-side .env files in place
  - Persists the deployed RATBOT_IMAGE back to the server-side .env for bot stacks
  - Runs docker compose pull/up on the remote host
EOF
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
local_compose="$repo_root/docker-compose.production.yml"
local_env_example="$repo_root/env/production.env.example"

upload_file() {
  local src="$1"
  local dest="$2"

  if [[ ! -f "$src" ]]; then
    echo "Local file not found: $src" >&2
    exit 1
  fi

  ssh "${ssh_opts[@]}" "${vps_user}@${VPS_HOST}" "cat > '$dest'" < "$src"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project)
      project="${2:-}"
      shift 2
      ;;
    --remote-dir)
      remote_dir="${2:-}"
      shift 2
      ;;
    --image-ref)
      image_ref="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$project" || -z "$remote_dir" ]]; then
  echo "Missing required arguments." >&2
  usage
  exit 1
fi

if [[ ! -f "$local_compose" ]]; then
  echo "Local compose file not found: $local_compose" >&2
  exit 1
fi

if [[ ! -f "$local_env_example" ]]; then
  echo "Local env example file not found: $local_env_example" >&2
  exit 1
fi

: "${VPS_HOST:?VPS_HOST is required}"
: "${VPS_SSH_PRIVATE_KEY:?VPS_SSH_PRIVATE_KEY is required}"

vps_port="${VPS_PORT:-22}"
vps_user="${VPS_USER:-deploy}"

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

key_file="$tmp_dir/id_key"
known_hosts_file="$tmp_dir/known_hosts"

printf '%s\n' "$VPS_SSH_PRIVATE_KEY" > "$key_file"
chmod 600 "$key_file"

if [[ -n "${VPS_SSH_KNOWN_HOSTS:-}" ]]; then
  printf '%s\n' "$VPS_SSH_KNOWN_HOSTS" > "$known_hosts_file"
else
  ssh-keyscan -H -p "$vps_port" "$VPS_HOST" > "$known_hosts_file" 2>/dev/null
fi
chmod 600 "$known_hosts_file"

ssh_opts=(
  -i "$key_file"
  -o IdentitiesOnly=yes
  -o StrictHostKeyChecking=yes
  -o UserKnownHostsFile="$known_hosts_file"
  -p "$vps_port"
)

image_ref_remote="$image_ref"

remote_tmp_dir="${remote_dir}/.sync-tmp"

ssh "${ssh_opts[@]}" "${vps_user}@${VPS_HOST}" \
  "REMOTE_DIR='$remote_dir' REMOTE_TMP_DIR='$remote_tmp_dir' bash -s" <<'REMOTE_PREP_EOF'
set -euo pipefail
mkdir -p "$REMOTE_DIR" "$REMOTE_TMP_DIR"
REMOTE_PREP_EOF

upload_file "$local_compose" "${remote_tmp_dir}/compose.yaml"
upload_file "$local_env_example" "${remote_tmp_dir}/.env.example"

ssh "${ssh_opts[@]}" "${vps_user}@${VPS_HOST}" \
  "PROJECT='$project' REMOTE_DIR='$remote_dir' IMAGE_REF='$image_ref_remote' bash -s" <<'REMOTE_EOF'
set -euo pipefail

if [[ ! -d "$REMOTE_DIR" ]]; then
  echo "Remote directory does not exist: $REMOTE_DIR" >&2
  exit 1
fi

cd "$REMOTE_DIR"

mv .sync-tmp/compose.yaml ./compose.yaml
mv .sync-tmp/.env.example ./.env.example
rmdir .sync-tmp 2>/dev/null || true

if ! command -v docker >/dev/null 2>&1; then
  echo "docker command not found on remote host." >&2
  exit 1
fi

compose_args=(-p "$PROJECT" -f compose.yaml)
if [[ -f .env ]]; then
  compose_args+=(--env-file .env)
elif [[ -f .env.example ]]; then
  echo "Missing required server-side .env in $REMOTE_DIR. A .env.example is present as a template." >&2
  exit 1
else
  echo "Missing required server-side .env in $REMOTE_DIR." >&2
  exit 1
fi

if [[ -n "${IMAGE_REF}" ]]; then
  export RATBOT_IMAGE="$IMAGE_REF"
fi

docker compose "${compose_args[@]}" config >/dev/null
docker compose "${compose_args[@]}" pull
docker compose "${compose_args[@]}" up -d --remove-orphans

if [[ -n "${IMAGE_REF}" ]]; then
  tmp_env_file=".env.tmp"
  awk -v image_ref="$IMAGE_REF" '
    BEGIN { updated = 0 }
    /^RATBOT_IMAGE=/ {
      print "RATBOT_IMAGE=" image_ref
      updated = 1
      next
    }
    { print }
    END {
      if (!updated) {
        print "RATBOT_IMAGE=" image_ref
      }
    }
  ' .env > "$tmp_env_file"
  mv "$tmp_env_file" .env

  state_dir=".deploy-state"
  mkdir -p "$state_dir"

  if [[ -f "$state_dir/current-image.txt" ]]; then
    cp "$state_dir/current-image.txt" "$state_dir/previous-image.txt"
  fi

  printf '%s\n' "$IMAGE_REF" > "$state_dir/current-image.txt"
fi

docker compose "${compose_args[@]}" ps
REMOTE_EOF
