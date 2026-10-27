#!/usr/bin/env bash
# Installed root-owned at /usr/local/sbin/sylvan-docs-deploy on the VPS.
set -euo pipefail

if [[ "${SSH_ORIGINAL_COMMAND:-}" != deploy ]]; then
  echo 'Only docs deployment is permitted.' >&2
  exit 1
fi

site=/var/www/math/sylvan
exec 9>"$site/.deploy.lock"
flock -w 60 9
release=$(mktemp -d "$site/releases/deploy-XXXXXXXX")
umask 022

# Python's data filter rejects archive paths and links outside the release.
python3 -c '
import sys, tarfile
with tarfile.open(fileobj=sys.stdin.buffer, mode="r|gz") as archive:
    archive.extractall(sys.argv[1], filter="data")
' "$release"
chmod 755 "$release"
test -s "$release/index.html"
test -s "$release/pagefind/pagefind-entry.json"

previous=$(readlink "$site/current")
ln -s "releases/$(basename "$release")" "$site/current.next"
mv -Tf "$site/current.next" "$site/current"

# Check the actual published bytes; keep the previous release for rollback.
if ! curl --fail --silent --show-error --max-time 30 \
    https://math.ratgirl.dev/sylvan/ | cmp -s "$release/index.html" -; then
  ln -s "$previous" "$site/current.next"
  mv -Tf "$site/current.next" "$site/current"
  echo 'Docs verification failed; restored the previous release.' >&2
  exit 1
fi

echo "Published $(basename "$release") to https://math.ratgirl.dev/sylvan/"
