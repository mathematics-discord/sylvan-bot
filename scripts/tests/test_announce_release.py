"""Local release-announcement checks; no request reaches Discord."""

import importlib.util
import io
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlsplit


SCRIPT = Path(__file__).resolve().parents[1] / "announce-release.py"
spec = importlib.util.spec_from_file_location("announce_release", SCRIPT)
announcement = importlib.util.module_from_spec(spec)
spec.loader.exec_module(announcement)


class Receipt:
    status = 200

    def __enter__(self):
        return self

    def __exit__(self, *_):
        pass

    def read(self):
        return b'{"id":"123"}'


class ReleaseAnnouncementTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="sylvan-announcement-")
        self.repo = Path(self.temp.name)
        self.old_cwd = Path.cwd()
        os.chdir(self.repo)
        self.git("init", "-q", "-b", "master")
        self.git("config", "user.name", "Fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        self.write("version.json", '{"version":"0.4.0","assemblyVersion":"0.1.0.0"}')
        self.base = self.commit("Baseline")

    def tearDown(self):
        os.chdir(self.old_cwd)
        self.temp.cleanup()

    def git(self, *args):
        return subprocess.check_output(["git", *args], text=True).strip()

    def write(self, name, value):
        path = self.repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding="utf-8")

    def commit(self, message):
        self.git("add", ".")
        self.git("commit", "-qm", message)
        return self.git("rev-parse", "HEAD")

    def test_push_range_and_release_file_rules(self):
        self.write(".changes/unreleased/pending.yaml", "kind: Changed\nbody: pending\n")
        migration = self.commit("Migrate changelog")
        self.assertIsNone(announcement.release_from_push(self.base, migration)[0])

        self.write("ordinary.txt", "code")
        ordinary = self.commit("Ordinary change")
        self.assertIsNone(announcement.release_from_push(migration, ordinary)[0])

        self.write(".changes/v0.5.0.md", "## v0.5.0\n\n- New thing\n")
        self.commit("Add curated release notes")
        self.write("version.json", '{"version":"0.5.0","assemblyVersion":"0.1.0.0"}')
        release = self.commit("Advance version")
        self.assertEqual(announcement.release_from_push(ordinary, release)[0],
                         ("0.5.0", "## v0.5.0\n\n- New thing\n"))

        self.write(".changes/v0.5.0.md", "## v0.5.0\n\n- Edited\n")
        edit = self.commit("Edit existing notes")
        self.assertIsNone(announcement.release_from_push(release, edit)[0])

        self.write(".changes/v0.6.0.md", "## v0.6.0\n")
        self.write(".changes/v0.7.0.md", "## v0.7.0\n")
        self.write("version.json", '{"version":"0.7.0","assemblyVersion":"0.1.0.0"}')
        multiple = self.commit("Batch twice")
        self.assertEqual(announcement.release_from_push(edit, multiple)[0],
                         ("0.7.0", "## v0.7.0\n"))
        self.assertIsNone(announcement.release_from_push("0" * 40, multiple)[0])
        self.assertIsNone(announcement.release_from_push("f" * 40, multiple)[0])

    def test_single_commit_release_and_edited_note_with_version_advance(self):
        self.write(".changes/v0.5.0.md", "## v0.5.0\n")
        self.write("version.json", '{"version":"0.5.0"}')
        release = self.commit("Squash release")
        self.assertEqual(announcement.release_from_push(self.base, release)[0][0], "0.5.0")

        self.write(".changes/v0.5.0.md", "## v0.5.0 edited\n")
        self.write("version.json", '{"version":"0.6.0"}')
        edit = self.commit("Change version and old notes")
        self.assertIsNone(announcement.release_from_push(release, edit)[0])

    def test_old_and_new_batches_have_the_same_compact_layout(self):
        old = ("## v0.5.0 - 2026-10-11\n\n### General\n\n#### Added\n\n"
               "- Earlier kind.\n\n#### Changed\n\n"
               "- First 🐀 @everyone\n  continues here.\n\n"
               "### Administration\n\n#### Added\n\n- Second entry.\n\n"
               "### Moderation\n\n#### Fixed\n\n- Third entry.\n")
        new = ("# v0.5.0 - 2026-10-11\n## General\n### Added\n- Earlier kind.\n### Changed\n"
               "- First 🐀 @everyone\n  continues here.\n"
               "## Administration\n### Added\n- Second entry.\n"
               "## Moderation\n### Fixed\n- Third entry.\n")
        expected = [
            "# v0.5.0",
            "## General\n### Added\n- Earlier kind.\n### Changed\n- First 🐀 @everyone\n  continues here.",
            "## Administration\n### Added\n- Second entry.",
            "## Moderation\n### Fixed\n- Third entry.",
        ]
        for notes in (old, new):
            messages = announcement.build_messages("0.5.0", notes)
            self.assertEqual(len(messages), 1)
            message = messages[0]
            self.assertEqual(message["flags"], 32768)
            self.assertEqual(message["allowed_mentions"], {"parse": []})
            self.assertEqual(len(message["components"]), 1)
            container = message["components"][0]
            self.assertEqual(container["type"], 17)
            children = container["components"]
            self.assertEqual([child["type"] for child in children], [10, 14, 10, 14, 10, 14, 10])
            self.assertEqual([child["content"] for child in children if child["type"] == 10], expected)
            self.assertTrue(all(child == {"type": 14, "divider": False, "spacing": 1}
                                for child in children if child["type"] == 14))
            self.assertNotIn("content", message)
            self.assertNotIn("embeds", message)
            self.assertNotIn("####", json.dumps(message))

        self.assertEqual(announcement.build_messages("0.5.0", "## v0.5.0 - 2026-10-11\n"),
                         [announcement.container_message("0.5.0", [])])
        with self.assertRaisesRegex(ValueError, "matching release heading"):
            announcement.build_messages("0.5.0", "# v0.5.1\n## General\n### Changed\n- Wrong batch")

    def test_empty_sections_are_omitted_and_multiline_entries_are_preserved(self):
        notes = ("# v0.5.0 - 2026-10-11\n## General\n### Changed\n"
                 "- First paragraph\n\n  second paragraph with **Markdown**.\n"
                 "## Administration\n### Added\n"
                 "## Moderation\n### Fixed\n- Another entry.\n")
        message = announcement.build_messages("0.5.0", notes)[0]
        contents = [child["content"] for child in message["components"][0]["components"]
                    if child["type"] == 10]
        self.assertEqual(contents, ["# v0.5.0",
                                    "## General\n### Changed\n- First paragraph\n\n  second paragraph with **Markdown**.",
                                    "## Moderation\n### Fixed\n- Another entry."])

        # Changie 1.26 emits unindented continuation lines from a block body.
        generated = ("# v0.5.0 - 2026-10-11\n## General\n### Changed\n"
                     "- First paragraph with **Markdown**.\n\nSecond paragraph with 🐀.\n"
                     "- nested item\n\n- Another actual change.\n")
        sections = announcement.parse_sections("0.5.0", generated)
        self.assertEqual(sections[0]["kinds"][0]["entries"],
                         ["- First paragraph with **Markdown**.\n\nSecond paragraph with 🐀.\n- nested item",
                          "- Another actual change."])

    def test_long_releases_pack_complete_entries_and_split_one_oversized_entry(self):
        entries = [f"- Entry {number}: " + "🐀" * 60 for number in range(100)]
        notes = "# v0.5.0\n## General\n### Changed\n" + "\n".join(entries)
        messages = announcement.build_messages("0.5.0", notes)
        self.assertGreater(len(messages), 1)
        for number, message in enumerate(messages, 1):
            children = message["components"][0]["components"]
            self.assertEqual(children[0]["content"], f"# v0.5.0 (part {number}/{len(messages)})")
            self.assertLessEqual(1 + len(children), announcement.MAX_COMPONENTS)
            self.assertLessEqual(sum(announcement.length(child["content"]) for child in children
                                     if child["type"] == 10), announcement.MAX_TEXT_UNITS)
        rendered = "\n".join(child["content"] for message in messages
                              for child in message["components"][0]["components"] if child["type"] == 10)
        for entry in entries:
            self.assertEqual(rendered.count(entry), 1)

        huge = "🐀" * 5000 + "\nA final line with `Markdown`."
        messages = announcement.build_messages("0.5.0", "# v0.5.0\n## General\n### Changed\n- " + huge)
        self.assertGreater(len(messages), 2)
        pieces = [child["content"].split("\n- ", 1)[1]
                  for message in messages for child in message["components"][0]["components"]
                  if child["type"] == 10 and child["content"].startswith("## General")]
        self.assertEqual("".join(pieces), huge)
        self.assertTrue(all(announcement.length(piece) <= announcement.MAX_TEXT_UNITS for piece in pieces))

    def test_webhook_request_has_v2_payload_and_preserves_query_options(self):
        notes = "# v0.5.0\n## General\n### Changed\n- Quotes: \"hi\", 🐀 and @everyone."
        messages = announcement.build_messages("0.5.0", notes)
        requests = []
        def accept(request, timeout):
            requests.append(request)
            self.assertEqual(timeout, 20)
            return Receipt()

        with patch.object(announcement, "urlopen", side_effect=accept):
            announcement.post("https://example.invalid/webhook/SECRET?thread_id=123&empty=&wait=false&with_components=false", messages)
        self.assertEqual(len(requests), len(messages))
        request = requests[0]
        self.assertEqual(parse_qs(urlsplit(request.full_url).query, keep_blank_values=True),
                         {"thread_id": ["123"], "empty": [""], "wait": ["true"],
                          "with_components": ["true"]})
        self.assertEqual(request.get_header("User-agent"), announcement.USER_AGENT)
        self.assertEqual(request.get_header("Content-type"), "application/json; charset=utf-8")
        self.assertEqual(json.loads(request.data), messages[0])
        self.assertIn("🐀", request.data.decode("utf-8"))

    def test_rate_limit_and_other_http_failures(self):
        messages = announcement.build_messages("0.5.0", "# v0.5.0\n")
        limited = HTTPError("https://example.invalid/SECRET", 429, "rate limit",
                            {"Retry-After": "0.01"}, io.BytesIO(b'{"retry_after":0.01}'))
        with patch.object(announcement, "urlopen", side_effect=[limited, Receipt()]), \
             patch.object(announcement.time, "sleep") as sleep:
            announcement.post("https://example.invalid/SECRET", messages)
            sleep.assert_called_once_with(0.01)

        failure = HTTPError("https://example.invalid/SECRET", 500, "server error", {},
                            io.BytesIO(b'{"code":12345,"message":"SECRET"}'))
        with patch.object(announcement, "urlopen", side_effect=failure):
            with self.assertRaisesRegex(RuntimeError, "HTTP 500, Discord code 12345") as error:
                announcement.post("https://example.invalid/SECRET", messages)
        self.assertNotIn("SECRET", str(error.exception))

        slow = HTTPError("https://example.invalid/SECRET", 429, "rate limit", {},
                         io.BytesIO(b'{"retry_after":61}'))
        with patch.object(announcement, "urlopen", side_effect=slow), \
             patch.object(announcement.time, "sleep") as sleep:
            with self.assertRaisesRegex(RuntimeError, "retry budget"):
                announcement.post("https://example.invalid/SECRET", messages)
            sleep.assert_not_called()

        class Unconfirmed(Receipt):
            def read(self):
                return b'{}'

        with patch.object(announcement, "urlopen", return_value=Unconfirmed()):
            with self.assertRaisesRegex(RuntimeError, "did not confirm"):
                announcement.post("https://example.invalid/SECRET", messages)

    def test_missing_secret_and_failed_deployment_skip(self):
        self.write(".changes/v0.5.0.md", "## v0.5.0\n")
        self.write("version.json", '{"version":"0.5.0"}')
        release = self.commit("Release")
        output = self.repo / "summary.md"
        env = {"GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/master",
               "DEPLOY_RESULT": "success", "PUSH_BEFORE": self.base,
               "PUSH_AFTER": release, "GITHUB_STEP_SUMMARY": str(output)}
        with patch.dict(os.environ, env, clear=True), patch.object(announcement, "post") as post:
            announcement.main()
            post.assert_not_called()
        self.assertIn("secret is absent", output.read_text(encoding="utf-8"))

        env["DEPLOY_RESULT"] = "failure"
        env["DISCORD_RELEASE_WEBHOOK_URL"] = "https://example.invalid/SECRET"
        with patch.dict(os.environ, env, clear=True), patch.object(announcement, "post") as post:
            announcement.main()
            post.assert_not_called()

    def test_successful_production_release_uses_committed_notes(self):
        self.write(".changes/v0.5.0.md", "# v0.5.0 - 2026-10-11\n## General\n### Changed\n- Committed entry.\n")
        self.write("version.json", '{"version":"0.5.0"}')
        release = self.commit("Release")
        self.write(".changes/v0.5.0.md", "# v0.5.0\n## General\n### Changed\n- Uncommitted edit.\n")
        env = {"GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/master",
               "DEPLOY_RESULT": "success", "PUSH_BEFORE": self.base, "PUSH_AFTER": release,
               "DISCORD_RELEASE_WEBHOOK_URL": "https://example.invalid/SECRET",
               "PATH": os.environ.get("PATH", "")}
        with patch.dict(os.environ, env, clear=True), patch.object(announcement, "post") as post:
            announcement.main()
        messages = post.call_args.args[1]
        self.assertEqual(messages[0]["components"][0]["components"][2]["content"],
                         "## General\n### Changed\n- Committed entry.")


if __name__ == "__main__":
    unittest.main()
