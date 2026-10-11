#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: scripts/prepare-release.sh <major|minor|patch|custom> [major.minor.patch]" >&2
  exit 2
fi

python_command=python3
if ! command -v python3 >/dev/null 2>&1; then
  python_command=python
fi

# The application version is the baseline even when Changie has no releases.
version="$("$python_command" - "$1" "${2:-}" <<'PY'
import json
import re
import sys
from pathlib import Path

release_type, explicit = sys.argv[1:]
current = json.loads(Path("version.json").read_text(encoding="utf-8"))["version"]
pattern = re.compile(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\Z")
if not pattern.fullmatch(current):
    sys.exit(f"Cannot compare release version with version.json value {current!r}.")
if release_type == "custom":
    if not pattern.fullmatch(explicit):
        sys.exit("Custom release version must be major.minor.patch (without v).")
    requested = explicit
elif release_type in ("major", "minor", "patch"):
    if explicit:
        sys.exit("An explicit version is only valid with the custom release type.")
    major, minor, patch = map(int, current.split("."))
    if release_type == "major":
        requested = f"{major + 1}.0.0"
    elif release_type == "minor":
        requested = f"{major}.{minor + 1}.0"
    else:
        requested = f"{major}.{minor}.{patch + 1}"
else:
    sys.exit("Release type must be major, minor, patch, or custom.")
if tuple(map(int, requested.split("."))) <= tuple(map(int, current.split("."))):
    sys.exit(f"Release version {requested} must be newer than version.json ({current}).")
if Path(f".changes/v{requested}.md").exists():
    sys.exit(f"Release notes for v{requested} already exist.")
print(requested)
PY
)"

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
