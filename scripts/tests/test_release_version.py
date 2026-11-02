from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from release_version import next_version, version_key


class ReleaseVersionTests(unittest.TestCase):
    def test_prereleases_and_final_release_advance_in_order(self):
        versions = ["0.3.0", "0.4.0-alpha.0", "0.4.0-alpha.2", "0.4.0-alpha.10",
                    "0.4.0-beta.1", "0.4.0-rc.2", "0.4.0-rc.10", "0.4.0"]
        for current, requested in zip(versions, versions[1:]):
            with self.subTest(current=current, requested=requested):
                self.assertEqual(next_version(current, "custom", requested), requested)
                with self.assertRaises(ValueError):
                    next_version(requested, "custom", current)

    def test_equal_versions_are_rejected(self):
        for version in ["0.4.0", "0.4.0-rc.1"]:
            with self.subTest(version=version), self.assertRaises(ValueError):
                next_version(version, "custom", version)

    def test_invalid_versions_are_rejected(self):
        for version in ["v0.4.0", "0.4", "01.4.0", "0.4.0-rc", "0.4.0-rc.01",
                        "0.4.0-preview.1", "0.4.0-rc.-1", "0.4.0+", "0.4.0+build..1",
                        "0.4.0+build_1", "0.4.0+build+1", "0.4.0-preview.1+build", "0.4.0\n"]:
            with self.subTest(version=version), self.assertRaises(ValueError):
                version_key(version)

    def test_build_metadata_is_preserved_but_does_not_affect_precedence(self):
        for suffix in ["", "-alpha.1", "-beta.1", "-rc.1"]:
            version = f"0.4.0{suffix}"
            with self.subTest(version=version):
                metadata_version = f"{version}+build-ABC.001"
                self.assertEqual(next_version("0.3.0", "custom", metadata_version), metadata_version)
                self.assertEqual(version_key(version), version_key(metadata_version))
                with self.assertRaises(ValueError):
                    next_version(version, "custom", metadata_version)

    def test_standard_bumps_increment_core_version(self):
        for current in ["1.2.3", "1.2.3-rc.1"]:
            for bump, expected in [("major", "2.0.0"), ("minor", "1.3.0"), ("patch", "1.2.4")]:
                with self.subTest(current=current, bump=bump):
                    self.assertEqual(next_version(current, bump), expected)

    def test_explicit_versions_require_custom_bump(self):
        with self.assertRaises(ValueError):
            next_version("0.3.0", "minor", "0.4.0-rc.1")


if __name__ == "__main__":
    unittest.main()
