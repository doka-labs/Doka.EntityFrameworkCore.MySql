"""Regression tests for the repository commit-message contract."""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from eng.quality import commit_message as validate_commit_message


VALID_MESSAGE = """fix(provider): harden resource identity

- Incomplete endpoint identity allowed unrelated physical databases to
  share process-wide state.

- Include the normalized transport endpoint in the cache key.
- Cover distinct and equivalent endpoints with regression tests.
"""


class CommitMessageTests(unittest.TestCase):
    """Pin every objective message-shape rule enforced before local commits."""

    def test_canonical_message_is_accepted(self) -> None:
        """Accept the rationale and change layout used by reviewed history."""
        self.assertEqual([], validate_commit_message.validate_commit_message(VALID_MESSAGE))

    def test_comments_and_git_trailers_are_accepted(self) -> None:
        """Ignore editor comments and preserve standard attribution trailers."""
        message = VALID_MESSAGE + "\nCo-authored-by: Doka Test <doka@example.invalid>\n# editor hint\n"

        self.assertEqual([], validate_commit_message.validate_commit_message(message))

    def test_verbose_diff_after_git_scissors_is_ignored(self) -> None:
        """Ignore the uncommitted diff Git appends to a verbose editor buffer."""
        message = VALID_MESSAGE + """# ------------------------ >8 ------------------------
# Do not modify or remove the line above.
diff --git a/eng/example.py b/eng/example.py
index 1111111..2222222 100644
--- a/eng/example.py
+++ b/eng/example.py
@@ -1 +1 @@
-old source line that is not commit-message prose
+new source line that is deliberately longer than seventy-two characters to prove the diff is ignored
"""

        self.assertEqual([], validate_commit_message.validate_commit_message(message))

    def test_missing_rationale_change_separator_is_rejected(self) -> None:
        """Require the blank line whose absence caused the observed history drift."""
        message = """fix(provider): harden resource identity

- Incomplete endpoint identity allowed unrelated state sharing.
- Include the normalized transport endpoint in the cache key.
"""

        errors = validate_commit_message.validate_commit_message(message)

        self.assertTrue(any("separated by one blank line" in error for error in errors))

    def test_heading_based_body_is_rejected(self) -> None:
        """Keep Why and What headings out of the bullet-based repository format."""
        message = """fix(provider): harden resource identity

Why

- Include the normalized transport endpoint in the cache key.
"""

        errors = validate_commit_message.validate_commit_message(message)

        self.assertIn("the rationale section must start with one non-empty bullet", errors)

    def test_non_conventional_subject_is_rejected(self) -> None:
        """Reject subjects that cannot be classified in repository history."""
        message = VALID_MESSAGE.replace(
            "fix(provider): harden resource identity",
            "Harden resource identity",
        )

        errors = validate_commit_message.validate_commit_message(message)

        self.assertTrue(any("Conventional Commit type" in error for error in errors))

    def test_non_ascii_content_is_rejected(self) -> None:
        """Apply the repository ASCII contract to committed prose as well as code."""
        message = VALID_MESSAGE.replace("required", "benotigt") + "\n- Prufung mit Umlaut: \u00fc.\n"

        errors = validate_commit_message.validate_commit_message(message)

        self.assertTrue(any("ASCII characters only" in error for error in errors))

    def test_overlong_line_is_rejected(self) -> None:
        """Keep commit output readable in terminals and Git hosting interfaces."""
        message = VALID_MESSAGE.replace(
            "- Include the normalized transport endpoint in the cache key.",
            "- " + ("x" * validate_commit_message.MAX_LINE_LENGTH),
        )

        errors = validate_commit_message.validate_commit_message(message)

        self.assertTrue(any("exceeds the 72-character limit" in error for error in errors))

    def test_cli_returns_actionable_failure(self) -> None:
        """Prove the hook-facing process rejects an invalid message with guidance."""
        with tempfile.TemporaryDirectory(prefix="doka-commit-message-") as temporary_directory:
            message_path = Path(temporary_directory) / "COMMIT_EDITMSG"
            message_path.write_text("invalid message\n", encoding="ascii")

            result = subprocess.run(
                [
                    sys.executable,
                    str(Path(validate_commit_message.__file__).resolve()),
                    str(message_path),
                ],
                check=False,
                capture_output=True,
                text=True,
            )

        self.assertEqual(1, result.returncode)
        self.assertIn("Commit message rejected", result.stderr)
        self.assertIn("Expected shape", result.stderr)


if __name__ == "__main__":
    unittest.main()
