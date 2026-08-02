"""Regression tests for performance, baseline, and soak evidence gates."""

from __future__ import annotations

import copy
import importlib.util
import json
import subprocess
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from types import ModuleType
from typing import Any


def load_module() -> ModuleType:
    """Load the repository script without requiring eng to be a Python package."""
    script = Path(__file__).resolve().parents[1] / "performance_evidence.py"
    spec = importlib.util.spec_from_file_location("performance_evidence", script)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {script}.")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


performance_evidence = load_module()


class PerformanceEvidenceTests(unittest.TestCase):
    """Exercise complete evidence and every material hard-failure class."""

    _repo_root = Path(__file__).resolve().parents[2]
    _contract_path = _repo_root / "benchmarks" / "performance-contract.json"

    def setUp(self) -> None:
        """Load the versioned contract shared by every evidence fixture."""
        self.contract = performance_evidence.load_json(self._contract_path)

    def test_contract_covers_every_declared_matrix_dimension(self) -> None:
        """Accept the checked-in contract only when every coverage token has a workload."""
        performance_evidence.validate_contract(self.contract)

    def test_source_hash_tracks_code_but_excludes_the_generated_baseline(self) -> None:
        """Bind evidence to dirty source without making baseline generation self-referential."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-source-") as directory:
            repository = Path(directory)
            baseline = repository / "benchmarks" / "baselines" / "doka-benchmark-baseline.json"
            source = repository / "source.txt"
            baseline.parent.mkdir(parents=True)
            source.write_text("initial\n", encoding="utf-8")
            baseline.write_text("{}\n", encoding="utf-8")
            subprocess.run(["git", "init", "-q", str(repository)], check=True)
            subprocess.run(["git", "-C", str(repository), "add", "."], check=True)
            subprocess.run(
                [
                    "git",
                    "-C",
                    str(repository),
                    "-c",
                    "user.name=Performance test",
                    "-c",
                    "user.email=performance@example.invalid",
                    "-c",
                    "commit.gpgsign=false",
                    "commit",
                    "-qm",
                    "fixture",
                ],
                check=True,
            )

            clean_hash = performance_evidence.repository_source_hash(repository)
            baseline.write_text('{"baseline": true}\n', encoding="utf-8")
            self.assertEqual(clean_hash, performance_evidence.repository_source_hash(repository))

            source.write_text("changed\n", encoding="utf-8")
            self.assertNotEqual(clean_hash, performance_evidence.repository_source_hash(repository))

    def test_workload_report_recomputes_tail_statistics_and_complete_matrix(self) -> None:
        """Accept exact scorecard cells with independently recomputable statistics."""
        report = self._workload_report("mysql84")
        first_samples = sorted(report["workloads"][0]["samplesNanoseconds"])

        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )

        self.assertEqual(len(self.contract["workloads"]), len(workloads))
        self.assertEqual(
            performance_evidence.percentile(first_samples, 0.95),
            workloads[0]["p95Nanoseconds"],
        )
        self.assertEqual(
            performance_evidence.percentile(first_samples, 0.99),
            workloads[0]["p99Nanoseconds"],
        )

    def test_workload_report_rejects_missing_matrix_cell(self) -> None:
        """Reject one missing workload even when every remaining statistic is valid."""
        report = self._workload_report("mysql84")
        report["workloads"].pop()

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "Workload matrix drift",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_diagnostic_workload_report_cannot_satisfy_release_evidence(self) -> None:
        """Keep targeted root-cause measurements outside the release contract."""
        report = self._workload_report("mysql84")
        report["kind"] = "performance-workload-diagnostic"
        report["workloads"] = report["workloads"][:1]

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "schema or kind",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_host_preflight_rejects_an_overloaded_runner(self) -> None:
        """Reject a finite but non-quiescent host before latency is measured."""
        report = self._host_preflight()
        report["loadAverage1Minute"] = 3
        report["loadAverage1MinutePerProcessor"] = 0.375
        report["success"] = False

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "not quiescent",
        ):
            performance_evidence.validate_host_preflight(
                report,
                self.contract,
                maximum_age_hours=12,
            )

    def test_workload_report_rejects_operations_per_sample_drift(self) -> None:
        """Reject evidence that normalizes a workload with an unapproved batch size."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["operationsPerSample"] += 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "operationsPerSample",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_warmup_count_drift(self) -> None:
        """Reject a report produced with fewer warmups than the active profile."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["warmupSamples"] -= 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "warmups",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_sample_count_drift(self) -> None:
        """Reject a report produced with fewer samples than the active profile."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["sampleCount"] -= 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "samples",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_wrong_server_image(self) -> None:
        """Reject evidence whose observed container image differs from the contract."""
        report = self._workload_report("mysql84")
        report["environment"]["serverImage"] = "mysql:8.4@sha256:" + ("0" * 64)

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "serverImage",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_wrong_observed_engine_version(self) -> None:
        """Reject a target label backed by a different database engine."""
        report = self._workload_report("mysql84")
        report["environment"]["serverVersion"] = "11.8.8-MariaDB"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "serverVersion",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_non_finite_sample(self) -> None:
        """Reject NaN before it can produce a misleading successful summary."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["samplesNanoseconds"][0] = float("nan")

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "must be finite",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_excessive_measurement_noise(self) -> None:
        """Reject a statistically unstable workload even when every sample is finite."""
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        samples = [1.0] * (entry["sampleCount"] - 1) + [10000.0]
        entry["samplesNanoseconds"] = samples
        entry["medianNanoseconds"] = performance_evidence.percentile(sorted(samples), 0.5)
        entry["p95Nanoseconds"] = performance_evidence.percentile(sorted(samples), 0.95)
        entry["p99Nanoseconds"] = performance_evidence.percentile(sorted(samples), 0.99)
        entry["standardErrorNanoseconds"] = performance_evidence.standard_error(samples)

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "relative standard error",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_absolute_budget_rejects_latency_regression(self) -> None:
        """Reject a current measurement beyond its family's absolute p99 ceiling."""
        report = self._workload_report("mysql84")
        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        workloads[0]["p99Nanoseconds"] = 1000000000000

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "Absolute budget failed",
        ):
            performance_evidence.validate_absolute_budgets(workloads, self.contract)

    def test_soak_report_rejects_resource_failure(self) -> None:
        """Reject a failed cleanup invariant even when all scenario rows are present."""
        report = self._soak_report("mysql84")
        report["success"] = False
        report["scenarios"][1]["success"] = False
        report["scenarios"][1]["error"] = "buffer remained rented"
        report["scenarios"][1]["metrics"]["outstandingBuffers"] = 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "Soak gate failed",
        ):
            performance_evidence.validate_soak_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_soak_report_rejects_a_budget_that_does_not_match_the_contract(self) -> None:
        """Reject a report that weakens its own working-set ceiling."""
        report = self._soak_report("mysql84")
        report["scenarios"][4]["budgets"]["maximumWorkingSetGrowthBytes"] += 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "expected",
        ):
            performance_evidence.validate_soak_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_benchmarkdotnet_reports_require_statistics_memory_and_controls(self) -> None:
        """Accept a current-run report only with complete BDN controls and memory data."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-bdn-") as directory:
            reports = Path(directory) / "reports" / "run-1"
            result_directory = reports / "results"
            result_directory.mkdir(parents=True)
            report_path = result_directory / "Doka.Benchmarks-report-full.json"
            report_path.write_text(
                json.dumps(self._bdn_report()),
                encoding="utf-8",
            )

            evidence = performance_evidence.validate_bdn_reports(
                self.contract,
                reports,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

            self.assertTrue(evidence["success"])
            self.assertEqual(4, len(evidence["controls"]))
            self.assertEqual(1, len(evidence["rawReports"]))

    def test_benchmarkdotnet_reports_reject_missing_statistics(self) -> None:
        """Reject a benchmark error row represented by absent statistics."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-bdn-") as directory:
            reports = Path(directory) / "reports" / "run-1"
            result_directory = reports / "results"
            result_directory.mkdir(parents=True)
            payload = self._bdn_report()
            payload["Benchmarks"][0]["Statistics"] = None
            (result_directory / "Doka.Benchmarks-report-full.json").write_text(
                json.dumps(payload),
                encoding="utf-8",
            )

            with self.assertRaisesRegex(
                performance_evidence.PerformanceEvidenceError,
                "has no statistics",
            ):
                performance_evidence.validate_bdn_reports(
                    self.contract,
                    reports,
                    run_id="run-1",
                    target="mysql84",
                    profile="scorecard",
                )

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

    def test_benchmarkdotnet_host_must_match_workload_processor(self) -> None:
        """Reject same-run controls captured under a different host identity."""
        environment = self._workload_report("mysql84")["environment"]
        bdn_host = self._bdn_report()["HostEnvironmentInfo"]
        bdn_host["ProcessorName"] = "different CPU"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "different processors",
        ):
            performance_evidence.validate_bdn_workload_environment(
                bdn_host,
                environment,
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

    def _workload_report(self, target: str) -> dict[str, Any]:
        """Build a complete scorecard workload report for one target."""
        maximum_host_load = self.contract["hostPreconditions"][
            "maximumOneMinuteLoadAveragePerProcessor"
        ]
        workloads = []
        profile = self.contract["profiles"]["scorecard"]

        for definition in self.contract["workloads"]:
            sample_count = (
                profile["expensiveMeasurementSamples"]
                if definition.get("cost", "standard") == "expensive"
                else profile["measurementSamples"]
            )
            samples = [float(100 + (index * 10)) for index in range(sample_count)]
            sorted_samples = sorted(samples)
            workloads.append(
                {
                    "id": definition["id"],
                    "family": definition["family"],
                    "warmupSamples": 5,
                    "sampleCount": len(samples),
                    "operationsPerSample": definition.get("operationsPerSample", 1),
                    "checksum": 1,
                    "medianNanoseconds": performance_evidence.percentile(sorted_samples, 0.5),
                    "p95Nanoseconds": performance_evidence.percentile(sorted_samples, 0.95),
                    "p99Nanoseconds": performance_evidence.percentile(sorted_samples, 0.99),
                    "standardErrorNanoseconds": performance_evidence.standard_error(samples),
                    "allocatedBytesPerOperation": 1000,
                    "retainedBytes": 0,
                    "gen0CollectionsPer1000": 0,
                    "gen1CollectionsPer1000": 0,
                    "gen2CollectionsPer1000": 0,
                    "samplesNanoseconds": samples,
                }
            )

        return {
            "schemaVersion": 2,
            "kind": "performance-workloads",
            "contractVersion": self.contract["contractVersion"],
            "runId": "run-1",
            "target": target,
            "profile": "scorecard",
            "commit": "0" * 40,
            "sourceHash": "a" * 64,
            "runnerClass": "test-runner",
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "stopwatchFrequency": 1000000000,
            "environment": {
                "frameworkDescription": ".NET 10",
                "osDescription": "test",
                "osArchitecture": "X64",
                "processArchitecture": "X64",
                "processor": "test CPU",
                "processorCount": 8,
                "hostLoadAverage1Minute": 0.5,
                "hostLoadAverage5Minutes": 0.5,
                "hostLoadAverage15Minutes": 0.5,
                "hostLoadAverage1MinutePerProcessor": 0.0625,
                "maximumHostLoadAverage1MinutePerProcessor": maximum_host_load,
                "engineFamily": self.contract["requiredTargets"][target]["engineFamily"],
                "serverVersion": (
                    "11.8.8-MariaDB"
                    if target == "mariadb118"
                    else "8.4.10"
                ),
                "serverImage": self.contract["requiredTargets"][target]["serverImage"],
            },
            "workloads": workloads,
        }

    def _host_preflight(self) -> dict[str, Any]:
        """Build a passing host-preflight fixture bound to the test CPU."""
        maximum_host_load = self.contract["hostPreconditions"][
            "maximumOneMinuteLoadAveragePerProcessor"
        ]

        return {
            "schemaVersion": 1,
            "kind": "performance-host-preflight",
            "contractVersion": self.contract["contractVersion"],
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "processor": "test CPU",
            "processorCount": 8,
            "loadAverage1Minute": 0.5,
            "loadAverage5Minutes": 0.5,
            "loadAverage15Minutes": 0.5,
            "loadAverage1MinutePerProcessor": 0.0625,
            "maximumLoadAverage1MinutePerProcessor": maximum_host_load,
            "success": True,
        }

    def _soak_report(self, target: str) -> dict[str, Any]:
        """Build a complete passing soak report for one target."""
        budgets = self.contract["soakBudgets"]
        return {
            "schemaVersion": 2,
            "kind": "performance-soak",
            "contractVersion": self.contract["contractVersion"],
            "runId": "run-1",
            "target": target,
            "profile": "scorecard",
            "commit": "0" * 40,
            "sourceHash": "a" * 64,
            "runnerClass": "test-runner",
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "success": True,
            "scenarios": [
                {
                    "id": "soak.hilo-cache-bound",
                    "success": True,
                    "metrics": {"cacheEntries": 0},
                    "budgets": {
                        "maximumCacheEntries": budgets["hiloCacheMaximumEntries"],
                    },
                },
                {
                    "id": "soak.pooled-buffer-return",
                    "success": True,
                    "metrics": {
                        "rentCount": 1,
                        "returnCount": 1,
                        "outstandingBuffers": 0,
                    },
                    "budgets": {
                        "maximumOutstandingBuffers": budgets[
                            "pooledBufferMaximumOutstanding"
                        ],
                    },
                },
                {
                    "id": "soak.connection-cleanup",
                    "success": True,
                    "metrics": {
                        "threadsConnectedBefore": 1,
                        "threadsConnectedAfter": 1,
                        "connectionDelta": 0,
                    },
                    "budgets": {
                        "maximumConnectionDelta": budgets["connectionMaximumDelta"],
                    },
                },
                {
                    "id": "soak.migration-lock-cleanup",
                    "success": True,
                    "metrics": {"heldLocks": 0},
                    "budgets": {
                        "maximumHeldLocks": budgets["migrationLockMaximumHeld"],
                    },
                },
                {
                    "id": "soak.working-set-stabilization",
                    "success": True,
                    "metrics": {
                        "workingSetFirstBytes": 100,
                        "workingSetLastBytes": 100,
                        "workingSetGrowthBytes": 0,
                        "managedHeapFirstBytes": 100,
                        "managedHeapLastBytes": 100,
                        "managedHeapGrowthBytes": 0,
                    },
                    "budgets": {
                        "maximumWorkingSetGrowthBytes": budgets[
                            "workingSetMaximumGrowthBytes"
                        ],
                        "maximumManagedHeapGrowthBytes": budgets[
                            "managedHeapMaximumGrowthBytes"
                        ],
                    },
                },
                {
                    "id": "soak.concurrent-throughput-retention",
                    "success": True,
                    "metrics": {
                        "initialOperationsPerSecond": 100,
                        "finalOperationsPerSecond": 100,
                        "throughputRetentionRatio": 1,
                    },
                    "budgets": {
                        "minimumThroughputRetentionRatio": budgets[
                            "minimumThroughputRetentionRatio"
                        ],
                    },
                },
            ],
        }

    @staticmethod
    def _bdn_report() -> dict[str, Any]:
        """Build raw BenchmarkDotNet controls with stable sample statistics."""

        def benchmark(type_name: str, method: str, mean: float, allocated: int) -> dict[str, Any]:
            """Build one raw BenchmarkDotNet benchmark result."""
            values = [mean - 1, mean, mean + 1]
            return {
                "Type": type_name,
                "Method": method,
                "Statistics": {
                    "OriginalValues": values,
                    "N": 3,
                    "Mean": mean,
                    "Median": mean,
                    "StandardError": 1,
                    "Percentiles": {"P95": mean + 1},
                },
                "Memory": {
                    "Gen0Collections": 0,
                    "Gen1Collections": 0,
                    "Gen2Collections": 0,
                    "TotalOperations": 1,
                    "BytesAllocatedPerOperation": allocated,
                },
            }

        return {
            "HostEnvironmentInfo": {
                "BenchmarkDotNetVersion": "0.15.8",
                "OsVersion": "test",
                "ProcessorName": "test CPU",
                "RuntimeVersion": ".NET 10",
                "Architecture": "X64",
                "Configuration": "RELEASE",
            },
            "Benchmarks": [
                benchmark("IdentifierQuotingBenchmark", "NaiveDelimitStringPlain", 100, 100),
                benchmark("IdentifierQuotingBenchmark", "DelimitStringPlain", 50, 50),
                benchmark("BulkInsertBenchmark", "PerRowSaveChanges", 300, 1000),
                benchmark("BulkInsertBenchmark", "MultiRowAddRangeSaveChanges", 90, 500),
                benchmark("JsonComparerBenchmark", "NaiveJsonElementEqualsLoop", 100, 1000),
                benchmark("JsonComparerBenchmark", "JsonElementEqualsLoop", 100, 100),
                benchmark(
                    "QueryTranslationBenchmarks",
                    "TranslateRepresentativeCorpus",
                    100,
                    140000,
                ),
            ],
        }

    def _evaluation(self, target: str) -> dict[str, Any]:
        """Evaluate a complete fixture into the baseline seed representation."""
        report = self._workload_report(target)
        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target=target,
            profile="scorecard",
        )
        soak_scenarios = performance_evidence.validate_soak_report(
            self._soak_report(target),
            self.contract,
            run_id="run-1",
            target=target,
            profile="scorecard",
        )
        return {
            "schemaVersion": 2,
            "kind": "performance-evaluation",
            "contractVersion": self.contract["contractVersion"],
            "runId": "run-1",
            "mode": "seed",
            "success": True,
            "target": target,
            "profile": "scorecard",
            "runnerClass": "test-runner",
            "commit": "0" * 40,
            "sourceHash": "a" * 64,
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "environment": report["environment"],
            "hostPreflight": self._host_preflight(),
            "artifactHashes": {
                "contract": performance_evidence.sha256(self._contract_path),
                "hostPreflight": "f" * 64,
                "workloads": "b" * 64,
                "benchmarkDotNet": "c" * 64,
                "soak": "d" * 64,
            },
            "rawReports": [
                {
                    "path": "results/fixture-report-full.json",
                    "sha256": "e" * 64,
                },
            ],
            "benchmarkDotNetHostEnvironment": self._bdn_report()[
                "HostEnvironmentInfo"
            ],
            "benchmarkDotNetControls": [
                {
                    "id": control["id"],
                    "metric": control["metric"],
                    "actual": 0,
                    "maximum": control["maximum"],
                    "passed": True,
                }
                for control in self.contract["benchmarkDotNetControls"]
            ],
            "historicalChecks": [],
            "workloads": workloads,
            "soakScenarios": soak_scenarios,
        }

    def _write_seed_evaluations(
        self,
        root: Path,
        runner_class: str,
    ) -> list[Path]:
        """Persist one seed evaluation per required target for a runner class."""
        paths = []
        for target in ("mysql84", "mariadb118"):
            evaluation = self._evaluation(target)
            evaluation["runnerClass"] = runner_class
            path = root / f"{runner_class}-{target}.json"
            path.write_text(json.dumps(evaluation), encoding="utf-8")
            paths.append(path)

        return paths


if __name__ == "__main__":
    unittest.main()
