#!/usr/bin/env python3
"""Announce committed release notes after a successful production push deployment."""

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
# .changie.yaml: changesDir=.changes, versionFileFormat='v{{.VersionNoPrefix}}.md'.
PUBLISHED_BATCH = re.compile(rf"\.changes/v{SEMVER}\.md\Z")
USER_AGENT = "DiscordBot (https://github.com/ratgirl-laboratories/sylvan-bot, 1.0)"


def git(*args):
    return subprocess.check_output(["git", *args], stderr=subprocess.DEVNULL).decode("utf-8")


def release_from_push(before, after):
    if not re.fullmatch(r"[0-9a-f]{40}", before) or before == "0" * 40:
        return None, "Push has no usable before commit."
    if not re.fullmatch(r"[0-9a-f]{40}", after):
        raise ValueError("Push has no valid after commit.")
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
        return None, "Version metadata is unavailable in the push range."
    if not VERSION.fullmatch(old) or not VERSION.fullmatch(new):
        raise ValueError("version.json contains a non-release version.")
    if tuple(map(int, new.split("."))) <= tuple(map(int, old.split("."))):
        before_files = git("ls-tree", "-r", "--name-only", before, "--", ".changes")
        if any(PUBLISHED_BATCH.fullmatch(path) for path in before_files.splitlines()):
            return None, "version.json did not advance."

    note_path = f".changes/v{new}.md"
    added = git("diff", "--name-status", "--diff-filter=A", before, after, "--", note_path)
    if added.strip() != f"A\t{note_path}":
        return None, f"Release notes for v{new} were not newly added in this push."
    notes = git("show", f"{after}:{note_path}")
    if not notes.strip():
        raise ValueError(f"Committed release notes for v{new} are empty.")
    return (new, notes), ""


def length(text):
    # Discord's character count is safest measured as UTF-16 code units.
    return len(text.encode("utf-16-le")) // 2


def split_notes(version, notes):
    title = f"**RatBot v{version}**\n\n"
    if length(title + notes) <= 2000:
        return [title + notes]

    # Reserve room for the longest possible part label before splitting.
    digits = len(str(len(notes)))
    largest_title = f"**RatBot v{version} (part {'9' * digits}/{'9' * digits})**\n\n"
    budget = 2000 - length(largest_title)
    if budget <= 0:
        raise ValueError("Release version leaves no room for Discord message content.")
    parts = []
    while notes:
        used = 0
        end = 0
        for character in notes:
            size = length(character)
            if used + size > budget:
                break
            used += size
            end += 1
        if end == len(notes):
            cut = end
        else:
            paragraph = notes.rfind("\n\n", 0, end)
            line = notes.rfind("\n", 0, end)
            cut = paragraph + 2 if paragraph + 2 >= end // 2 else 0
            if not cut and line + 1 >= end // 2:
                cut = line + 1
            if not cut:
                cut = end
        parts.append(notes[:cut])
        notes = notes[cut:]

    messages = [f"**RatBot v{version} (part {i}/{len(parts)})**\n\n{part}"
                for i, part in enumerate(parts, 1)]
    if any(length(message) > 2000 for message in messages):
        raise ValueError("A Discord message exceeded 2,000 characters.")
    return messages


def webhook_with_wait(url):
    parsed = urlsplit(url)
    query = [(key, value) for key, value in parse_qsl(parsed.query) if key != "wait"]
    query.append(("wait", "true"))
    return urlunsplit(parsed._replace(query=urlencode(query)))


def post(url, messages):
    try:
        endpoint = webhook_with_wait(url)
    except ValueError:
        raise RuntimeError("Invalid Discord webhook URL configuration.") from None
    for number, content in enumerate(messages, 1):
        payload = json.dumps({"content": content, "allowed_mentions": {"parse": []}},
                             ensure_ascii=False).encode("utf-8")
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
    if (os.environ.get("GITHUB_EVENT_NAME") != "push"
            or os.environ.get("GITHUB_REF") != "refs/heads/master"
            or os.environ.get("DEPLOY_RESULT") != "success"):
        summary("Skipped: this was not a successful production push deployment.")
        return

    release, reason = release_from_push(os.environ.get("PUSH_BEFORE", ""),
                                        os.environ.get("PUSH_AFTER", ""))
    if release is None:
        summary(f"Skipped: {reason}")
        return

    version, notes = release
    webhook = os.environ.get("DISCORD_RELEASE_WEBHOOK_URL", "")
    if not webhook:
        summary(f"Skipped v{version}: DISCORD_RELEASE_WEBHOOK_URL Actions secret is absent.")
        return

    messages = split_notes(version, notes)
    post(webhook, messages)
    summary(f"Posted v{version} to Discord in {len(messages)} message(s).")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        # These diagnostics contain no webhook URL, token, or response body.
        print(f"Release announcement failed: {error}", file=sys.stderr)
        sys.exit(1)
