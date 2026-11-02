#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: scripts/prepare-release.sh <major|minor|patch|custom> [major.minor.patch[-alpha.N|-beta.N|-rc.N][+metadata]]" >&2
  exit 2
fi

python_command=python3
if ! command -v python3 >/dev/null 2>&1; then
  python_command=python
fi

# The application version is the baseline even when Changie has no releases.
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
version="$("$python_command" "$script_dir/release_version.py" "$1" "${2:-}")"

changie batch "$version" --allow-no-changes=false
changie merge --include-unreleased "## Unreleased"

"$python_command" - "$version" <<'PY'
import json
import sys
from pathlib import Path

path = Path("version.json")
data = json.loads(path.read_text(encoding="utf-8"))
data["version"] = sys.argv[1]
path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
PY

echo "Prepared v$version locally. Review .changes/, CHANGELOG.md and version.json before committing."
