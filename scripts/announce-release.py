#!/usr/bin/env python3
"""Announce committed release notes after a successful manual production deployment."""

import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit
from urllib.request import Request, urlopen


SEMVER = r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"
VERSION = re.compile(rf"{SEMVER}\Z")
USER_AGENT = "DiscordBot (https://github.com/ratgirl-laboratories/sylvan-bot, 1.0)"
COMPONENT_NAMES = ("General", "Administration", "Moderation", "Internal")
KIND_NAMES = ("Security", "Added", "Changed", "Fixed", "Deprecated", "Removed")
MAX_TEXT_UNITS = 4000
MAX_COMPONENTS = 40


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
    if not VERSION.fullmatch(old) or not VERSION.fullmatch(new):
        raise ValueError("version.json contains a non-release version.")
    if tuple(map(int, new.split("."))) <= tuple(map(int, old.split("."))):
        return None, "version.json did not advance."

    note_path = f".changes/v{new}.md"
    added = git("diff", "--name-status", "--diff-filter=A", before, after, "--", note_path)
    if added.strip() != f"A\t{note_path}":
        return None, f"Release notes for v{new} were not newly added in this release."
    notes = git("show", f"{after}:{note_path}")
    if not notes.strip():
        raise ValueError(f"Committed release notes for v{new} are empty.")
    return (new, notes), ""


def length(text):
    # Discord's character count is safest measured as UTF-16 code units.
    return len(text.encode("utf-16-le")) // 2


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


def section_text(section):
    lines = [f"## {section['name']}"]
    for kind in section["kinds"]:
        lines.append(f"### {kind['name']}")
        lines.extend(kind["entries"])
    return "\n".join(lines)


def take_prefix(text, budget):
    used = 0
    end = 0
    for character in text:
        size = length(character)
        if used + size > budget:
            break
        used += size
        end += 1
    if not end:
        raise ValueError("Release entry cannot fit in a Discord Text Display.")
    if end < len(text):
        boundary = max(text.rfind("\n", 0, end) + 1, text.rfind(" ", 0, end) + 1)
        if boundary and boundary >= end // 2:
            end = boundary
    return text[:end], text[end:]


def section_chunks(section, limit):
    header = f"## {section['name']}"
    chunks = []
    current = header
    last_kind = None
    for kind in section["kinds"]:
        for entry in kind["entries"]:
            addition = (f"\n### {kind['name']}" if last_kind != kind["name"] else "") + f"\n{entry}"
            if length(current + addition) <= limit:
                current += addition
                last_kind = kind["name"]
                continue
            if current != header:
                chunks.append(current)
            current = f"{header}\n### {kind['name']}"
            last_kind = kind["name"]
            if length(current + f"\n{entry}") <= limit:
                current += f"\n{entry}"
                continue

            # Only an individual entry too large for a fresh section is divided.
            body = entry[2:]
            while True:
                budget = limit - length(current + "\n- ")
                piece, body = take_prefix(body, budget) if body else ("", "")
                current += "\n- " + piece
                if not body:
                    break
                chunks.append(current)
                current = f"{header}\n### {kind['name']}"
    if current != header:
        chunks.append(current)
    return chunks


def container_message(version, chunks, part=None, total=None):
    title = f"# v{version}" if part is None else f"# v{version} (part {part}/{total})"
    children = [{"type": 10, "content": title}]
    for chunk in chunks:
        children.append({"type": 14, "divider": False, "spacing": 1})
        children.append({"type": 10, "content": chunk})
    return {"flags": 32768, "components": [{"type": 17, "components": children}],
            "allowed_mentions": {"parse": []}}


def build_messages(version, notes, notes_url):
    sections = parse_sections(version, notes)
    has_internal = any(section["name"] == "Internal" for section in sections)
    sections = [section for section in sections if section["name"] != "Internal"]
    closing_chunks = []
    if has_internal and not sections:
        closing_chunks.append("Maintenance release; no user-facing changes.")
    if has_internal:
        closing_chunks.append(f"-# Internal changes are listed in the [full release notes]({notes_url}).")
    chunks = [section_text(section) for section in sections] + closing_chunks
    title = f"# v{version}"
    if (length(title) + sum(map(length, chunks)) <= MAX_TEXT_UNITS
            and 2 + 2 * len(chunks) <= MAX_COMPONENTS):
        return [container_message(version, chunks)]

    # A part label must fit even before the final number of messages is known.
    digits = len(str(max(1, length(notes))))
    largest_title = f"# v{version} (part {'9' * digits}/{'9' * digits})"
    limit = MAX_TEXT_UNITS - length(largest_title)
    if limit <= length("## General\n### Changed\n- "):
        raise ValueError("Release version leaves no room for Discord components.")

    chunks = [chunk for section in sections for chunk in section_chunks(section, limit)]
    if any(length(chunk) > limit for chunk in closing_chunks):
        raise ValueError("Release notes summary cannot fit in a Discord Text Display.")
    chunks.extend(closing_chunks)
    groups = []
    current = []
    used = length(largest_title)
    for chunk in chunks:
        if current and (used + length(chunk) > MAX_TEXT_UNITS
                        or 2 + 2 * (len(current) + 1) > MAX_COMPONENTS):
            groups.append(current)
            current = []
            used = length(largest_title)
        current.append(chunk)
        used += length(chunk)
    if current:
        groups.append(current)
    return [container_message(version, group, number, len(groups))
            for number, group in enumerate(groups, 1)]


def webhook_with_options(url):
    parsed = urlsplit(url)
    query = [(key, value) for key, value in parse_qsl(parsed.query, keep_blank_values=True)
             if key not in ("wait", "with_components")]
    query.extend((("wait", "true"), ("with_components", "true")))
    return urlunsplit(parsed._replace(query=urlencode(query)))


def post(url, messages):
    try:
        endpoint = webhook_with_options(url)
    except ValueError:
        raise RuntimeError("Invalid Discord webhook URL configuration.") from None
    for number, message in enumerate(messages, 1):
        payload = json.dumps(message, ensure_ascii=False).encode("utf-8")
        for attempt in range(4):
            request = Request(endpoint, payload,
                              {"Content-Type": "application/json; charset=utf-8",
                               "User-Agent": USER_AGENT}, method="POST")
            try:
                with urlopen(request, timeout=20) as response:
                    receipt = json.load(response)
                    if response.status != 200 or not isinstance(receipt, dict) or not receipt.get("id"):
                        raise RuntimeError(f"Discord did not confirm message {number}/{len(messages)}.")
                    break
            except HTTPError as error:
                body = error.read(4096)
                try:
                    details = json.loads(body)
                except (ValueError, UnicodeDecodeError):
                    details = {}
                code = details.get("code") if isinstance(details, dict) else None
                if error.code != 429:
                    suffix = f", Discord code {code}" if isinstance(code, int) else ""
                    raise RuntimeError(
                        f"Discord rejected message {number}/{len(messages)}: HTTP {error.code}{suffix}."
                    ) from None
                try:
                    delay = float(details.get("retry_after", error.headers.get("Retry-After")))
                except (TypeError, ValueError):
                    raise RuntimeError("Discord rate limit had no usable retry delay.") from None
                if attempt == 3 or not 0 <= delay <= 60:
                    raise RuntimeError(
                        f"Discord rate limit exceeded retry budget at message {number}/{len(messages)}."
                    ) from None
                time.sleep(delay)
            except URLError as error:
                raise RuntimeError(
                    f"Network failure posting message {number}/{len(messages)} ({type(error.reason).__name__})."
                ) from None
            except RuntimeError:
                raise
            except Exception as error:
                raise RuntimeError(
                    f"Webhook request failed at message {number}/{len(messages)} ({type(error).__name__})."
                ) from None


def summary(text):
    print(text)
    if path := os.environ.get("GITHUB_STEP_SUMMARY"):
        with Path(path).open("a", encoding="utf-8") as file:
            file.write(f"### Discord release announcement\n{text}\n")


def main():
    if (os.environ.get("GITHUB_EVENT_NAME") != "workflow_dispatch"
            or os.environ.get("GITHUB_REF") != "refs/heads/master"
            or os.environ.get("DEPLOY_RESULT") != "success"):
        summary("Skipped: this was not a successful manual production deployment.")
        return

    release, reason = release_from_commits(os.environ.get("RELEASE_BEFORE", ""),
                                           os.environ.get("RELEASE_AFTER", ""))
    if release is None:
        summary(f"Skipped: {reason}")
        return

    version, notes = release
    webhook = os.environ.get("DISCORD_RELEASE_WEBHOOK_URL", "")
    if not webhook:
        summary(f"Skipped v{version}: DISCORD_RELEASE_WEBHOOK_URL Actions secret is absent.")
        return

    repository = os.environ.get("GITHUB_REPOSITORY", "ratgirl-laboratories/sylvan-bot")
    commit = os.environ["RELEASE_AFTER"]
    notes_url = f"https://github.com/{repository}/blob/{commit}/.changes/v{version}.md"
    messages = build_messages(version, notes, notes_url)
    post(webhook, messages)
    summary(f"Posted v{version} to Discord in {len(messages)} message(s).")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        # These diagnostics contain no webhook URL, token, or response body.
        print(f"Release announcement failed: {error}", file=sys.stderr)
        sys.exit(1)
