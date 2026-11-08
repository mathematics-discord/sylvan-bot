#!/usr/bin/env bash
# Installed root-owned at /usr/local/sbin/sylvan-docs-deploy on the VPS.
set -euo pipefail

curl_options=()
case "${SSH_ORIGINAL_COMMAND:-}" in
  deploy)
    site=/var/www/math/sylvan
    verify_url=https://math.ratgirl.dev/sylvan/
    ;;
  deploy-root)
    site=/var/www/sylvan
    verify_url=http://sylvan.mathematics.gg/
    # Verify locally while DNS/TLS is pending, and after HTTP redirects to TLS.
    curl_options=(--noproxy '*' --location
      --resolve sylvan.mathematics.gg:80:127.0.0.1
      --resolve sylvan.mathematics.gg:443:127.0.0.1)
    ;;
  *)
    echo 'Only docs deployment is permitted.' >&2
    exit 1
    ;;
esac
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

if [[ "${SSH_ORIGINAL_COMMAND}" == deploy-root ]]; then
  # Reject a legacy-base archive before it can replace the root site.
  python3 - "$release" <<'PY'
import sys
from html.parser import HTMLParser
from pathlib import Path

class Canonical(HTMLParser):
    urls = []
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'link' and attrs.get('rel') == 'canonical':
            self.urls.append(attrs.get('href'))

release = Path(sys.argv[1])
parser = Canonical()
parser.feed((release / 'index.html').read_text())
if parser.urls != ['https://sylvan.mathematics.gg/']:
    sys.exit('Root docs require the sylvan.mathematics.gg canonical URL.')
for path in release.rglob('*'):
    if path.is_file() and path.suffix in {'.html', '.css', '.js', '.xml', '.json'}:
        text = path.read_text(errors='replace')
        if '/sylvan/' in text or 'math.ratgirl.dev' in text:
            sys.exit(f'Legacy docs URL found in {path.relative_to(release)}')
PY
fi

previous=$(readlink "$site/current")
ln -s "releases/$(basename "$release")" "$site/current.next"
mv -Tf "$site/current.next" "$site/current"

# Check the actual published bytes; keep the previous release for rollback.
if ! curl --fail --silent --show-error --max-time 30 \
    "${curl_options[@]}" "$verify_url" | cmp -s "$release/index.html" -; then
  ln -s "$previous" "$site/current.next"
  mv -Tf "$site/current.next" "$site/current"
  echo 'Docs verification failed; restored the previous release.' >&2
  exit 1
fi

echo "Published $(basename "$release") to $verify_url"
