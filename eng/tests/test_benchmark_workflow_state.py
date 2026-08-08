"""Tests for the hosted benchmark workflow control plane."""

from __future__ import annotations

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
                    ".github/workflows/benchmark-scorecard.yml",
                    ".github/workflows/benchmark.yml",
                    "eng/performance/workflow_state.py",
                    "eng/performance/check-benchmark-ratios.sh",
                    "eng/performance/cli.py",
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
                required, changes = (
                    benchmark_workflow_state.event_requires_scorecard(
                        self.repo,
                        event_name,
                        None,
                        "current-commit",
                    )
                )

                self.assertTrue(required)
                self.assertEqual((f"<{event_name}>",), changes)

    @patch.object(benchmark_workflow_state, "relevant_changes")
    def test_push_runs_only_for_changed_performance_inputs(
        self,
        changes: Mock,
    ) -> None:
        """Measure relevant pushes and keep unrelated pushes inexpensive."""
        cases = (
            ((), False),
            (("src/Doka.EntityFrameworkCore.MySql/Storage/Mapping.cs",), True),
        )
        for observed_changes, expected in cases:
            with self.subTest(changes=observed_changes, expected=expected):
                changes.return_value = observed_changes

                required, observed = (
                    benchmark_workflow_state.event_requires_scorecard(
                        self.repo,
                        "push",
                        "before-commit",
                        "current-commit",
                    )
                )

                self.assertEqual(expected, required)
                self.assertEqual(observed_changes, observed)

        self.assertEqual(2, changes.call_count)

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

            documentation_required, documentation_changes = (
                benchmark_workflow_state.event_requires_scorecard(
                    repository,
                    "push",
                    initial_revision,
                    documentation_revision,
                )
            )

            self.assertFalse(documentation_required)
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

            provider_required, provider_changes = (
                benchmark_workflow_state.event_requires_scorecard(
                    repository,
                    "push",
                    documentation_revision,
                    provider_revision,
                )
            )

            self.assertTrue(provider_required)
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

            moved_required, moved_changes = (
                benchmark_workflow_state.event_requires_scorecard(
                    repository,
                    "push",
                    provider_revision,
                    moved_revision,
                )
            )

            self.assertTrue(moved_required)
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

            deleted_required, deleted_changes = (
                benchmark_workflow_state.event_requires_scorecard(
                    repository,
                    "push",
                    added_revision,
                    deleted_revision,
                )
            )

            self.assertTrue(deleted_required)
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
        required, changes = benchmark_workflow_state.event_requires_scorecard(
            self.repo,
            "push",
            benchmark_workflow_state.ZERO_REVISION,
            "current-commit",
        )

        self.assertTrue(required)
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
        scorecard_required, sync_required = (
            benchmark_workflow_state.decide_work(
                "seed",
                False,
                proposal,
            )
        )

        validate_baseline.assert_called_once()
        self.assertEqual("current", proposal.disposition)
        self.assertTrue(proposal.behind_current)
        self.assertFalse(scorecard_required)
        self.assertTrue(sync_required)

    def test_scheduled_seed_refreshes_a_current_proposal(self) -> None:
        """Refresh evidence when the event explicitly requests measurement."""
        proposal = benchmark_workflow_state.ProposalState(
            disposition="current",
            reason="Current proposal.",
            behind_current=False,
        )

        scorecard_required, sync_required = (
            benchmark_workflow_state.decide_work(
                "seed",
                True,
                proposal,
            )
        )

        self.assertTrue(scorecard_required)
        self.assertFalse(sync_required)

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
            (False, False),
            benchmark_workflow_state.decide_work("seed", False, proposal),
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
        scorecard_required, sync_required = (
            benchmark_workflow_state.decide_work(
                "seed",
                True,
                proposal,
            )
        )

        validate_baseline.assert_called_once()
        self.assertEqual("stale", proposal.disposition)
        self.assertEqual(("eng/benchmark.sh",), proposal.relevant_changes)
        self.assertTrue(scorecard_required)
        self.assertFalse(sync_required)

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
            (False, False),
            benchmark_workflow_state.decide_work("seed", False, proposal),
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
            (False, False),
            benchmark_workflow_state.decide_work("seed", False, proposal),
        )
        validate_baseline.assert_called_once()

    def test_explicit_refresh_remeasures_invalid_seed_proposal(self) -> None:
        """Recover invalid seed evidence only on an authorized refresh."""
        proposal = benchmark_workflow_state.ProposalState(
            "invalid",
            "The proposal is invalid.",
        )

        self.assertEqual(
            (True, False),
            benchmark_workflow_state.decide_work("seed", True, proposal),
        )

    def test_unrelated_push_does_not_seed_an_absent_proposal(self) -> None:
        """Prevent proposal state from overriding event relevance."""
        proposal = benchmark_workflow_state.ProposalState(
            "absent",
            "No open baseline proposal exists.",
        )

        self.assertEqual(
            (False, False),
            benchmark_workflow_state.decide_work("seed", False, proposal),
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
            (False, False),
            benchmark_workflow_state.decide_work(
                "compare",
                False,
                proposal,
            ),
        )
        self.assertEqual(
            (True, False),
            benchmark_workflow_state.decide_work(
                "compare",
                True,
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
