"""Independent tail-confirmation tests for performance evidence."""

from __future__ import annotations

import copy
import unittest
from typing import Any

from eng.performance import cli as performance_evidence
from eng.tests._performance_fixtures import PerformanceEvidenceFixtureMixin


class PerformanceConfirmationTests(PerformanceEvidenceFixtureMixin, unittest.TestCase):
    """Verify p95 and p99 confirmation planning and recomputation."""

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

    def test_all_out_of_budget_latency_metrics_require_confirmation(self) -> None:
        """Require independent confirmation for every latency percentile."""
        current = [
            {
                "id": "latency",
                "normalizedMedian": 200.0,
                "normalizedP95": 200.0,
                "normalizedP99": 200.0,
                "normalizedSamples": [200.0] * 100,
                "calibrationMedianNanoseconds": 10.0,
            }
        ]
        baseline = {
            "workloads": [
                {
                    "id": "latency",
                    "normalizedMedian": 100.0,
                    "normalizedP95": 100.0,
                    "normalizedP99": 100.0,
                    "calibrationMedianNanoseconds": 10.0,
                }
            ]
        }

        candidates = performance_evidence.historical_latency_confirmation_candidates(
            current,
            baseline,
            self.contract,
        )

        self.assertEqual(1, len(candidates))
        self.assertEqual(2, candidates[0]["confirmationRuns"])
        self.assertEqual(
            {"normalizedMedian", "normalizedP95", "normalizedP99"},
            {metric["metric"] for metric in candidates[0]["metrics"]},
        )

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


if __name__ == "__main__":
    unittest.main()
