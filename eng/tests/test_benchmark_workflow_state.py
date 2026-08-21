"""Tests for the hosted benchmark workflow control plane."""

from __future__ import annotations

import argparse
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from eng.performance import workflow_state as benchmark_workflow_state


class BenchmarkWorkflowStateTests(unittest.TestCase):
    """Keep expensive scorecard allocation behind deterministic decisions."""

    def setUp(self) -> None:
        """Create the minimal contract and proposal fixtures."""
        self.repo = Path("/repository")
        self.contract_path = self.repo / "benchmarks/performance-contract.json"
        self.proposal_path = self.repo / "artifacts/proposed-baseline.json"
        self.contract = {
            "requiredTargets": {
                "mysql84": {},
                "mariadb118": {},
            },
        }
        self.baseline = {
            "baselines": [
                {
                    "target": target,
                    "profile": "scorecard",
                    "runnerClass": "github-ubuntu-latest-x64",
                    "commit": "source-commit",
                }
                for target in ("mysql84", "mariadb118")
            ],
        }

    def test_comparison_mode_separates_checks_from_baseline_seeding(
        self,
    ) -> None:
        """Keep normal checks CPU-independent without breaking recalibration."""
        self.assertEqual(
            "paired",
            benchmark_workflow_state.comparison_mode_for_baseline_mode(
                "compare",
            ),
        )
        self.assertEqual(
            "historical",
            benchmark_workflow_state.comparison_mode_for_baseline_mode(
                "seed",
            ),
        )
        with self.assertRaisesRegex(
            benchmark_workflow_state.WorkflowStateError,
            "Unsupported resolved baseline mode",
        ):
            benchmark_workflow_state.comparison_mode_for_baseline_mode(
                "unknown",
            )

    def test_main_persists_the_resolved_comparison_mode(self) -> None:
        """Bind the control-plane payload to the resolved comparison mode."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output = Path(temp_dir) / "workflow-state.json"
            arguments = argparse.Namespace(
                repo=self.repo,
                event_name="push",
                before_revision="before-commit",
                current_revision="current-commit",
                baseline_mode="compare",
                contract=self.contract_path,
                proposed_baseline=None,
                proposal_head_ref=None,
                profile="scorecard",
                runner_class="github-ubuntu-latest-x64",
                output=output,
            )
            proposal = benchmark_workflow_state.ProposalState(
                disposition="absent",
                reason="No proposal exists.",
            )

            with (
                patch.object(
                    benchmark_workflow_state,
                    "parse_args",
                    return_value=arguments,
                ),
                patch.object(
                    benchmark_workflow_state.performance_evidence,
                    "load_json",
                    return_value=self.contract,
                ),
                patch.object(
                    benchmark_workflow_state.performance_evidence,
                    "validate_contract",
                ),
                patch.object(
                    benchmark_workflow_state,
                    "event_measurement_tier",
                    return_value=("smoke", ("src/Provider.cs",)),
                ),
                patch.object(
                    benchmark_workflow_state,
                    "inspect_proposal",
                    return_value=proposal,
                ),
                patch.object(
                    benchmark_workflow_state,
                    "decide_work",
                    return_value=("smoke", False, False),
                ),
            ):
                self.assertEqual(0, benchmark_workflow_state.main())

            payload = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual("compare", payload["baselineMode"])
            self.assertEqual("paired", payload["comparisonMode"])
            self.assertEqual("smoke", payload["measurementTier"])

    def test_performance_inputs_are_limited_to_measurement_inputs(
        self,
    ) -> None:
        """Refresh evidence only when measured behavior can have changed."""
        included = (
            "benchmarks/baselines/unexpected-baseline.json",
            "benchmarks/performance-contract.json",
            "benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks/Program.cs",
            "benchmarks/corpora/translation-corpus.json",
            "docker/compose.yml",
            "Directory.Packages.props",
            "eng/benchmark.sh",
            "eng/common/deadline.py",
            "eng/common/verify-dotnet.sh",
            "eng/performance/benchmark.sh",
            "global.json",
            "src/Doka.EntityFrameworkCore.MySql/Storage/MySqlTypeMapping.cs",
            (
                "src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/"
                "Storage/MySqlGeometryTypeMapping.cs"
            ),
        )
        excluded = (
            ".github/workflows/benchmark-scorecard.yml",
            ".github/workflows/benchmark.yml",
            "benchmarks/baselines/doka-benchmark-baseline.json",
            "eng/performance/workflow_state.py",
            "eng/performance/check-benchmark-ratios.sh",
            "eng/performance/inputs.py",
            "eng/performance/workflow_state.py",
            "eng/performance/cli.py",
            "docs/operations/performance-evidence.md",
            "tests/Doka.EntityFrameworkCore.MySql.Tests/MySqlOptionsTests.cs",
        )

        for path in included:
            with self.subTest(path=path):
                self.assertTrue(
                    benchmark_workflow_state.is_performance_input(path),
                )

        for path in excluded:
            with self.subTest(path=path):
                self.assertFalse(
                    benchmark_workflow_state.is_performance_input(path),
                )

    def test_measurement_tiers_keep_smoke_non_qualifying(self) -> None:
        """Reserve complete scorecards for inputs that define measurement."""
        cases = {
            "src/Doka.EntityFrameworkCore.MySql/Storage/Mapping.cs": "smoke",
            ".github/workflows/benchmark-smoke.yml": "smoke",
            "benchmarks/performance-contract.json": "scorecard",
            ".github/workflows/benchmark-scorecard.yml": "scorecard",
            ".github/workflows/benchmark-target.yml": "scorecard",
            "eng/performance/paired.py": "scorecard",
            "eng/performance/host-preflight.sh": "scorecard",
            "eng/performance/paired-benchmark.sh": "scorecard",
            "docker/compose.yml": "scorecard",
            "Directory.Build.props": "scorecard",
            "Directory.Packages.props": "scorecard",
            "global.json": "scorecard",
            "src/Doka.EntityFrameworkCore.MySql/Provider.csproj": "scorecard",
            "src/Doka.EntityFrameworkCore.MySql/packages.lock.json": "scorecard",
            "benchmarks/baselines/doka-benchmark-baseline.json": "none",
            "docs/operations/performance-evidence.md": "none",
            "tests/ProviderTests.cs": "none",
        }

        for path, expected in cases.items():
            with self.subTest(path=path):
                self.assertEqual(
                    expected,
                    benchmark_workflow_state.measurement_tier(path),
                )

    def test_tracked_performance_domain_is_fail_closed(self) -> None:
        """Require an explicit decision for every performance-domain file."""
        repository_root = Path(__file__).parents[2]
        result = subprocess.run(
            ["git", "ls-files", "eng/performance"],
            cwd=repository_root,
            check=True,
            capture_output=True,
            text=True,
        )
        control_plane = {
            "eng/performance/inputs.py",
            "eng/performance/workflow_state.py",
        }

        for path in result.stdout.splitlines():
            expected = "none" if path in control_plane else "scorecard"

            with self.subTest(path=path):
                self.assertEqual(
                    expected,
                    benchmark_workflow_state.measurement_tier(path),
                )

    def test_central_package_groups_bind_the_complete_measured_inventory(
        self,
    ) -> None:
        """Bind production and benchmark packages without listing test tools."""
        document = Path(__file__).parents[2] / "Directory.Packages.props"
        contract = benchmark_workflow_state.central_package_contract(
            document.read_text(encoding="utf-8"),
        )

        self.assertEqual(
            {
                "package:BenchmarkDotNet",
                "package:Microsoft.EntityFrameworkCore.Design",
                "package:Microsoft.EntityFrameworkCore.Relational",
                "package:MySqlConnector",
                "package:NetTopologySuite",
                "package:System.IO.Hashing",
                "property:DokaEfCoreVersion",
                "property:DokaMySqlConnectorVersion",
                "property:ManagePackageVersionsCentrally",
            },
            set(contract),
        )

    def test_central_package_changes_allocate_only_for_measured_inputs(
        self,
    ) -> None:
        """Separate runtime and benchmark CVE bumps from tooling-only bumps."""
        before = """\
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <DokaMySqlConnectorVersion>[2.5.0, 3.0.0)</DokaMySqlConnectorVersion>
  </PropertyGroup>
  <ItemGroup Label="Production">
    <PackageVersion Include="MySqlConnector" Version="$(DokaMySqlConnectorVersion)" />
  </ItemGroup>
  <ItemGroup Label="Benchmarks">
    <PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />
  </ItemGroup>
  <ItemGroup Label="Tests">
    <PackageVersion Include="SSH.NET" Version="2026.0.0" />
  </ItemGroup>
</Project>
"""
        cases = (
            (
                before.replace("[2.5.0, 3.0.0)", "[2.5.1, 3.0.0)"),
                True,
            ),
            (before.replace("0.15.8", "0.15.9"), True),
            (before.replace("2026.0.0", "2026.0.1"), False),
            (before.replace("<Project>", "<Project>\n  <!-- formatting -->"), False),
            (
                before.replace(
                    '<ItemGroup Label="Tests">',
                    '<ItemGroup Label="Unclassified">',
                ),
                True,
            ),
            (
                before.replace(
                    '<ItemGroup Label="Production">',
                    '<ItemGroup Label="Tests">',
                ),
                True,
            ),
        )

        for current, expected in cases:
            with (
                self.subTest(expected=expected, current=current),
                patch.object(
                    benchmark_workflow_state,
                    "revision_file",
                    side_effect=(before, current),
                ),
            ):
                self.assertEqual(
                    expected,
                    benchmark_workflow_state.central_package_change_requires_scorecard(
                        self.repo,
                        "before-commit",
                        "current-commit",
                    ),
                )

    @patch.object(benchmark_workflow_state, "run_git")
    def test_control_plane_changes_do_not_allocate_the_scorecard(
        self,
        run_git: Mock,
    ) -> None:
        """Keep orchestration-only changes on the inexpensive resolver path."""
        run_git.return_value = subprocess.CompletedProcess(
            [],
            0,
            "\n".join(
                (
                    ".github/workflows/benchmark.yml",
                    "eng/performance/workflow_state.py",
                    "eng/performance/inputs.py",
                    "docs/operations/performance-evidence.md",
                )
            ),
            "",
        )

        changes = benchmark_workflow_state.relevant_changes(
            self.repo,
            "before-commit",
            "current-commit",
        )

        self.assertEqual((), changes)

    def test_scheduled_and_manual_events_always_refresh_evidence(self) -> None:
        """Keep periodic and operator-requested measurements unconditional."""
        for event_name in ("schedule", "workflow_dispatch"):
            with self.subTest(event_name=event_name):
                tier, changes = (
                    benchmark_workflow_state.event_measurement_tier(
                        self.repo,
                        event_name,
                        None,
                        "current-commit",
                    )
                )

                self.assertEqual("scorecard", tier)
                self.assertEqual((f"<{event_name}>",), changes)

    @patch.object(benchmark_workflow_state, "changed_measurement_inputs")
    def test_push_selects_the_strongest_changed_measurement_tier(
        self,
        changes: Mock,
    ) -> None:
        """Route provider changes to smoke and measurement changes to scorecard."""
        cases = (
            (benchmark_workflow_state.MeasurementChanges(), "none"),
            (
                benchmark_workflow_state.MeasurementChanges(
                    smoke=(
                        "src/Doka.EntityFrameworkCore.MySql/Storage/Mapping.cs",
                    ),
                ),
                "smoke",
            ),
            (
                benchmark_workflow_state.MeasurementChanges(
                    scorecard=("eng/benchmark.sh",),
                    smoke=(
                        "src/Doka.EntityFrameworkCore.MySql/Storage/Mapping.cs",
                    ),
                ),
                "scorecard",
            ),
        )
        for observed_changes, expected_tier in cases:
            with self.subTest(changes=observed_changes, expected=expected_tier):
                changes.return_value = observed_changes

                tier, observed = (
                    benchmark_workflow_state.event_measurement_tier(
                        self.repo,
                        "push",
                        "before-commit",
                        "current-commit",
                    )
                )

                self.assertEqual(expected_tier, tier)
                self.assertEqual(observed_changes.all, observed)

        self.assertEqual(3, changes.call_count)

    def test_real_git_diff_allocates_only_for_performance_inputs(self) -> None:
        """Exercise the production Git diff path instead of a mocked result."""
        with tempfile.TemporaryDirectory(
            prefix="doka-benchmark-workflow-",
        ) as directory:
            repository = Path(directory)
            source_path = repository / "src/Provider/QueryGenerator.cs"
            source_path.parent.mkdir(parents=True)
            source_path.write_text("internal sealed class QueryGenerator {}\n")
            (repository / "README.md").write_text("# Provider\n")

            self._git(repository, "init", "--initial-branch=main")
            self._git(repository, "config", "user.name", "Workflow Test")
            self._git(
                repository,
                "config",
                "user.email",
                "workflow-test@example.invalid",
            )
            self._git(repository, "config", "commit.gpgsign", "false")
            self._git(repository, "add", ".")
            self._git(repository, "commit", "-m", "Initial state")
            initial_revision = self._git(
                repository,
                "rev-parse",
                "HEAD",
            ).stdout.strip()

            (repository / "README.md").write_text("# Provider\n\nDocs.\n")
            self._git(repository, "add", "README.md")
            self._git(repository, "commit", "-m", "Update documentation")
            documentation_revision = self._git(
                repository,
                "rev-parse",
                "HEAD",
            ).stdout.strip()

            documentation_tier, documentation_changes = (
                benchmark_workflow_state.event_measurement_tier(
                    repository,
                    "push",
                    initial_revision,
                    documentation_revision,
                )
            )

            self.assertEqual("none", documentation_tier)
            self.assertEqual((), documentation_changes)

            source_path.write_text(
                "internal sealed class QueryGenerator { public int Id => 1; }\n",
            )
            self._git(repository, "add", "src/Provider/QueryGenerator.cs")
            self._git(repository, "commit", "-m", "Update provider")
            provider_revision = self._git(
                repository,
                "rev-parse",
                "HEAD",
            ).stdout.strip()

            provider_tier, provider_changes = (
                benchmark_workflow_state.event_measurement_tier(
                    repository,
                    "push",
                    documentation_revision,
                    provider_revision,
                )
            )

            self.assertEqual("smoke", provider_tier)
            self.assertEqual(
                ("src/Provider/QueryGenerator.cs",),
                provider_changes,
            )

            (repository / "docs").mkdir()
            self._git(
                repository,
                "mv",
                "src/Provider/QueryGenerator.cs",
                "docs/QueryGenerator.cs",
            )
            self._git(repository, "commit", "-m", "Move provider source")
            moved_revision = self._git(
                repository,
                "rev-parse",
                "HEAD",
            ).stdout.strip()

            moved_tier, moved_changes = (
                benchmark_workflow_state.event_measurement_tier(
                    repository,
                    "push",
                    provider_revision,
                    moved_revision,
                )
            )

            self.assertEqual("smoke", moved_tier)
            self.assertEqual(
                ("src/Provider/QueryGenerator.cs",),
                moved_changes,
            )

            deleted_path = repository / "src/Provider/Deleted.cs"
            deleted_path.write_text("internal sealed class Deleted {}\n")
            self._git(repository, "add", "src/Provider/Deleted.cs")
            self._git(repository, "commit", "-m", "Add provider source")
            added_revision = self._git(
                repository,
                "rev-parse",
                "HEAD",
            ).stdout.strip()

            deleted_path.unlink()
            self._git(repository, "add", "--all")
            self._git(repository, "commit", "-m", "Delete provider source")
            deleted_revision = self._git(
                repository,
                "rev-parse",
                "HEAD",
            ).stdout.strip()

            deleted_tier, deleted_changes = (
                benchmark_workflow_state.event_measurement_tier(
                    repository,
                    "push",
                    added_revision,
                    deleted_revision,
                )
            )

            self.assertEqual("smoke", deleted_tier)
            self.assertEqual(
                ("src/Provider/Deleted.cs",),
                deleted_changes,
            )

    @patch.object(benchmark_workflow_state, "run_git")
    def test_initial_push_requires_scorecard_evidence(
        self,
        run_git: Mock,
    ) -> None:
        """Treat initial branch creation as a complete measurement edge."""
        tier, changes = benchmark_workflow_state.event_measurement_tier(
            self.repo,
            "push",
            benchmark_workflow_state.ZERO_REVISION,
            "current-commit",
        )

        self.assertEqual("scorecard", tier)
        self.assertEqual(("<initial-push>",), changes)
        run_git.assert_not_called()

    @patch.object(benchmark_workflow_state, "run_git")
    @patch.object(benchmark_workflow_state, "relevant_changes")
    @patch.object(
        benchmark_workflow_state.performance_evidence,
        "load_json",
    )
    @patch.object(
        benchmark_workflow_state.performance_evidence,
        "validate_baseline_file",
    )
    def test_current_proposal_is_reused_and_synchronized_cheaply(
        self,
        validate_baseline: Mock,
        load_json: Mock,
        changes: Mock,
        run_git: Mock,
    ) -> None:
        """Avoid a second scorecard while keeping the proposal mergeable."""
        load_json.return_value = self.baseline
        changes.return_value = ()
        run_git.side_effect = (
            subprocess.CompletedProcess([], 0, "source-commit\n", ""),
            subprocess.CompletedProcess([], 0, "", ""),
            subprocess.CompletedProcess([], 1, "", ""),
        )

        proposal = benchmark_workflow_state.inspect_proposal(
            self.repo,
            self.contract_path,
            self.contract,
            self.proposal_path,
            "origin/automation/performance-baseline-2026-08-07",
            "current-commit",
            "scorecard",
            "github-ubuntu-latest-x64",
        )
        measurement, sync_required, proposal_required = (
            benchmark_workflow_state.decide_work(
                "seed",
                "none",
                proposal,
            )
        )

        validate_baseline.assert_called_once()
        self.assertEqual("current", proposal.disposition)
        self.assertTrue(proposal.behind_current)
        self.assertEqual("none", measurement)
        self.assertTrue(sync_required)
        self.assertFalse(proposal_required)

    def test_scheduled_seed_refreshes_a_current_proposal(self) -> None:
        """Refresh evidence when the event explicitly requests measurement."""
        proposal = benchmark_workflow_state.ProposalState(
            disposition="current",
            reason="Current proposal.",
            behind_current=False,
        )

        measurement, sync_required, proposal_required = (
            benchmark_workflow_state.decide_work(
                "seed",
                "scorecard",
                proposal,
            )
        )

        self.assertEqual("scorecard", measurement)
        self.assertFalse(sync_required)
        self.assertTrue(proposal_required)

    @patch.object(benchmark_workflow_state, "run_git")
    @patch.object(benchmark_workflow_state, "relevant_changes")
    @patch.object(
        benchmark_workflow_state.performance_evidence,
        "load_json",
    )
    @patch.object(
        benchmark_workflow_state.performance_evidence,
        "validate_baseline_file",
    )
    def test_current_up_to_date_proposal_is_a_no_op(
        self,
        validate_baseline: Mock,
        load_json: Mock,
        changes: Mock,
        run_git: Mock,
    ) -> None:
        """Avoid both measurement and mutation for a current proposal."""
        load_json.return_value = self.baseline
        changes.return_value = ()
        run_git.side_effect = (
            subprocess.CompletedProcess([], 0, "source-commit\n", ""),
            subprocess.CompletedProcess([], 0, "", ""),
            subprocess.CompletedProcess([], 0, "", ""),
        )

        proposal = benchmark_workflow_state.inspect_proposal(
            self.repo,
            self.contract_path,
            self.contract,
            self.proposal_path,
            "origin/automation/performance-baseline-2026-08-07",
            "current-commit",
            "scorecard",
            "github-ubuntu-latest-x64",
        )

        self.assertEqual("current", proposal.disposition)
        self.assertFalse(proposal.behind_current)
        self.assertEqual(
            ("none", False, False),
            benchmark_workflow_state.decide_work("seed", "none", proposal),
        )
        validate_baseline.assert_called_once()

    @patch.object(benchmark_workflow_state, "run_git")
    @patch.object(benchmark_workflow_state, "relevant_changes")
    @patch.object(
        benchmark_workflow_state.performance_evidence,
        "load_json",
    )
    @patch.object(
        benchmark_workflow_state.performance_evidence,
        "validate_baseline_file",
    )
    def test_changed_performance_input_refreshes_the_existing_proposal(
        self,
        validate_baseline: Mock,
        load_json: Mock,
        changes: Mock,
        run_git: Mock,
    ) -> None:
        """Replace stale proposal evidence on its stable review branch."""
        load_json.return_value = self.baseline
        changes.return_value = ("eng/benchmark.sh",)
        run_git.side_effect = (
            subprocess.CompletedProcess([], 0, "source-commit\n", ""),
            subprocess.CompletedProcess([], 0, "", ""),
            subprocess.CompletedProcess([], 1, "", ""),
        )

        proposal = benchmark_workflow_state.inspect_proposal(
            self.repo,
            self.contract_path,
            self.contract,
            self.proposal_path,
            "origin/automation/performance-baseline-2026-08-07",
            "current-commit",
            "scorecard",
            "github-ubuntu-latest-x64",
        )
        measurement, sync_required, proposal_required = (
            benchmark_workflow_state.decide_work(
                "seed",
                "scorecard",
                proposal,
            )
        )

        validate_baseline.assert_called_once()
        self.assertEqual("stale", proposal.disposition)
        self.assertEqual(("eng/benchmark.sh",), proposal.relevant_changes)
        self.assertEqual("scorecard", measurement)
        self.assertFalse(sync_required)
        self.assertTrue(proposal_required)

    @patch.object(benchmark_workflow_state, "run_git")
    @patch.object(benchmark_workflow_state, "relevant_changes")
    @patch.object(
        benchmark_workflow_state.performance_evidence,
        "load_json",
    )
    @patch.object(
        benchmark_workflow_state.performance_evidence,
        "validate_baseline_file",
    )
    def test_non_ancestor_source_commit_invalidates_the_proposal(
        self,
        validate_baseline: Mock,
        load_json: Mock,
        changes: Mock,
        run_git: Mock,
    ) -> None:
        """Reject evidence that is detached from the current main history."""
        load_json.return_value = self.baseline
        run_git.side_effect = (
            subprocess.CompletedProcess([], 0, "source-commit\n", ""),
            subprocess.CompletedProcess([], 1, "", ""),
        )

        proposal = benchmark_workflow_state.inspect_proposal(
            self.repo,
            self.contract_path,
            self.contract,
            self.proposal_path,
            "origin/automation/performance-baseline-2026-08-07",
            "current-commit",
            "scorecard",
            "github-ubuntu-latest-x64",
        )

        self.assertEqual("invalid", proposal.disposition)
        self.assertIn("not an ancestor", proposal.reason)
        self.assertEqual(
            ("none", False, False),
            benchmark_workflow_state.decide_work("seed", "none", proposal),
        )
        validate_baseline.assert_called_once()
        changes.assert_not_called()

    @patch.object(
        benchmark_workflow_state.performance_evidence,
        "validate_baseline_file",
        side_effect=benchmark_workflow_state.performance_evidence.PerformanceEvidenceError(
            "invalid proposal",
        ),
    )
    def test_push_defers_invalid_proposal_until_explicit_refresh(
        self,
        validate_baseline: Mock,
    ) -> None:
        """Prevent corrupt review state from creating unbounded push cost."""
        proposal = benchmark_workflow_state.inspect_proposal(
            self.repo,
            self.contract_path,
            self.contract,
            self.proposal_path,
            "origin/automation/performance-baseline-2026-08-07",
            "current-commit",
            "scorecard",
            "github-ubuntu-latest-x64",
        )

        self.assertEqual("invalid", proposal.disposition)
        self.assertEqual(
            ("none", False, False),
            benchmark_workflow_state.decide_work("seed", "none", proposal),
        )
        validate_baseline.assert_called_once()

    def test_explicit_refresh_remeasures_invalid_seed_proposal(self) -> None:
        """Recover invalid seed evidence only on an authorized refresh."""
        proposal = benchmark_workflow_state.ProposalState(
            "invalid",
            "The proposal is invalid.",
        )

        self.assertEqual(
            ("scorecard", False, True),
            benchmark_workflow_state.decide_work(
                "seed",
                "scorecard",
                proposal,
            ),
        )

    def test_unrelated_push_does_not_seed_an_absent_proposal(self) -> None:
        """Prevent proposal state from overriding event relevance."""
        proposal = benchmark_workflow_state.ProposalState(
            "absent",
            "No open baseline proposal exists.",
        )

        self.assertEqual(
            ("none", False, False),
            benchmark_workflow_state.decide_work("seed", "none", proposal),
        )

    def test_compare_mode_only_runs_when_the_event_requires_evidence(
        self,
    ) -> None:
        """Preserve accepted baselines during unrelated main pushes."""
        proposal = benchmark_workflow_state.ProposalState(
            "absent",
            "No proposal is needed in compare mode.",
        )

        self.assertEqual(
            ("none", False, False),
            benchmark_workflow_state.decide_work(
                "compare",
                "none",
                proposal,
            ),
        )
        self.assertEqual(
            ("smoke", False, False),
            benchmark_workflow_state.decide_work(
                "compare",
                "smoke",
                proposal,
            ),
        )
        self.assertEqual(
            ("scorecard", False, False),
            benchmark_workflow_state.decide_work(
                "compare",
                "scorecard",
                proposal,
            ),
        )

    def test_compare_mode_never_synchronizes_a_seed_proposal(self) -> None:
        """Keep accepted-baseline proposal mutations exclusive to seed work."""
        proposal = benchmark_workflow_state.ProposalState(
            "current",
            "The seed proposal is valid but behind main.",
            behind_current=True,
        )

        self.assertEqual(
            ("none", False, False),
            benchmark_workflow_state.decide_work(
                "compare",
                "none",
                proposal,
            ),
        )

    def test_seed_upgrades_provider_smoke_to_complete_scorecard(self) -> None:
        """Permit baseline proposals only from the complete target contract."""
        proposal = benchmark_workflow_state.ProposalState(
            "absent",
            "The active contract has no accepted baseline.",
        )

        self.assertEqual(
            ("scorecard", False, True),
            benchmark_workflow_state.decide_work(
                "seed",
                "smoke",
                proposal,
            ),
        )

    @staticmethod
    def _git(
        repository: Path,
        *arguments: str,
    ) -> subprocess.CompletedProcess[str]:
        """Run Git with captured output for an isolated integration fixture."""
        return subprocess.run(
            ("git", *arguments),
            cwd=repository,
            check=True,
            capture_output=True,
            text=True,
        )


if __name__ == "__main__":
    unittest.main()
