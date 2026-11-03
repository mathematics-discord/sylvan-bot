#!/usr/bin/env python3
"""Select the newest valid batch on a push's first-parent history."""

import json
import os
import re
import subprocess
import sys
from pathlib import Path

from release_batch import git, release_from_commits


def detect_release(before, after):
    for commit in (before, after):
        if not re.fullmatch(r"[0-9a-f]{40}", commit) or commit == "0" * 40:
            raise ValueError("Push must have two usable commit SHAs.")
        git("cat-file", "-e", f"{commit}^{{commit}}")

    # Require the old head on this first-parent chain, not merely somewhere
    # in merged history. Otherwise the push range cannot be interpreted safely.
    history = git("rev-list", "--first-parent", after).splitlines()
    if before not in history:
        raise ValueError("Before commit is not on the pushed first-parent history.")

    selected = {"has_release": "false"}
    for commit in reversed(history[:history.index(before)]):
        parent = git("rev-parse", f"{commit}^1").strip()
        old = json.loads(git("show", f"{parent}:version.json"))["version"]
        new = json.loads(git("show", f"{commit}:version.json"))["version"]
        added = git("diff", "--no-renames", "--name-only", "--diff-filter=A",
                    parent, commit, "--", ".changes").splitlines()
        notes = [path for path in added if re.fullmatch(r"\.changes/v[^/]+\.md", path)]
        if old == new and not notes:
            continue

        release, reason = release_from_commits(parent, commit)
        if release is None:
            raise ValueError(f"Invalid release batch at {commit}: {reason}")
        version, _ = release
        if notes != [f".changes/v{version}.md"]:
            raise ValueError(f"Release batch at {commit} adds mismatched release notes.")
        selected = {"has_release": "true", "version": version,
                    "image_tag": "v" + version.replace("+", "_"),
                    "source_sha": commit, "before_sha": parent}
    return selected


def main():
    result = detect_release(os.environ.get("RELEASE_BEFORE", ""),
                            os.environ.get("RELEASE_AFTER", ""))
    if path := os.environ.get("GITHUB_OUTPUT"):
        with Path(path).open("a", encoding="utf-8") as output:
            for key, value in result.items():
                output.write(f"{key}={value}\n")
    print(json.dumps(result))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        print(f"Release detection failed: {error}", file=sys.stderr)
        sys.exit(1)
