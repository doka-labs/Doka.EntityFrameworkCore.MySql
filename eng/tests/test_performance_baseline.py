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

    def _other_contract_version(self) -> str:
        """Return a contract version that cannot equal the checked-in one.

        A literal date silently stops testing a rollover the moment the real
        contract is bumped to that same date, which turns a passing test into
        one that asserts the opposite of its name.
        """
        current = self.contract["contractVersion"]
        date, _, revision = current.partition(".")

        return f"{date}.{int(revision) + 1 if revision else 2}"

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
            performance_evidence.EnvironmentNotComparableError,
            "environment drift.*processorCount",
        ):
            performance_evidence.validate_environment_compatibility(
                current,
                baseline,
            )

    def test_seed_requires_every_engine_target(self) -> None:
        """Reject a baseline seed that omits any supported engine line."""
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

    def test_seed_accepts_one_run_identifier_per_measurement_job(self) -> None:
        """Accept matrix evidence whose targets were measured in separate jobs.

        The hosted matrix runs one job per target and names each job in the run
        identifier, so its evaluations cannot carry the same one. Binding
        promotion to that field made the gate unsatisfiable on the only path
        that produces release evidence.
        """
        with tempfile.TemporaryDirectory(prefix="doka-performance-jobs-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "github-runner")

            baseline = performance_evidence.seed_baseline(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(self._contract_path),
                        "evidence": [str(path) for path in paths],
                        "version": "test",
                        "accepted_utc": "2026-08-06T00:00:00Z",
                        "merge_existing": None,
                    },
                )()
            )

            # Each entry keeps the identifier of the job that measured it; the
            # value is provenance, not an identity the targets have to share.
            self.assertEqual(
                {
                    f"github-1000-{target}-attempt-1"
                    for target in self.contract["requiredTargets"]
                },
                {entry["runId"] for entry in baseline["baselines"]},
            )

    def test_seed_rejects_targets_measured_from_different_sources(self) -> None:
        """Reject evidence whose targets did not measure the same software."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-drift-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "github-runner")

            drifted = json.loads(paths[0].read_text(encoding="utf-8"))
            drifted["commit"] = "f" * 40
            paths[0].write_text(json.dumps(drifted), encoding="utf-8")

            with self.assertRaisesRegex(
                performance_evidence.PerformanceEvidenceError,
                "must share one profile, runner, commit, and source hash",
            ):
                performance_evidence.seed_baseline(
                    type(
                        "Args",
                        (),
                        {
                            "contract": str(self._contract_path),
                            "evidence": [str(path) for path in paths],
                            "version": "test",
                            "accepted_utc": "2026-08-06T00:00:00Z",
                            "merge_existing": None,
                        },
                    )()
                )

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

            self.assertEqual(
                2 * len(self.contract["requiredTargets"]),
                len(merged["baselines"]),
            )
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

            self.assertEqual(
                len(self.contract["requiredTargets"]),
                len(merged["baselines"]),
            )
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

    def test_contract_revision_produces_a_valid_replacement_baseline(self) -> None:
        """Close the contract-change path from seed selection through acceptance."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-transition-") as directory:
            root = Path(directory)
            old_paths = self._write_seed_evaluations(root, "github-runner")
            old_baseline_path = self._write_baseline(root, old_paths)

            revised_contract = copy.deepcopy(self.contract)
            revised_contract["contractVersion"] = self._other_contract_version()
            revised_contract_path = root / "revised-contract.json"
            revised_contract_path.write_text(
                json.dumps(revised_contract),
                encoding="utf-8",
            )

            revised_paths = []
            for target in revised_contract["requiredTargets"]:
                evaluation = self._evaluation(target)
                evaluation["contractVersion"] = revised_contract["contractVersion"]
                evaluation["runnerClass"] = "github-runner"
                evaluation["hostPreflight"]["contractVersion"] = revised_contract[
                    "contractVersion"
                ]
                evaluation["artifactHashes"]["contract"] = (
                    performance_evidence.sha256(revised_contract_path)
                )
                path = root / f"revised-{target}.json"
                path.write_text(json.dumps(evaluation), encoding="utf-8")
                revised_paths.append(path)

            candidate = performance_evidence.seed_baseline(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(revised_contract_path),
                        "evidence": [str(path) for path in revised_paths],
                        "version": "replacement",
                        "accepted_utc": "2026-08-09T00:00:00Z",
                        "merge_existing": str(old_baseline_path),
                    },
                )()
            )
            candidate_path = root / "candidate.json"
            candidate_path.write_text(json.dumps(candidate), encoding="utf-8")

            validation = performance_evidence.validate_baseline_file(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(revised_contract_path),
                        "baseline": str(candidate_path),
                    },
                )()
            )
            comparison = performance_evidence.compare_baseline_files(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(revised_contract_path),
                        "current": str(old_baseline_path),
                        "candidate": str(candidate_path),
                    },
                )()
            )

            self.assertTrue(validation["success"])
            self.assertEqual(
                len(revised_contract["requiredTargets"]),
                validation["targetCount"],
            )
            self.assertTrue(comparison["changed"])
            self.assertEqual(
                "contract-version-changed",
                comparison["disposition"],
            )
            self.assertEqual(
                self.contract["contractVersion"],
                comparison["currentContractVersion"],
            )
            self.assertEqual(
                revised_contract["contractVersion"],
                comparison["candidateContractVersion"],
            )

    def test_auto_baseline_mode_seeds_when_contract_changed(self) -> None:
        """Keep historical evidence bound to its original contract bytes."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-resolve-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "github-runner")
            baseline_path = self._write_baseline(root, paths)

            current_contract = json.loads(
                self._contract_path.read_text(encoding="utf-8"),
            )
            current_contract["contractVersion"] = self._other_contract_version()
            current_contract_path = root / "current-contract.json"
            current_contract_path.write_text(
                json.dumps(current_contract),
                encoding="utf-8",
            )

            resolved = performance_evidence.resolve_baseline_mode(
                type(
                    "Args",
                    (),
                    {
                        "contract": str(current_contract_path),
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

    def test_auto_baseline_mode_seeds_when_runner_matrix_is_missing(self) -> None:
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
            self.assertEqual("runner-matrix-missing", resolved["baselineDisposition"])

    def test_auto_baseline_mode_compares_an_accepted_runner_matrix(self) -> None:
        """Compare only when the current contract has the complete runner matrix."""
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
                "accepted-runner-matrix",
                resolved["baselineDisposition"],
            )

    def test_explicit_compare_rejects_a_missing_runner_matrix(self) -> None:
        """Fail a strict comparison before allocating the benchmark matrix."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-resolve-") as directory:
            root = Path(directory)
            paths = self._write_seed_evaluations(root, "local-runner")
            baseline_path = self._write_baseline(root, paths)

            with self.assertRaisesRegex(
                performance_evidence.PerformanceEvidenceError,
                "requires a current accepted baseline matrix",
            ):
                self._resolve_baseline_mode(
                    baseline_path,
                    "github-runner",
                    "compare",
                )


if __name__ == "__main__":
    unittest.main()
