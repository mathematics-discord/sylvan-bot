import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
SPEC = importlib.util.spec_from_file_location(
    "detect_release", Path(__file__).resolve().parents[1] / "detect-release.py")
detect = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(detect)


class ReleaseDetectionTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.previous = Path.cwd()
        os.chdir(self.directory.name)
        self.addCleanup(self.cleanup)
        self.git("init", "-b", "master")
        self.git("config", "user.name", "Release tests")
        self.git("config", "user.email", "release@example.invalid")
        self.write("version.json", json.dumps({"version": "1.0.0"}))
        self.before = self.commit()

    def cleanup(self):
        os.chdir(self.previous)
        self.directory.cleanup()

    def git(self, *args):
        return subprocess.check_output(["git", *args], stderr=subprocess.DEVNULL).decode().strip()

    def write(self, path, text):
        target = Path(path)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")

    def commit(self):
        self.git("add", "--all")
        self.git("commit", "-m", "Test change")
        return self.git("rev-parse", "HEAD")

    def batch(self, version="1.1.0", notes=None):
        self.write("version.json", json.dumps({"version": version}))
        self.write(f".changes/v{version}.md", notes if notes is not None else
                   f"# v{version}\n## Internal\n### Changed\n- Improve deployment.\n")
        return self.commit()

    def test_ordinary_changes_and_fragments_skip(self):
        self.write("app.txt", "change")
        self.commit()
        self.write(".changes/unreleased/change.yaml", "body: Pending")
        after = self.commit()
        self.assertEqual(detect.detect_release(self.before, after), {"has_release": "false"})

    def test_selects_latest_batch_and_excludes_later_changes(self):
        self.batch("1.1.0-alpha.1")
        parent = self.git("rev-parse", "HEAD")
        batch = self.batch("1.1.0+build.1")
        self.write("app.txt", "not released")
        after = self.commit()
        self.assertEqual(detect.detect_release(self.before, after), {
            "has_release": "true", "version": "1.1.0+build.1", "image_tag": "v1.1.0_build.1",
            "source_sha": batch, "before_sha": parent})

    def test_prerelease_is_accepted(self):
        after = self.batch("1.1.0-alpha.1")
        self.assertEqual(detect.detect_release(self.before, after)["version"], "1.1.0-alpha.1")

    def test_existing_notes_edit_skips(self):
        before = self.batch()
        self.write(".changes/v1.1.0.md", "Corrected notes")
        after = self.commit()
        self.assertEqual(detect.detect_release(before, after), {"has_release": "false"})

    def test_invalid_heading_and_empty_notes_fail(self):
        for notes in ("", "# v1.2.0\n", "# v1.1.0\n", "# v1.1.0\n## Internal\n### Changed\n- \n"):
            with self.subTest(notes=notes):
                self.git("reset", "--hard", self.before)
                after = self.batch(notes=notes)
                with self.assertRaises(ValueError):
                    detect.detect_release(self.before, after)

    def test_version_only_and_mismatched_notes_fail(self):
        self.write("version.json", '{"version": "1.1.0"}')
        after = self.commit()
        with self.assertRaises(ValueError):
            detect.detect_release(self.before, after)
        self.write(".changes/v1.2.0.md", "# v1.2.0")
        after = self.commit()
        with self.assertRaises(ValueError):
            detect.detect_release(self.before, after)

    def test_nonadvancing_batch_fails(self):
        after = self.batch("0.9.0")
        with self.assertRaises(ValueError):
            detect.detect_release(self.before, after)

    def test_merge_uses_first_parent(self):
        self.git("checkout", "-b", "release")
        self.batch()
        self.git("checkout", "master")
        self.write("app.txt", "master change")
        parent = self.commit()
        self.git("merge", "--no-ff", "release", "-m", "Merge release")
        after = self.git("rev-parse", "HEAD")
        result = detect.detect_release(self.before, after)
        self.assertEqual(result["source_sha"], after)
        self.assertEqual(result["before_sha"], parent)

    def test_missing_or_unrelated_history_fails(self):
        after = self.batch()
        for before in ("", "0" * 40, "a" * 40):
            with self.subTest(before=before):
                with self.assertRaises((ValueError, subprocess.CalledProcessError)):
                    detect.detect_release(before, after)
        with self.assertRaises(ValueError):
            detect.detect_release(after, self.before)


if __name__ == "__main__":
    unittest.main()
