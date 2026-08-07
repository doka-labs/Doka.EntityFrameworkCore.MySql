"""Tests for the hosted benchmark workflow control plane."""

from __future__ import annotations

import subprocess
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from eng import benchmark_workflow_state


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

    def test_performance_inputs_exclude_generated_and_provider_outputs(
        self,
    ) -> None:
        """Refresh evidence for harness changes, not for measured outputs."""
        included = (
            ".github/workflows/benchmark-scorecard.yml",
            "benchmarks/performance-contract.json",
            "benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks/Program.cs",
            "global.json",
        )
        excluded = (
            ".github/workflows/benchmark.yml",
            "benchmarks/baselines/doka-benchmark-baseline.json",
            "eng/benchmark_workflow_state.py",
            "src/Doka.EntityFrameworkCore.MySql/Storage/MySqlTypeMapping.cs",
            "docs/operations/performance-evidence.md",
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
                    ".github/workflows/benchmark.yml",
                    "eng/benchmark_workflow_state.py",
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
    def test_push_only_refreshes_changed_performance_inputs(
        self,
        changes: Mock,
    ) -> None:
        """Keep unrelated main pushes on the cheap resolver path."""
        changes.return_value = ()

        required, observed = benchmark_workflow_state.event_requires_scorecard(
            self.repo,
            "push",
            "before-commit",
            "current-commit",
        )

        self.assertFalse(required)
        self.assertEqual((), observed)
        changes.assert_called_once_with(
            self.repo,
            "before-commit",
            "current-commit",
        )

    @patch.object(benchmark_workflow_state, "run_git")
    def test_initial_push_requires_fresh_evidence(
        self,
        run_git: Mock,
    ) -> None:
        """Treat a newly created main branch as an explicit measurement edge."""
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
            (True, False),
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
    def test_invalid_proposal_is_remeasured(
        self,
        validate_baseline: Mock,
    ) -> None:
        """Recover from corrupt review state with fresh validated evidence."""
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
            (True, False),
            benchmark_workflow_state.decide_work("seed", False, proposal),
        )
        validate_baseline.assert_called_once()

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


if __name__ == "__main__":
    unittest.main()
