"""Regression tests for performance, baseline, and soak evidence gates."""

from __future__ import annotations

import copy
import importlib.util
import json
import math
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

    def test_json_comparer_warmup_reaches_the_contract_operation_floor(self) -> None:
        """Keep tiered-JIT promotion outside JSON comparer tail measurements."""
        definitions = {
            definition["id"]: definition
            for definition in self.contract["workloads"]
        }
        profile = self.contract["profiles"]["scorecard"]

        self.assertEqual(
            128,
            performance_evidence.expected_warmup_sample_count(
                profile,
                definitions["json.compare.node.equal.bytes-65536"],
            ),
        )
        self.assertEqual(
            256,
            performance_evidence.expected_warmup_sample_count(
                profile,
                definitions["json.compare.node.late-mismatch.bytes-65536"],
            ),
        )

    def test_json_element_tail_population_exceeds_the_profile_default(self) -> None:
        """Keep isolated scheduler bursts from dominating element-comparer p99."""
        definition = next(
            definition
            for definition in self.contract["workloads"]
            if definition["id"] == "json.compare.element.late-mismatch.bytes-65536"
        )

        self.assertEqual(
            1024,
            performance_evidence.expected_measurement_sample_count(
                self.contract["profiles"]["scorecard"],
                definition,
            ),
        )

    def test_expensive_workloads_keep_p99_population_without_full_matrix_cost(self) -> None:
        """Retain at least 100 tail observations without repeating large writes 256 times."""
        definition = next(
            definition
            for definition in self.contract["workloads"]
            if definition["id"] == "write.savechanges.async.rows-10000.batch-default"
        )

        self.assertEqual(
            128,
            performance_evidence.expected_measurement_sample_count(
                self.contract["profiles"]["scorecard"],
                definition,
            ),
        )
        self.assertEqual(
            256,
            performance_evidence.expected_measurement_sample_count(
                self.contract["profiles"]["stress"],
                definition,
            ),
        )

    def test_fixed_large_write_populations_have_bounded_timeout_floors(self) -> None:
        """Keep every fixed large write population complete on hosted runners."""
        definitions = {
            definition["id"]: definition
            for definition in self.contract["workloads"]
        }
        expected_floors = {
            "hilo.insert.async.contexts-10.rows-1000": 240,
            "hilo.insert.sync.contexts-10.rows-1000": 240,
            "write.savechanges.async.rows-10000.batch-default": 300,
            "write.savechanges.sync.rows-10000.batch-default": 300,
        }

        for workload_id, expected_floor in expected_floors.items():
            with self.subTest(workload=workload_id):
                definition = definitions[workload_id]

                self.assertEqual(
                    expected_floor,
                    performance_evidence.expected_workload_timeout_seconds(
                        self.contract["timeoutPolicies"],
                        self.contract["profiles"]["scorecard"],
                        definition,
                    ),
                )
                self.assertEqual(
                    300,
                    performance_evidence.expected_workload_timeout_seconds(
                        self.contract["timeoutPolicies"],
                        self.contract["profiles"]["stress"],
                        definition,
                    ),
                )

    def test_every_expensive_workload_uses_a_named_timeout_policy(self) -> None:
        """Keep expensive workload hang deadlines exhaustive and centralized."""
        expensive = [
            workload
            for workload in self.contract["workloads"]
            if workload.get("cost") == "expensive"
        ]

        self.assertTrue(expensive)
        self.assertTrue(all("timeoutPolicy" in workload for workload in expensive))

    def test_expensive_workload_without_timeout_policy_is_rejected(self) -> None:
        """Reject additions that silently inherit an unsuitable short deadline."""
        contract = copy.deepcopy(self.contract)
        workload = next(
            workload
            for workload in contract["workloads"]
            if workload.get("cost") == "expensive"
        )
        del workload["timeoutPolicy"]

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "must reference a timeoutPolicy",
        ):
            performance_evidence.validate_contract(contract)

    def test_unknown_and_unused_timeout_policies_are_rejected(self) -> None:
        """Reject drift between declarations and their active consumers."""
        contract = copy.deepcopy(self.contract)
        workload = next(
            workload
            for workload in contract["workloads"]
            if workload.get("cost") == "expensive"
        )
        workload["timeoutPolicy"] = "unknown"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "references unknown timeout policy",
        ):
            performance_evidence.validate_contract(contract)

        contract = copy.deepcopy(self.contract)
        contract["timeoutPolicies"]["unused"] = {
            "minimumWorkloadTimeoutSeconds": 180,
        }

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "unused timeout policies: unused",
        ):
            performance_evidence.validate_contract(contract)

    def test_timeout_policy_must_be_positive_and_matrix_bounded(self) -> None:
        """Reject disabled or ineffective named hang deadlines."""
        contract = copy.deepcopy(self.contract)
        contract["timeoutPolicies"]["expensive-standard"][
            "minimumWorkloadTimeoutSeconds"
        ] = 0

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "minimumWorkloadTimeoutSeconds",
        ):
            performance_evidence.validate_contract(contract)

        contract["timeoutPolicies"]["expensive-standard"][
            "minimumWorkloadTimeoutSeconds"
        ] = 1201

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "timeout exceeds the 'scorecard' matrix deadline",
        ):
            performance_evidence.validate_contract(contract)

    def test_resilience_tail_population_excludes_tiered_jit_and_short_bursts(self) -> None:
        """Keep query startup and isolated host bursts outside resilience p99."""
        definitions = [
            definition
            for definition in self.contract["workloads"]
            if definition["family"] == "resilience"
        ]
        profile = self.contract["profiles"]["scorecard"]

        self.assertEqual(4, len(definitions))
        for definition in definitions:
            with self.subTest(workload=definition["id"]):
                self.assertEqual(
                    512,
                    performance_evidence.expected_warmup_sample_count(
                        profile,
                        definition,
                    ),
                )
                self.assertEqual(
                    8192,
                    performance_evidence.expected_measurement_sample_count(
                        profile,
                        definition,
                    ),
                )

    def test_only_out_of_budget_p99_values_require_confirmation(self) -> None:
        """Plan bounded reruns without repeating an otherwise green matrix."""
        current = [
            {
                "id": "stable",
                "normalizedP99": 139.0,
                "calibrationMedianNanoseconds": 10.0,
            },
            {
                "id": "tail",
                "normalizedP99": 141.0,
                "calibrationMedianNanoseconds": 10.0,
            },
        ]
        baseline = {
            "workloads": [
                {
                    "id": "stable",
                    "normalizedP99": 100.0,
                    "calibrationMedianNanoseconds": 10.0,
                },
                {
                    "id": "tail",
                    "normalizedP99": 100.0,
                    "calibrationMedianNanoseconds": 10.0,
                },
            ],
        }

        candidates = performance_evidence.historical_p99_confirmation_candidates(
            current,
            baseline,
            self.contract,
        )

        self.assertEqual(1, len(candidates))
        self.assertEqual("tail", candidates[0]["workloadId"])
        self.assertEqual(2, candidates[0]["confirmationRuns"])

    def test_p99_gate_accepts_an_insignificant_number_of_tail_exceedances(self) -> None:
        """Do not classify ordinary one-percent tail variation as a regression."""
        samples = [100.0] * 1028 + [200.0] * 16
        workload = {
            "normalizedP99": performance_evidence.percentile(
                sorted(samples),
                0.99,
            ),
            "normalizedSamples": samples,
            "calibrationMedianNanoseconds": 10.0,
        }

        check = performance_evidence.historical_p99_check(
            "tail",
            workload,
            {
                "normalizedP99": 100.0,
                "calibrationMedianNanoseconds": 10.0,
            },
            self.contract,
        )

        self.assertGreater(check["actual"], check["maximum"])
        self.assertEqual(16, check["exceedanceCount"])
        self.assertGreater(check["pValue"], check["significanceLevel"])
        self.assertTrue(check["passed"])

    def test_p99_gate_rejects_a_statistically_significant_tail_regression(self) -> None:
        """Reject sustained excess tail latency across a complete sample population."""
        samples = [100.0] * 960 + [200.0] * 40
        workload = {
            "normalizedP99": performance_evidence.percentile(
                sorted(samples),
                0.99,
            ),
            "normalizedSamples": samples,
            "calibrationMedianNanoseconds": 10.0,
        }

        check = performance_evidence.historical_p99_check(
            "tail",
            workload,
            {
                "normalizedP99": 100.0,
                "calibrationMedianNanoseconds": 10.0,
            },
            self.contract,
        )

        self.assertEqual(40, check["exceedanceCount"])
        self.assertLess(check["pValue"], check["significanceLevel"])
        self.assertFalse(check["passed"])

    def test_confirmed_p99_gate_binds_independent_samples_to_the_trigger(self) -> None:
        """Recompute confirmation evidence without reusing the selected population."""
        samples = [100.0] * 658 + [200.0] * 10
        confirmation_p99 = performance_evidence.percentile(
            sorted(samples),
            0.99,
        )
        check = performance_evidence.historical_p99_check(
            "tail",
            {
                "normalizedP99": confirmation_p99,
                "normalizedSamples": samples,
                "calibrationMedianNanoseconds": 10.0,
            },
            {
                "normalizedP99": 100.0,
                "calibrationMedianNanoseconds": 10.0,
            },
            self.contract,
        )
        confirmation = {
            "confirmationRuns": 2,
            "originalSampleCount": 128,
            "confirmationSampleCount": len(samples),
            "originalNormalizedP99": 200.0,
            "confirmationNormalizedP99": confirmation_p99,
            "confirmationCalibrationMedianNanoseconds": 10.0,
            "maximumNormalizedP99": check["maximum"],
            "calibrationAdjustmentFactor": check[
                "calibrationAdjustmentFactor"
            ],
            "exceedanceCount": check["exceedanceCount"],
            "exceedanceRate": check["exceedanceRate"],
            "expectedExceedanceProbability": check[
                "expectedExceedanceProbability"
            ],
            "pValue": check["pValue"],
            "significanceLevel": check["significanceLevel"],
            "normalizedSamples": samples,
            "passed": check["passed"],
        }

        validated = performance_evidence.validate_confirmed_p99_check(
            "tail",
            {
                "sampleCount": 128,
                "normalizedP99": 200.0,
            },
            {
                "normalizedP99": 100.0,
                "calibrationMedianNanoseconds": 10.0,
            },
            confirmation,
            self.contract,
        )

        self.assertEqual(200.0, validated["triggerActual"])
        self.assertEqual(confirmation_p99, validated["actual"])
        self.assertTrue(validated["confirmationRequired"])
        self.assertTrue(validated["passed"])

    def test_historical_gate_never_penalizes_a_faster_control_path(self) -> None:
        """Use calibration only to discount contention, never to invent slowdown."""
        report = self._workload_report("mysql84")
        baseline_workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        current_workloads = copy.deepcopy(baseline_workloads)
        workload = current_workloads[0]
        workload["calibrationMedianNanoseconds"] /= 2
        workload["normalizedMedian"] *= 2
        workload["normalizedP95"] *= 2
        workload["normalizedP99"] *= 2
        workload["normalizedStandardError"] *= 2
        workload["_normalizedSamples"] = [
            sample * 2
            for sample in workload["_normalizedSamples"]
        ]

        checks = performance_evidence.validate_historical_budgets(
            current_workloads,
            {"workloads": baseline_workloads},
            self.contract,
        )

        workload_checks = [
            check
            for check in checks
            if check["workloadId"] == workload["id"]
        ]
        self.assertTrue(all(check["passed"] for check in workload_checks))
        self.assertEqual(
            {0.5, 1.0},
            {
                check["calibrationAdjustmentFactor"]
                for check in workload_checks
            },
        )

    def test_tail_confirmation_recomputes_combined_statistics_and_memory(self) -> None:
        """Bind a confirmed verdict to all original and repeated samples."""
        def workload(samples: list[float], allocated_bytes: float) -> dict[str, Any]:
            sorted_samples = sorted(samples)
            calibration = [10.0] * len(samples)
            normalized = [sample / 10.0 for sample in samples]
            sorted_normalized = sorted(normalized)
            return {
                "id": "tail",
                "operationsPerSample": 1,
                "checksum": len(samples),
                "samplesNanoseconds": samples,
                "sampleCount": len(samples),
                "medianNanoseconds": performance_evidence.percentile(
                    sorted_samples,
                    0.5,
                ),
                "p95Nanoseconds": performance_evidence.percentile(
                    sorted_samples,
                    0.95,
                ),
                "p99Nanoseconds": performance_evidence.percentile(
                    sorted_samples,
                    0.99,
                ),
                "standardErrorNanoseconds": performance_evidence.standard_error(samples),
                "calibrationKind": "cpu",
                "calibrationMedianNanoseconds": 10.0,
                "calibrationStandardErrorNanoseconds": 0.0,
                "normalizedMedian": performance_evidence.percentile(
                    sorted_normalized,
                    0.5,
                ),
                "normalizedP95": performance_evidence.percentile(
                    sorted_normalized,
                    0.95,
                ),
                "normalizedP99": performance_evidence.percentile(
                    sorted_normalized,
                    0.99,
                ),
                "allocatedBytesPerOperation": allocated_bytes,
                "retainedBytes": allocated_bytes,
                "gen0CollectionsPer1000": allocated_bytes / 10,
                "gen1CollectionsPer1000": 0.0,
                "gen2CollectionsPer1000": 0.0,
                "calibrationNanoseconds": calibration,
                "calibrationPulseNanoseconds": [10.0],
                "calibrationPulseIndices": [0] * len(samples),
                "normalizedSamples": normalized,
            }

        original = workload([10.0, 20.0], 100.0)
        confirmations = [
            workload([30.0, 40.0], 200.0),
            workload([50.0, 60.0], 300.0),
        ]

        merged = performance_evidence.merge_workload_tail_samples(
            original,
            confirmations,
        )

        self.assertEqual(6, merged["sampleCount"])
        self.assertEqual(35.0, merged["medianNanoseconds"])
        self.assertEqual(200.0, merged["allocatedBytesPerOperation"])
        self.assertEqual(300.0, merged["retainedBytes"])

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
        """Reject genuine CPU saturation before calibrated measurement starts."""
        report = self._host_preflight()
        report["initialCpuUtilization"] = 0.95
        report["success"] = False

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "initially CPU-saturated",
        ):
            performance_evidence.validate_host_preflight(
                report,
                self.contract,
                maximum_age_hours=12,
            )

    def test_cpu_admission_accepts_desktop_load_without_cpu_saturation(self) -> None:
        """Do not reject browser and window-server threads as CPU saturation."""
        report = self._host_preflight()
        report.update(
            {
                "schemaVersion": 3,
                "admissionMetric": performance_evidence.HOST_ADMISSION_METRIC,
                "initialCpuUtilization": 0.2,
                "maximumInitialCpuUtilization": 0.9,
                "loadAverage1Minute": 12.0,
                "loadAverage1MinutePerProcessor": 1.5,
            }
        )

        performance_evidence.validate_host_preflight(
            report,
            self.contract,
            maximum_age_hours=12,
        )

    def test_cpu_admission_rejects_actual_cpu_saturation(self) -> None:
        """Keep genuine host contention outside latency measurements."""
        report = self._host_preflight()
        report.update(
            {
                "schemaVersion": 3,
                "admissionMetric": performance_evidence.HOST_ADMISSION_METRIC,
                "initialCpuUtilization": 0.95,
                "maximumInitialCpuUtilization": 0.9,
                "success": False,
            }
        )

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "initially CPU-saturated",
        ):
            performance_evidence.validate_host_preflight(
                report,
                self.contract,
                maximum_age_hours=12,
            )

    def test_process_cpu_percentages_are_normalized_by_processor_count(self) -> None:
        """Interpret ps percentages as aggregate capacity rather than load average."""
        utilization = performance_evidence.parse_process_cpu_utilization(
            "100.0\n50.0\n",
            8,
        )

        self.assertEqual(0.1875, utilization)

    def test_workload_report_binds_cpu_admission_without_load_rejection(self) -> None:
        """Accept high desktop load only when the bound CPU admission passed."""
        report = self._workload_report("mysql84")
        report["environment"].update(
            {
                "hostAdmissionMetric": performance_evidence.HOST_ADMISSION_METRIC,
                "initialHostCpuUtilization": 0.2,
                "maximumInitialHostCpuUtilization": 0.9,
                "hostLoadAverage1Minute": 12.0,
                "hostLoadAverage1MinutePerProcessor": 1.5,
            }
        )

        performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
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

    def test_workload_report_rejects_an_insufficient_measurement_window(self) -> None:
        """Reject a large sample count that covers too little measured time."""
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        self._replace_workload_samples(entry, [1.0] * entry["sampleCount"])

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "expected at least",
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
        samples = [8_000_000.0] * (entry["sampleCount"] - 1) + [10_000_000_000.0]
        self._replace_workload_samples(entry, samples)

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

    def test_gc_policy_variance_is_diagnostic_not_a_provider_failure(self) -> None:
        """Retain GC evidence without attributing host policy choices to the provider."""
        report = self._workload_report("mysql84")
        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        baseline_entry = {"workloads": copy.deepcopy(workloads)}
        workload = workloads[0]
        workload["gen0CollectionsPer1000"] = 1000000
        workload["gen1CollectionsPer1000"] = 1000000
        workload["gen2CollectionsPer1000"] = 1000000

        performance_evidence.validate_absolute_budgets(workloads, self.contract)
        performance_evidence.validate_historical_budgets(
            workloads,
            baseline_entry,
            self.contract,
        )
        diagnostics = performance_evidence.collect_gc_diagnostics(
            workloads,
            baseline_entry,
            self.contract,
        )

        workload_diagnostics = [
            diagnostic
            for diagnostic in diagnostics
            if diagnostic["workloadId"] == workload["id"]
        ]
        self.assertEqual(4, len(workload_diagnostics))
        self.assertTrue(
            all(
                diagnostic["withinReferenceRange"] is False
                for diagnostic in workload_diagnostics
            )
        )

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
            "normalizedMedian",
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
        maximum_initial_cpu = self.contract["hostPreconditions"][
            "maximumInitialCpuUtilization"
        ]
        workloads = []
        profile = self.contract["profiles"]["scorecard"]

        for definition in self.contract["workloads"]:
            sample_count = performance_evidence.expected_measurement_sample_count(
                profile,
                definition,
            )
            operations_per_sample = definition.get("operationsPerSample", 1)
            minimum_duration_nanoseconds = (
                profile["minimumMeasurementDurationMilliseconds"]
                * 1_000_000
            )
            minimum_sample_nanoseconds = (
                minimum_duration_nanoseconds
                / sample_count
                / operations_per_sample
            )
            sample_base = max(100.0, minimum_sample_nanoseconds * 1.05)
            samples = [sample_base + (index % 10) for index in range(sample_count)]
            sorted_samples = sorted(samples)
            calibration_samples = [100.0] * sample_count
            calibration_interval = profile["calibrationIntervalSamples"]
            calibration_pulses = [
                100.0
                for _ in range(math.ceil(sample_count / calibration_interval))
            ]
            calibration_pulse_indices = [
                index // calibration_interval
                for index in range(sample_count)
            ]
            normalized_samples = [sample / 100.0 for sample in samples]
            sorted_normalized_samples = sorted(normalized_samples)
            calibration_kind = (
                "cpu"
                if definition["family"] in self.contract["calibration"]["cpuFamilies"]
                else "database"
            )
            workloads.append(
                {
                    "id": definition["id"],
                    "family": definition["family"],
                    "warmupSamples": performance_evidence.expected_warmup_sample_count(
                        profile,
                        definition,
                    ),
                    "sampleCount": len(samples),
                    "operationsPerSample": operations_per_sample,
                    "checksum": 1,
                    "measuredUtc": datetime.now(timezone.utc).isoformat(),
                    "medianNanoseconds": performance_evidence.percentile(sorted_samples, 0.5),
                    "p95Nanoseconds": performance_evidence.percentile(sorted_samples, 0.95),
                    "p99Nanoseconds": performance_evidence.percentile(sorted_samples, 0.99),
                    "standardErrorNanoseconds": performance_evidence.standard_error(samples),
                    "calibrationKind": calibration_kind,
                    "calibrationMedianNanoseconds": 100.0,
                    "calibrationStandardErrorNanoseconds": 0.0,
                    "normalizedMedian": performance_evidence.percentile(
                        sorted_normalized_samples,
                        0.5,
                    ),
                    "normalizedP95": performance_evidence.percentile(
                        sorted_normalized_samples,
                        0.95,
                    ),
                    "normalizedP99": performance_evidence.percentile(
                        sorted_normalized_samples,
                        0.99,
                    ),
                    "allocatedBytesPerOperation": 1000,
                    "retainedBytes": 0,
                    "gen0CollectionsPer1000": 0,
                    "gen1CollectionsPer1000": 0,
                    "gen2CollectionsPer1000": 0,
                    "samplesNanoseconds": samples,
                    "calibrationNanoseconds": calibration_samples,
                    "calibrationPulseNanoseconds": calibration_pulses,
                    "calibrationPulseIndices": calibration_pulse_indices,
                    "normalizedSamples": normalized_samples,
                }
            )

        return {
            "schemaVersion": 3,
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
                "hostAdmissionMetric": performance_evidence.HOST_ADMISSION_METRIC,
                "initialHostCpuUtilization": 0.2,
                "maximumInitialHostCpuUtilization": maximum_initial_cpu,
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

    @staticmethod
    def _replace_workload_samples(
        entry: dict[str, Any],
        samples: list[float],
    ) -> None:
        """Replace raw samples while keeping every derived statistic coherent."""
        calibration_samples = entry["calibrationNanoseconds"]
        normalized_samples = [
            sample / calibration
            for sample, calibration in zip(samples, calibration_samples)
        ]
        sorted_samples = sorted(samples)
        sorted_normalized_samples = sorted(normalized_samples)

        entry.update(
            {
                "samplesNanoseconds": samples,
                "normalizedSamples": normalized_samples,
                "medianNanoseconds": performance_evidence.percentile(
                    sorted_samples,
                    0.5,
                ),
                "p95Nanoseconds": performance_evidence.percentile(
                    sorted_samples,
                    0.95,
                ),
                "p99Nanoseconds": performance_evidence.percentile(
                    sorted_samples,
                    0.99,
                ),
                "standardErrorNanoseconds": performance_evidence.standard_error(
                    samples
                ),
                "normalizedMedian": performance_evidence.percentile(
                    sorted_normalized_samples,
                    0.5,
                ),
                "normalizedP95": performance_evidence.percentile(
                    sorted_normalized_samples,
                    0.95,
                ),
                "normalizedP99": performance_evidence.percentile(
                    sorted_normalized_samples,
                    0.99,
                ),
            }
        )

    def _host_preflight(self) -> dict[str, Any]:
        """Build a passing host-preflight fixture bound to the test CPU."""
        maximum_initial_cpu = self.contract["hostPreconditions"][
            "maximumInitialCpuUtilization"
        ]

        return {
            "schemaVersion": 3,
            "kind": "performance-host-preflight",
            "contractVersion": self.contract["contractVersion"],
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "processor": "test CPU",
            "processorCount": 8,
            "loadAverage1Minute": 0.5,
            "loadAverage5Minutes": 0.5,
            "loadAverage15Minutes": 0.5,
            "loadAverage1MinutePerProcessor": 0.0625,
            "admissionMetric": performance_evidence.HOST_ADMISSION_METRIC,
            "initialCpuUtilization": 0.2,
            "maximumInitialCpuUtilization": maximum_initial_cpu,
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
            "schemaVersion": 3,
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
