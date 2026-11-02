"""Version validation shared by release preparation and announcements."""

import json
import re
import sys
from pathlib import Path


NUMBER = r"(?:0|[1-9][0-9]*)"
VERSION = re.compile(
    rf"({NUMBER})\.({NUMBER})\.({NUMBER})(?:-(alpha|beta|rc)\.({NUMBER}))?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\Z")


def version_key(version):
    match = VERSION.fullmatch(version)
    if not match:
        raise ValueError(
            f"Invalid release version {version!r}: expected major.minor.patch "
            "with optional -alpha.N, -beta.N or -rc.N and +build.metadata "
            "(without v or leading zeroes in version numbers).")
    major, minor, patch, channel, number = match.groups()
    # Build metadata does not affect precedence; stable follows its prereleases.
    return (int(major), int(minor), int(patch),
            {"alpha": 0, "beta": 1, "rc": 2, None: 3}[channel], int(number or 0))


def next_version(current, release_type, explicit=""):
    current_key = version_key(current)
    if release_type == "custom":
        requested = explicit
    elif release_type in ("major", "minor", "patch"):
        if explicit:
            raise ValueError("An explicit version is only valid with the custom release type.")
        major, minor, patch = current_key[:3]
        if release_type == "major":
            requested = f"{major + 1}.0.0"
        elif release_type == "minor":
            requested = f"{major}.{minor + 1}.0"
        else:
            requested = f"{major}.{minor}.{patch + 1}"
    else:
        raise ValueError("Release type must be major, minor, patch, or custom.")
    if version_key(requested) <= current_key:
        raise ValueError(f"Release version {requested} must be newer than version.json ({current}).")
    return requested


if __name__ == "__main__":
    current = json.loads(Path("version.json").read_text(encoding="utf-8"))["version"]
    try:
        requested = next_version(current, *sys.argv[1:])
        if Path(f".changes/v{requested}.md").exists():
            raise ValueError(f"Release notes for v{requested} already exist.")
    except ValueError as error:
        sys.exit(str(error))
    print(requested)
