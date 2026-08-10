"""Regression tests for the repository documentation contract."""

from __future__ import annotations

import re
import tempfile
import unittest
from pathlib import Path

from eng.quality import documentation as documentation_contract


class DocumentationContractTests(unittest.TestCase):
    """Prove valid navigation and fail-closed path and anchor handling."""

    def setUp(self) -> None:
        """Create an isolated synthetic documentation tree."""

        self._temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-documentation-contract-"
        )
        self.root = Path(self._temporary_directory.name)

    def tearDown(self) -> None:
        """Dispose the synthetic repository."""

        self._temporary_directory.cleanup()

    def test_valid_links_cover_generated_duplicate_and_explicit_anchors(self) -> None:
        """Accept GitHub duplicate suffixes, explicit IDs, and reference links."""

        docs = self.root / "docs"
        docs.mkdir()
        (docs / "guide.md").write_text(
            """# Guide

## Repeated Heading

## Repeated Heading

<a id="stable-anchor"></a>
""",
            encoding="ascii",
        )
        (self.root / "README.md").write_text(
            """# Project

[Duplicate](docs/guide.md#repeated-heading-1)
[Explicit](docs/guide.md#stable-anchor)
[Guide][guide]

[guide]: docs/guide.md
""",
            encoding="ascii",
        )

        result = documentation_contract.validate_repository(self.root)

        self.assertEqual(3, result.link_count)
        self.assertEqual((), result.errors)

    def test_missing_files_and_anchors_fail_with_precise_reasons(self) -> None:
        """Report independently missing paths and stale fragments."""

        docs = self.root / "docs"
        docs.mkdir()
        (docs / "guide.md").write_text("# Guide\n", encoding="ascii")
        (self.root / "README.md").write_text(
            """# Project

[Missing file](docs/missing.md)
[Missing anchor](docs/guide.md#missing)
""",
            encoding="ascii",
        )

        result = documentation_contract.validate_repository(self.root)

        self.assertEqual(
            [
                "target file does not exist",
                "anchor '#missing' does not exist",
            ],
            [error.reason for error in result.errors],
        )

    def test_fenced_examples_do_not_create_false_navigation_failures(self) -> None:
        """Ignore Markdown-looking links that are intentionally shown as source."""

        (self.root / "README.md").write_text(
            """# Project

```markdown
[Illustrative](missing.md)
```
""",
            encoding="ascii",
        )

        result = documentation_contract.validate_repository(self.root)

        self.assertEqual(0, result.link_count)
        self.assertEqual((), result.errors)

    def test_paths_outside_the_repository_are_rejected(self) -> None:
        """Keep authored navigation inside the versioned repository boundary."""

        (self.root / "README.md").write_text(
            "# Project\n\n[Outside](../outside.md)\n",
            encoding="ascii",
        )

        result = documentation_contract.validate_repository(self.root)

        self.assertEqual(1, len(result.errors))
        self.assertEqual(
            "target escapes the repository root",
            result.errors[0].reason,
        )


class ReleaseRunbookAgreementTests(unittest.TestCase):
    """Prove the runbook describes the release path the workflows implement.

    An operator follows the runbook, not the YAML. When the two drift, the
    procedure silently teaches a sequence that no longer exists -- which is how
    a manual dispatch survived in writing after the tag began starting the
    candidate by itself.
    """

    ROOT = Path(__file__).resolve().parents[2]

    def setUp(self) -> None:
        """Read the runbook and the workflows it describes."""
        self.runbook = (
            self.ROOT / "docs" / "operations" / "release-publication.md"
        ).read_text(encoding="utf-8")
        self.candidate = (
            self.ROOT / ".github" / "workflows" / "release-candidate.yml"
        ).read_text(encoding="utf-8")

    def test_the_runbook_names_the_required_aggregator(self) -> None:
        """Point the operator at the check the tag actually imports."""
        self.assertIn("repository-qualification", self.runbook)

    def test_the_runbook_names_the_pre_tag_check(self) -> None:
        """Keep the documented preparation step pointing at a real command."""
        self.assertIn("./eng/pre-tag-check.sh", self.runbook)
        self.assertTrue((self.ROOT / "eng" / "pre-tag-check.sh").is_file())

    def test_the_runbook_describes_an_automatic_tag_trigger(self) -> None:
        """Match the documented start of qualification to the workflow."""
        self.assertIn("starts the `release-candidate` workflow automatically",
                      self.runbook)
        self.assertIn("push:", self.candidate)
        self.assertIn("- \"v*\"", self.candidate)

    def test_the_runbook_states_the_receipt_count_the_workflow_produces(self) -> None:
        """Reject a documented stage count the workflow cannot satisfy."""
        stages = set(
            re.findall(r"release-candidate\.sh --stage ([a-z-]+)", self.candidate)
        ) - {"finalize"}
        stages |= set(re.findall(r"^\s+- stage: ([a-z-]+)$", self.candidate, re.M))

        self.assertEqual(6, len(stages))
        self.assertIn("exactly six required stage", self.runbook)

    def test_the_runbook_no_longer_promises_a_local_rehearsal(self) -> None:
        """Keep a removed command out of the documented procedure."""
        self.assertNotIn("./eng/rehearse-release.sh", self.runbook)


if __name__ == "__main__":
    unittest.main()
