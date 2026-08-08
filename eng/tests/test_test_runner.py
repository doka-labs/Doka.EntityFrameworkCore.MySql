"""Regression tests for the repo-local test runner."""

from __future__ import annotations

import unittest
from pathlib import Path


class TestRunnerTests(unittest.TestCase):
    """Keep restore detection aligned with the current artifacts layout."""

    def test_restore_detection_requires_complete_nuget_restore_outputs(self) -> None:
        """Reject partial obj trees without NuGet's generated MSBuild imports."""
        repository_root = Path(__file__).resolve().parents[2]
        script = (
            repository_root / "eng" / "testing" / "test.sh"
        ).read_text(encoding="utf-8")

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
                self.assertIn(f'"{project}"', script)

        self.assertIn('"${project_obj}/project.assets.json"', script)
        self.assertIn(
            '"${project_obj}/${project_name}.csproj.nuget.g.props"',
            script,
        )
        self.assertIn(
            '"${project_obj}/${project_name}.csproj.nuget.g.targets"',
            script,
        )
