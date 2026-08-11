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

    def test_integration_runner_passes_absolute_evidence_paths_to_vstest(self) -> None:
        """Keep relative operator inputs out of VSTest's build directory."""
        repository_root = Path(__file__).resolve().parents[2]
        script = (
            repository_root / "eng" / "testing" / "test-integration.sh"
        ).read_text(encoding="utf-8")

        self.assertIn("resolve_repo_path()", script)
        self.assertIn(
            'resolve_repo_path "${DOKA_INTEGRATION_ARTIFACTS_DIR:',
            script,
        )
        self.assertIn(
            'resolve_repo_path "${DOKA_TEST_DATABASE_EVIDENCE_FILE:',
            script,
        )
        self.assertIn(
            'resolve_repo_path "${DOKA_COVERAGE_RESULTS_DIR:',
            script,
        )

    def test_migration_runner_records_the_complete_engine_identity_matrix(self) -> None:
        """Bind release assembly to the engines the migration gate executes."""
        repository_root = Path(__file__).resolve().parents[2]
        script = (
            repository_root / "eng" / "testing" / "test-migration-deployment.sh"
        ).read_text(encoding="utf-8")

        for target in (
            "mysql84",
            "mysql97",
            "mariadb1011",
            "mariadb114",
            "mariadb118",
            "mariadb123",
        ):
            with self.subTest(target=target):
                self.assertIn(f'write_target_identity "{target}"', script)

        self.assertIn('lifecycleState: "cleanup-pending"', script)
        self.assertIn('.lifecycleState = "cleanup-completed"', script)
