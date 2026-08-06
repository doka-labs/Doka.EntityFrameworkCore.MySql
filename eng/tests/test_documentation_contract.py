"""Regression tests for the repository documentation contract."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from eng import documentation_contract


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


if __name__ == "__main__":
    unittest.main()
