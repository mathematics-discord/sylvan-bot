"""Local release-announcement checks; no request reaches Discord."""

import importlib.util
import io
import json
import os
import re
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError


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

    def test_payloads_preserve_long_unicode_notes_without_mentions(self):
        notes = "## v0.5.0\n\n" + ("Quotes: \"hi\", emoji 🐀 and @everyone\n\n" * 160)
        notes += "🐀" * 2400 + "\nfinal line\n"
        messages = announcement.split_notes("0.5.0", notes)
        self.assertGreater(len(messages), 2)
        self.assertTrue(all(announcement.length(message) <= 2000 for message in messages))
        reconstructed = "".join(re.sub(r"^\*\*RatBot v0\.5\.0 \(part \d+/\d+\)\*\*\n\n", "", message)
                                for message in messages)
        self.assertEqual(reconstructed, notes)

        requests = []
        def accept(request, timeout):
            requests.append(request)
            self.assertEqual(timeout, 20)
            return Receipt()

        with patch.object(announcement, "urlopen", side_effect=accept):
            announcement.post("https://example.invalid/webhook/SECRET?x=1", messages)
        self.assertEqual(len(requests), len(messages))
        for request, message in zip(requests, messages):
            self.assertIn("wait=true", request.full_url)
            payload = json.loads(request.data)
            self.assertEqual(payload, {"content": message, "allowed_mentions": {"parse": []}})

    def test_rate_limit_and_other_http_failures(self):
        limited = HTTPError("https://example.invalid/SECRET", 429, "rate limit",
                            {"Retry-After": "0.01"}, io.BytesIO(b'{"retry_after":0.01}'))
        with patch.object(announcement, "urlopen", side_effect=[limited, Receipt()]), \
             patch.object(announcement.time, "sleep") as sleep:
            announcement.post("https://example.invalid/SECRET", ["hello"])
            sleep.assert_called_once_with(0.01)

        failure = HTTPError("https://example.invalid/SECRET", 500, "server error", {},
                            io.BytesIO(b'{"code":12345,"message":"SECRET"}'))
        with patch.object(announcement, "urlopen", side_effect=failure):
            with self.assertRaisesRegex(RuntimeError, "HTTP 500, Discord code 12345") as error:
                announcement.post("https://example.invalid/SECRET", ["hello"])
        self.assertNotIn("SECRET", str(error.exception))

        slow = HTTPError("https://example.invalid/SECRET", 429, "rate limit", {},
                         io.BytesIO(b'{"retry_after":61}'))
        with patch.object(announcement, "urlopen", side_effect=slow), \
             patch.object(announcement.time, "sleep") as sleep:
            with self.assertRaisesRegex(RuntimeError, "retry budget"):
                announcement.post("https://example.invalid/SECRET", ["hello"])
            sleep.assert_not_called()

        class Unconfirmed(Receipt):
            def read(self):
                return b'{}'

        with patch.object(announcement, "urlopen", return_value=Unconfirmed()):
            with self.assertRaisesRegex(RuntimeError, "did not confirm"):
                announcement.post("https://example.invalid/SECRET", ["hello"])

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


if __name__ == "__main__":
    unittest.main()
