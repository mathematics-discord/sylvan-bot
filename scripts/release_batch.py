"""Read and validate committed Changie release batches."""

import json
import re
import subprocess

from release_version import version_key


COMPONENT_NAMES = ("General", "Administration", "Moderation", "Internal")
KIND_NAMES = ("Security", "Added", "Changed", "Fixed", "Deprecated", "Removed")


def git(*args):
    return subprocess.check_output(["git", *args], stderr=subprocess.DEVNULL).decode("utf-8")


def release_from_commits(before, after):
    if not re.fullmatch(r"[0-9a-f]{40}", before) or before == "0" * 40:
        return None, "Release has no usable before commit."
    if not re.fullmatch(r"[0-9a-f]{40}", after):
        raise ValueError("Release has no valid after commit.")
    if subprocess.run(["git", "cat-file", "-e", f"{before}^{{commit}}"],
                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
        return None, "Before commit is unavailable; release status cannot be verified."
    if subprocess.run(["git", "merge-base", "--is-ancestor", before, after],
                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
        return None, "Before commit is not an ancestor of the deployed commit."

    try:
        old = json.loads(git("show", f"{before}:version.json"))["version"]
        new = json.loads(git("show", f"{after}:version.json"))["version"]
    except (subprocess.CalledProcessError, KeyError, json.JSONDecodeError):
        return None, "Version metadata is unavailable in the release range."
    if version_key(new) <= version_key(old):
        return None, "version.json did not advance."

    note_path = f".changes/v{new}.md"
    added = git("diff", "--no-renames", "--name-status", "--diff-filter=A", before, after, "--", note_path)
    if added.strip() != f"A\t{note_path}":
        return None, f"Release notes for v{new} were not newly added in this release."
    notes = git("show", f"{after}:{note_path}")
    if not notes.strip():
        raise ValueError(f"Committed release notes for v{new} are empty.")
    sections = parse_sections(new, notes)
    if not any(entry[2:].strip() for section in sections for kind in section["kinds"]
               for entry in kind["entries"]):
        raise ValueError(f"Committed release notes for v{new} have no entries.")
    return (new, notes), ""


def parse_sections(version, notes):
    lines = notes.replace("\r\n", "\n").splitlines()
    first = lines[0].lstrip("\ufeff") if lines else ""
    heading = re.fullmatch(rf"(#{{1,2}}) v{re.escape(version)}(?: - \d{{4}}-\d{{2}}-\d{{2}})?", first)
    if not heading:
        raise ValueError("Committed release notes have no matching release heading.")

    component_level = len(heading.group(1)) + 1
    kind_level = component_level + 1
    sections = []
    section = None
    kind = None
    entry_lines = []
    embedded_list = False

    def finish_entry():
        nonlocal entry_lines, embedded_list
        while entry_lines and not entry_lines[-1].strip():
            entry_lines.pop()
        if entry_lines:
            kind["entries"].append("\n".join(entry_lines))
        entry_lines = []
        embedded_list = False

    for line in lines[1:]:
        if line in (f"{'#' * component_level} {name}" for name in COMPONENT_NAMES):
            finish_entry()
            section = {"name": line[component_level + 1:], "kinds": []}
            sections.append(section)
            kind = None
        elif line in (f"{'#' * kind_level} {name}" for name in KIND_NAMES):
            finish_entry()
            if section is None:
                raise ValueError("Release kind appears before a component.")
            kind = {"name": line[kind_level + 1:], "entries": []}
            section["kinds"].append(kind)
        elif line.startswith("- "):
            if (entry_lines and entry_lines[-1].strip()
                    and (embedded_list or (len(entry_lines) > 1 and not entry_lines[-1].startswith("- ")))):
                entry_lines.append(line)
                embedded_list = True
                continue
            finish_entry()
            if kind is None:
                raise ValueError("Release entry appears before a kind.")
            entry_lines = [line]
        elif entry_lines:
            entry_lines.append(line)
        elif line.strip():
            raise ValueError("Committed release notes contain text outside a component.")

    finish_entry()
    for section in sections:
        section["kinds"] = [kind for kind in section["kinds"] if kind["entries"]]
    return [section for section in sections if section["kinds"]]
