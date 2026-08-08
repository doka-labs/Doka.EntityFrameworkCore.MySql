"""Historical comparison and baseline lifecycle tests."""

from __future__ import annotations

import copy
import json
import tempfile
import unittest
from pathlib import Path

from eng.performance import cli as performance_evidence
from eng.tests._performance_fixtures import PerformanceEvidenceFixtureMixin


class PerformanceBaselineTests(PerformanceEvidenceFixtureMixin, unittest.TestCase):
    """Verify historical normalization, seed, promotion, and mode resolution."""

    def test_historical_gate_rejects_regression_against_matching_runner(self) -> None:
        """Reject a regression above both the ratio and absolute allowance."""
        report = self._workload_report("mysql84")
        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        baseline_entry = {
            "workloads": copy.deepcopy(workloads),
        }
        workloads[0]["allocatedBytesPerOperation"] = 2000

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "Historical budget failed",
        ):
            performance_evidence.validate_historical_budgets(
                workloads,
                baseline_entry,
                self.contract,
            )

    def test_historical_gate_normalizes_matching_background_slowdown(self) -> None:
        """Accept host slowdown only when the adjacent control slows equally."""
        baseline_report = self._workload_report("mysql84")
        baseline_workloads = performance_evidence.validate_workload_report(
            baseline_report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        current_report = copy.deepcopy(baseline_report)

        for entry in current_report["workloads"]:
            entry["samplesNanoseconds"] = [
                sample * 2
                for sample in entry["samplesNanoseconds"]
            ]
            entry["calibrationNanoseconds"] = [
                sample * 2
                for sample in entry["calibrationNanoseconds"]
            ]
            entry["calibrationPulseNanoseconds"] = [
                sample * 2
                for sample in entry["calibrationPulseNanoseconds"]
            ]
            entry["medianNanoseconds"] *= 2
            entry["p95Nanoseconds"] *= 2
            entry["p99Nanoseconds"] *= 2
            entry["standardErrorNanoseconds"] *= 2
            entry["calibrationMedianNanoseconds"] *= 2
            entry["calibrationStandardErrorNanoseconds"] *= 2

        current_workloads = performance_evidence.validate_workload_report(
            current_report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )

        checks = performance_evidence.validate_historical_budgets(
            current_workloads,
            {"workloads": baseline_workloads},
            self.contract,
        )

        self.assertTrue(all(check["passed"] for check in checks))
        self.assertGreater(
            current_workloads[0]["p99Nanoseconds"],
            baseline_workloads[0]["p99Nanoseconds"],
        )
        self.assertEqual(
            current_workloads[0]["normalizedP99"],
            baseline_workloads[0]["normalizedP99"],
        )

    def test_historical_gate_rejects_provider_only_slowdown(self) -> None:
        """Reject latency growth when its adjacent control remains stable."""
        baseline_report = self._workload_report("mysql84")
        baseline_workloads = performance_evidence.validate_workload_report(
            baseline_report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        current_report = copy.deepcopy(baseline_report)
        entry = current_report["workloads"][0]
        self._replace_workload_samples(
            entry,
            [sample * 2 for sample in entry["samplesNanoseconds"]],
        )
        current_workloads = performance_evidence.validate_workload_report(
            current_report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "independent confirmations",
        ):
            performance_evidence.validate_historical_budgets(
                current_workloads,
                {"workloads": baseline_workloads},
                self.contract,
            )

    def test_retained_heap_delta_is_not_a_hard_workload_budget(self) -> None:
        """Leave retained-memory verdicts to the sustained resource invariants."""
        report = self._workload_report("mysql84")
        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        baseline_entry = {"workloads": copy.deepcopy(workloads)}
        workloads[0]["retainedBytes"] = 1024**4

        absolute_checks = performance_evidence.validate_absolute_budgets(
            workloads,
            self.contract,
        )
        historical_checks = performance_evidence.validate_historical_budgets(
            workloads,
            baseline_entry,
            self.contract,
        )

        self.assertNotIn(
            "retainedBytes",
            {check["metric"] for check in absolute_checks},
        )
        self.assertNotIn(
            "retainedBytes",
            {check["metric"] for check in historical_checks},
        )

    def test_historical_gate_rejects_environment_drift(self) -> None:
        """Reject a matching runner label when its measured hardware has changed."""
        current = self._workload_report("mysql84")["environment"]
        baseline = copy.deepcopy(current)
        baseline["processorCount"] += 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "environment drift.*processorCount",
        ):
            performance_evidence.validate_environment_compatibility(
                current,
                baseline,
            )

    def test_seed_requires_both_engine_targets(self) -> None:
        """Reject a baseline seed that could hide one representative engine family."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-seed-") as directory:
            root = Path(directory)
            evidence_path = root / "mysql84.json"
            evidence_path.write_text(
                json.dumps(self._evaluation("mysql84")),
                encoding="utf-8",
            )
            args = type(
                "Args",
                (),
                {
                    "contract": str(self._contract_path),
                    "evidence": [str(evidence_path)],
                    "version": "test",
                    "accepted_utc": "2026-07-30T00:00:00Z",
                },
            )()

            with self.assertRaisesRegex(
                performance_evidence.PerformanceEvidenceError,
                "Baseline seed target drift",
            ):
                performance_evidence.seed_baseline(args)

    def test_seed_merges_a_new_runner_without_dropping_an_accepted_runner(self) -> None:
        """Retain complete runner groups while replacing only matching tuples."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-merge-") as directory:
            root = Path(directory)
            local_paths = self._write_seed_evaluations(root, "local-runner")
            local = performance_evidence.seed_baseline(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(self._contract_path),
                        "evidence": [str(path) for path in local_paths],
                        "version": "local",
                        "accepted_utc": "2026-07-30T00:00:00Z",
                        "merge_existing": None,
                    },
                )()
            )
            existing_path = root / "existing.json"
            existing_path.write_text(json.dumps(local), encoding="utf-8")

            github_paths = self._write_seed_evaluations(root, "github-runner")
            merged = performance_evidence.seed_baseline(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(self._contract_path),
                        "evidence": [str(path) for path in github_paths],
                        "version": "combined",
                        "accepted_utc": "2026-07-30T01:00:00Z",
                        "merge_existing": str(existing_path),
                    },
                )()
            )

            self.assertEqual(4, len(merged["baselines"]))
            self.assertEqual(
                {"local-runner", "github-runner"},
                {entry["runnerClass"] for entry in merged["baselines"]},
            )

    def test_seed_drops_entries_accepted_under_an_older_contract(self) -> None:
        """Prevent one accepted baseline from mixing incompatible evidence semantics."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-contract-") as directory:
            root = Path(directory)
            local_paths = self._write_seed_evaluations(root, "local-runner")
            existing = performance_evidence.seed_baseline(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(self._contract_path),
                        "evidence": [str(path) for path in local_paths],
                        "version": "local",
                        "accepted_utc": "2026-08-06T00:00:00Z",
                        "merge_existing": None,
                    },
                )()
            )
            existing["contractVersion"] = "older-contract"
            existing_path = root / "existing.json"
            existing_path.write_text(json.dumps(existing), encoding="utf-8")

            github_paths = self._write_seed_evaluations(root, "github-runner")
            merged = performance_evidence.seed_baseline(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(self._contract_path),
                        "evidence": [str(path) for path in github_paths],
                        "version": "github",
                        "accepted_utc": "2026-08-06T01:00:00Z",
                        "merge_existing": str(existing_path),
                    },
                )()
            )

            self.assertEqual(2, len(merged["baselines"]))
            self.assertEqual(
                {"github-runner"},
                {entry["runnerClass"] for entry in merged["baselines"]},
            )

    def test_baseline_comparison_ignores_provenance_only_drift(self) -> None:
        """Keep rerun metadata out of the reviewed acceptance contract."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-compare-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "github-runner")
            current_path = self._write_baseline(root, paths)
            candidate = json.loads(current_path.read_text(encoding="utf-8"))
            candidate["acceptedUtc"] = "2026-08-07T00:00:00Z"
            candidate["baselineVersion"] = "rerun"

            for entry in candidate["baselines"]:
                entry["commit"] = "f" * 40
                entry["runId"] = "replacement-run"

            candidate_path = root / "candidate.json"
            candidate_path.write_text(json.dumps(candidate), encoding="utf-8")
            result = performance_evidence.compare_baseline_files(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(self._contract_path),
                        "current": str(current_path),
                        "candidate": str(candidate_path),
                    },
                )()
            )

            self.assertFalse(result["changed"])
            self.assertEqual("provenance-only", result["disposition"])

    def test_baseline_comparison_detects_environment_contract_change(self) -> None:
        """Require review when stable execution-environment identity changes."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-compare-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "github-runner")
            current_path = self._write_baseline(root, paths)
            candidate = json.loads(current_path.read_text(encoding="utf-8"))
            candidate["baselines"][0]["environment"]["processor"] = "replacement-cpu"
            candidate["baselines"][0]["hostPreflight"]["processor"] = "replacement-cpu"
            candidate["baselines"][0]["benchmarkDotNetHostEnvironment"][
                "ProcessorName"
            ] = "replacement-cpu"

            candidate_path = root / "candidate.json"
            candidate_path.write_text(json.dumps(candidate), encoding="utf-8")
            result = performance_evidence.compare_baseline_files(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(self._contract_path),
                        "current": str(current_path),
                        "candidate": str(candidate_path),
                    },
                )()
            )

            self.assertTrue(result["changed"])
            self.assertEqual("contract-changed", result["disposition"])

    def test_auto_baseline_mode_seeds_when_contract_changed(self) -> None:
        """Seed a candidate instead of comparing incompatible evidence semantics."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-resolve-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "github-runner")
            baseline = performance_evidence.seed_baseline(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(self._contract_path),
                        "evidence": [str(path) for path in paths],
                        "version": "github",
                        "accepted_utc": "2026-08-06T00:00:00Z",
                        "merge_existing": None,
                    },
                )()
            )
            baseline["contractVersion"] = "older-contract"
            baseline_path = root / "baseline.json"
            baseline_path.write_text(json.dumps(baseline), encoding="utf-8")

            resolved = performance_evidence.resolve_baseline_mode(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(self._contract_path),
                        "baseline": str(baseline_path),
                        "profile": "scorecard",
                        "runner_class": "github-runner",
                        "requested_mode": "auto",
                    },
                )()
            )

            self.assertEqual("seed", resolved["mode"])
            self.assertEqual(
                "contract-version-mismatch",
                resolved["baselineDisposition"],
            )

    def test_auto_baseline_mode_seeds_when_runner_pair_is_missing(self) -> None:
        """Seed a complete candidate for a runner not represented by the baseline."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-resolve-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "local-runner")
            baseline_path = self._write_baseline(root, paths)

            resolved = self._resolve_baseline_mode(
                baseline_path,
                "github-runner",
                "auto",
            )

            self.assertEqual("seed", resolved["mode"])
            self.assertEqual("runner-pair-missing", resolved["baselineDisposition"])

    def test_auto_baseline_mode_compares_an_accepted_runner_pair(self) -> None:
        """Compare only when the current contract has the complete runner pair."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-resolve-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "github-runner")
            baseline_path = self._write_baseline(root, paths)

            resolved = self._resolve_baseline_mode(
                baseline_path,
                "github-runner",
                "auto",
            )

            self.assertEqual("compare", resolved["mode"])
            self.assertEqual(
                "accepted-runner-pair",
                resolved["baselineDisposition"],
            )

    def test_explicit_compare_rejects_a_missing_runner_pair(self) -> None:
        """Fail a strict comparison before allocating the benchmark matrix."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-resolve-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "local-runner")
            baseline_path = self._write_baseline(root, paths)

            with self.assertRaisesRegex(
                performance_evidence.PerformanceEvidenceError,
                "requires a current accepted baseline pair",
            ):
                self._resolve_baseline_mode(
                    baseline_path,
                    "github-runner",
                    "compare",
                )


if __name__ == "__main__":
    unittest.main()
