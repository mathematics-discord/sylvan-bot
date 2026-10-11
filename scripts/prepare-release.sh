#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: scripts/prepare-release.sh <major.minor.patch>" >&2
  exit 2
fi

version="$1"
python_command=python3
if ! command -v python3 >/dev/null 2>&1; then
  python_command=python
fi

# The application version is the baseline even when Changie has no releases.
"$python_command" - "$version" <<'PY'
import json
import re
import sys
from pathlib import Path

requested = sys.argv[1]
current = json.loads(Path("version.json").read_text(encoding="utf-8"))["version"]
pattern = re.compile(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\Z")
if not pattern.fullmatch(requested):
    sys.exit("Release version must be an explicit major.minor.patch (without v).")
if not pattern.fullmatch(current):
    sys.exit(f"Cannot compare release version with version.json value {current!r}.")
if tuple(map(int, requested.split("."))) <= tuple(map(int, current.split("."))):
    sys.exit(f"Release version {requested} must be newer than version.json ({current}).")
if Path(f".changes/v{requested}.md").exists():
    sys.exit(f"Release notes for v{requested} already exist.")
PY

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
