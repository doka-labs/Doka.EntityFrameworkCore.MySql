"""Regression tests for the repo-local test runner."""

from __future__ import annotations

import unittest
from pathlib import Path


class TestRunnerTests(unittest.TestCase):
    """Keep restore detection aligned with the current artifacts layout."""

    def test_restore_detection_uses_real_project_assets(self) -> None:
        """Do not wait for obsolete project-reference asset paths."""
        repository_root = Path(__file__).resolve().parents[2]
        script = (repository_root / "eng" / "test.sh").read_text(encoding="utf-8")

        self.assertNotIn("/refs/", script)

        required_projects = (
            "Doka.EntityFrameworkCore.MySql.Tests",
            "Doka.EntityFrameworkCore.MySql.FunctionalTests",
            "Doka.EntityFrameworkCore.MySql",
            "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
            "Doka.EntityFrameworkCore.MySql.AdrValidator",
            "Doka.EntityFrameworkCore.MySql.SpecificationContract",
            "Doka.EntityFrameworkCore.MySql.TestUtilities",
            "SpecificationAdapters",
        )

        for project in required_projects:
            with self.subTest(project=project):
                self.assertIn(f"artifacts/obj/{project}/project.assets.json", script)
