import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


SPEC = importlib.util.spec_from_file_location(
    "announce_release", Path(__file__).resolve().parents[1] / "announce-release.py")
announce = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(announce)
NOTES_URL = "https://github.com/ratgirl-laboratories/sylvan-bot/blob/" + "a" * 40 + "/.changes/v1.2.3.md"


def texts(messages):
    return [child["content"] for message in messages
            for child in message["components"][0]["components"] if child["type"] == 10]


class ReleaseAnnouncementTests(unittest.TestCase):
    def test_successful_master_push_announces_selected_batch(self):
        env = {"GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/master",
               "DEPLOY_RESULT": "success", "RELEASE_BEFORE": "a" * 40,
               "RELEASE_AFTER": "b" * 40, "DISCORD_RELEASE_WEBHOOK_URL": "https://example.invalid"}
        notes = "# v1.2.3\n## General\n### Changed\n- Visible change.\n"
        with patch.dict(announce.os.environ, env, clear=True), \
                patch.object(announce, "release_from_commits", return_value=(("1.2.3", notes), "")) as release, \
                patch.object(announce, "post") as post:
            announce.main()
        release.assert_called_once_with("a" * 40, "b" * 40)
        post.assert_called_once()

    def test_manual_nonmaster_and_failed_deployments_do_not_announce(self):
        for event, ref, result in [("workflow_dispatch", "refs/heads/master", "success"),
                                   ("push", "refs/heads/feature/test", "success"),
                                   ("push", "refs/heads/master", "failure")]:
            with self.subTest(event=event, ref=ref, result=result), \
                    patch.dict(announce.os.environ, {"GITHUB_EVENT_NAME": event,
                               "GITHUB_REF": ref, "DEPLOY_RESULT": result}, clear=True), \
                    patch.object(announce, "post") as post:
                announce.main()
                post.assert_not_called()

    def test_release_channel_colours_every_announcement_container(self):
        for suffix, colour in [("-alpha.1", 0x9B59B6), ("-beta.2", 0xF0B232),
                               ("-rc.10", 0x5865F2), ("", 0x57F287),
                               ("-alpha.1+build-ABC.001", 0x9B59B6),
                               ("-beta.2+build-ABC.001", 0xF0B232),
                               ("-rc.10+build-ABC.001", 0x5865F2),
                               ("+build-ABC.001", 0x57F287)]:
            for entry_count in [1, 60]:
                with self.subTest(suffix=suffix, entry_count=entry_count):
                    version = f"1.2.3{suffix}"
                    notes = (f"# v{version}\n## General\n### Changed\n"
                             + "".join(f"- Change {number}: " + "x" * 200 + "\n"
                                       for number in range(entry_count)))
                    messages = announce.build_messages(version, notes, NOTES_URL)

                    if entry_count == 1:
                        self.assertEqual(len(messages), 1)
                    else:
                        self.assertGreater(len(messages), 1)
                    for message in messages:
                        self.assertEqual(message["components"][0]["accent_color"], colour)

    def test_mixed_release_keeps_public_entries_and_links_to_internal_details(self):
        notes = """# v1.2.3
## General
### Added
- A visible feature.
## Internal
### Changed
- Private implementation detail.
## Moderation
### Fixed
- A visible fix.
"""
        messages = announce.build_messages("1.2.3", notes, NOTES_URL)
        output = "\n".join(texts(messages))

        self.assertIn("- A visible feature.", output)
        self.assertIn("- A visible fix.", output)
        self.assertNotIn("Private implementation detail", output)
        self.assertNotIn("## Internal", output)
        self.assertEqual(texts(messages)[-1],
                         f"-# Internal changes are listed in the [full release notes]({NOTES_URL}).")

    def test_internal_only_release_announces_maintenance(self):
        notes = "# v1.2.3\n## Internal\n### Changed\n- Move some classes.\n"

        messages = announce.build_messages("1.2.3", notes, NOTES_URL)
        output = "\n".join(texts(messages))

        self.assertEqual(len(messages), 1)
        self.assertIn("Maintenance release; no user-facing changes.", output)
        self.assertIn(NOTES_URL, output)
        self.assertNotIn("Move some classes", output)

    def test_public_only_release_preserves_existing_message(self):
        notes = "# v1.2.3\n## Moderation\n### Fixed\n- A visible fix.\n"

        messages = announce.build_messages("1.2.3", notes, NOTES_URL)

        self.assertEqual(messages, [announce.container_message(
            "1.2.3", [f"<@&{announce.MODERATORS_ROLE_ID}>", "## Moderation\n### Fixed\n- A visible fix."])])

    def test_moderation_release_pings_moderators_and_visible_staff_roles_only(self):
        notes = ("# v1.2.3\n## Moderation\n### Fixed\n"
                 "- <@&1522665207225127034> can edit tickets.\n"
                 "- <@&1522665207225127034> can use @everyone and <@123> safely.\n"
                 "## Internal\n### Changed\n- <@&456> hidden details.\n")

        messages = announce.build_messages("1.2.3", notes, NOTES_URL)

        self.assertEqual(messages[0]["allowed_mentions"],
                         {"parse": [], "roles": [announce.MODERATORS_ROLE_ID, "1522665207225127034"]})
        self.assertIn(f"<@&{announce.MODERATORS_ROLE_ID}>", texts(messages))

    def test_nonmoderation_release_does_not_ping_moderators(self):
        for section in ["General", "Administration", "Internal"]:
            with self.subTest(section=section):
                notes = f"# v1.2.3\n## {section}\n### Changed\n- A change.\n"
                messages = announce.build_messages("1.2.3", notes, NOTES_URL)
                self.assertEqual(messages[0]["allowed_mentions"], {"parse": []})

    def test_repeated_staff_mentions_ping_each_role_once_across_parts(self):
        notes = ("# v1.2.3\n## Moderation\n### Fixed\n"
                 + "".join("- <@&1522665207225127034> " + "x" * 200 + "\n" for _ in range(60)))
        messages = announce.build_messages("1.2.3", notes, NOTES_URL)

        self.assertGreater(len(messages), 1)
        roles = [role for message in messages for role in message["allowed_mentions"].get("roles", [])]
        self.assertEqual(roles, [announce.MODERATORS_ROLE_ID, "1522665207225127034"])

    def test_webhook_requires_discord_to_confirm_requested_role_mentions(self):
        message = announce.container_message("1.2.3", [f"<@&{announce.MODERATORS_ROLE_ID}>"])
        for confirmed in [[], [announce.MODERATORS_ROLE_ID]]:
            with self.subTest(confirmed=confirmed):
                response = io.BytesIO(json.dumps({"id": "123", "mention_roles": confirmed}).encode())
                response.status = 200
                with patch.object(announce, "urlopen", return_value=response) as request:
                    if confirmed:
                        announce.post("https://example.invalid", [message])
                    else:
                        with self.assertRaisesRegex(RuntimeError, "did not confirm all role mentions"):
                            announce.post("https://example.invalid", [message])
                request.assert_called_once()

    def test_large_release_keeps_footer_once_at_end_within_discord_limits(self):
        notes = ("# v1.2.3\n## Moderation\n### Fixed\n"
                 + "".join(f"- Fix {number}: " + "🐀" * 140 + "\n" for number in range(60))
                 + "## Internal\n### Changed\n- Hidden detail.\n")

        messages = announce.build_messages("1.2.3", notes, NOTES_URL)
        output = "\n".join(texts(messages))

        self.assertGreater(len(messages), 1)
        self.assertEqual(output.count(NOTES_URL), 1)
        self.assertIn(NOTES_URL, texts(messages)[-1])
        self.assertNotIn("Hidden detail", output)
        for number in range(60):
            self.assertEqual(output.count(f"- Fix {number}:"), 1)
        self.assertEqual(output.count(f"<@&{announce.MODERATORS_ROLE_ID}>"), 1)
        for number, message in enumerate(messages):
            self.assertLessEqual(sum(announce.length(text) for text in texts([message])), 4000)
            self.assertLessEqual(1 + len(message["components"][0]["components"]), 40)
            self.assertEqual(message["allowed_mentions"],
                             {"parse": [], "roles": [announce.MODERATORS_ROLE_ID]} if number == 0 else {"parse": []})

    def test_footer_can_move_to_next_message_without_overflow(self):
        header = "# v1.2.3\n## Moderation\n### Fixed\n- "
        entry = "x" * (4000 - announce.length(header.rstrip("\n")))
        notes = header + entry + "\n## Internal\n### Changed\n- Hidden detail.\n"

        messages = announce.build_messages("1.2.3", notes, NOTES_URL)

        self.assertGreater(len(messages), 1)
        self.assertIn(NOTES_URL, texts(messages)[-1])
        for message in messages:
            self.assertLessEqual(sum(announce.length(text) for text in texts([message])), 4000)


if __name__ == "__main__":
    unittest.main()
