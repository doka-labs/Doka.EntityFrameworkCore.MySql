"""Contracts for evidence that stopped at the configured sample cap.

The runner never exceeds the cap, so a run that reaches it before satisfying
the contract's minimum measurement duration produces a complete, typed result
rather than an exception. These tests pin what that result means: the quality
policy decides whether it is observed or retried, and it can never become the
baseline that later comparisons are judged against.
"""

from __future__ import annotations

import contextlib
import copy
import io
import json
import math
import unittest
from pathlib import Path

from eng.performance import cli as performance_evidence
from eng.tests._performance_fixtures import PerformanceEvidenceFixtureMixin


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


class CappedMeasurementEvidenceTests(PerformanceEvidenceFixtureMixin, unittest.TestCase):
    """Classify a capped measurement instead of rejecting it as malformed."""

    def capped_report(self, *, sample_nanoseconds: float = 1.0) -> dict:
        """Return a report whose first workload genuinely ran into the cap.

        The population is grown to exactly the contract cap and every sample is
        short enough that the minimum duration cannot be met. Producing the
        state rather than only relabelling it is what makes the test exercise
        the same evidence the runner emits.
        """
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        profile = self.contract["profiles"]["scorecard"]
        definition = next(
            candidate
            for candidate in self.contract["workloads"]
            if candidate["id"] == entry["id"]
        )
        cap = performance_evidence.expected_measurement_sample_count(
            profile,
            definition,
        ) * int(profile["maximumMeasurementSampleMultiplier"])

        interval = int(profile["calibrationIntervalSamples"])
        entry["sampleCount"] = cap
        entry["calibrationNanoseconds"] = [100.0] * cap
        entry["calibrationPulseNanoseconds"] = [100.0] * math.ceil(cap / interval)
        entry["calibrationPulseIndices"] = [index // interval for index in range(cap)]
        # A deterministic ramp keeps the relative standard error inside the
        # contract ceiling so precision is not the reason the run stopped.
        self._replace_workload_samples(
            entry,
            [sample_nanoseconds + (index % 3) * 1e-6 for index in range(cap)],
        )
        entry["terminationReason"] = "sample_cap_reached"
        entry["minimumDurationReached"] = False
        return report

    def validate(self, report: dict, contract: dict) -> list[dict]:
        """Run the workload validator against one contract."""
        return performance_evidence.validate_workload_report(
            report,
            contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )

    def test_enforce_policy_rejects_a_capped_measurement(self) -> None:
        """Route a truncated sample into the inconclusive retry path."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["scorecard"]["measurementQualityPolicy"] = "enforce"

        with self.assertRaises(performance_evidence.MeasurementQualityError) as raised:
            self.validate(self.capped_report(), contract)

        message = str(raised.exception)
        self.assertIn("sample cap", message)
        self.assertIn("minimum", message)

    def test_diagnostic_names_configured_and_achieved_values(self) -> None:
        """Give recalibration both numbers it needs, not just the failure."""
        contract = copy.deepcopy(self.contract)
        profile = contract["profiles"]["scorecard"]
        profile["measurementQualityPolicy"] = "enforce"
        report = self.capped_report()
        achieved = report["workloads"][0]["sampleCount"]

        with self.assertRaises(performance_evidence.MeasurementQualityError) as raised:
            self.validate(report, contract)

        message = str(raised.exception)
        self.assertIn(str(achieved), message)
        self.assertIn(str(profile["minimumMeasurementDurationMilliseconds"]), message)
        self.assertIn(str(profile["maximumMeasurementSampleMultiplier"]), message)
        self.assertIn("Recalibrate", message)

    def test_observe_policy_publishes_capped_evidence(self) -> None:
        """Keep diagnostic evidence usable while the profile only observes."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["scorecard"]["measurementQualityPolicy"] = "observe"

        workloads = self.validate(self.capped_report(), contract)

        capped = [
            workload
            for workload in workloads
            if workload["terminationReason"] == "sample_cap_reached"
        ]
        self.assertEqual(1, len(capped))
        self.assertFalse(capped[0]["minimumDurationReached"])

    def test_termination_reason_is_carried_into_normalized_evidence(self) -> None:
        """Persist the classification so downstream consumers can act on it."""
        workloads = self.validate(self._workload_report("mysql84"), self.contract)

        for workload in workloads:
            with self.subTest(workload=workload["id"]):
                self.assertEqual("precision_reached", workload["terminationReason"])
                self.assertTrue(workload["minimumDurationReached"])

    def test_unknown_termination_reason_is_rejected(self) -> None:
        """Refuse a classification the contract does not define."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["terminationReason"] = "gave_up"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "unknown termination reason",
        ):
            self.validate(report, self.contract)

    def test_previous_schemas_are_rejected_as_unsupported(self) -> None:
        """Name version transitions instead of calling old reports invalid."""
        for schema_version in (3, 4):
            with self.subTest(schema_version=schema_version):
                report = self._workload_report("mysql84")
                report["schemaVersion"] = schema_version

                with self.assertRaisesRegex(
                    performance_evidence.PerformanceEvidenceError,
                    f"schema version {schema_version}",
                ):
                    self.validate(report, self.contract)

    def test_current_schema_is_accepted(self) -> None:
        """Pin the version the current producer emits."""
        report = self._workload_report("mysql84")

        self.assertEqual(5, report["schemaVersion"])
        self.validate(report, self.contract)

    def test_contradictory_duration_and_reason_are_rejected(self) -> None:
        """Refuse a combination the runner cannot produce."""
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        entry["minimumDurationReached"] = False
        entry["terminationReason"] = "precision_reached"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "Only a capped run can miss the duration target",
        ):
            self.validate(report, self.contract)

    def test_capped_reason_below_the_cap_is_rejected(self) -> None:
        """Refuse a capped claim from a population the cap never bound."""
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        entry["terminationReason"] = "sample_cap_reached"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "but the contract cap is",
        ):
            self.validate(report, self.contract)

    def test_population_above_the_cap_is_rejected(self) -> None:
        """Refuse evidence the reviewed sampling loop cannot have produced."""
        report = self.capped_report()
        entry = report["workloads"][0]
        extra = entry["samplesNanoseconds"] + [1.0]
        entry["sampleCount"] = len(extra)
        entry["calibrationNanoseconds"] = [100.0] * len(extra)
        entry["calibrationPulseIndices"] = list(range(len(extra)))
        self._replace_workload_samples(entry, extra)

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "above the contract maximum",
        ):
            self.validate(report, self.contract)

    def test_capped_reason_with_both_targets_met_is_rejected(self) -> None:
        """Refuse calling a run truncated when nothing was left to achieve."""
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        profile = self.contract["profiles"]["scorecard"]
        definition = next(
            candidate
            for candidate in self.contract["workloads"]
            if candidate["id"] == entry["id"]
        )
        cap = performance_evidence.expected_measurement_sample_count(
            profile,
            definition,
        ) * int(profile["maximumMeasurementSampleMultiplier"])
        interval = int(profile["calibrationIntervalSamples"])
        per_sample = entry["samplesNanoseconds"][0]

        entry["sampleCount"] = cap
        entry["calibrationNanoseconds"] = [100.0] * cap
        entry["calibrationPulseNanoseconds"] = [100.0] * math.ceil(cap / interval)
        entry["calibrationPulseIndices"] = [index // interval for index in range(cap)]
        self._replace_workload_samples(entry, [per_sample] * cap)
        entry["terminationReason"] = "sample_cap_reached"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "A run that satisfies both is precise",
        ):
            self.validate(report, self.contract)

    def test_duration_flag_contradicting_the_samples_is_rejected(self) -> None:
        """Refuse a flag that disagrees with the measurement it describes."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["minimumDurationReached"] = False
        report["workloads"][0]["terminationReason"] = "sample_cap_reached"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "while its samples measure",
        ):
            self.validate(report, self.contract)

    def test_cap_with_duration_met_but_precision_missed_is_accepted(self) -> None:
        """Accept the capped run whose only unmet target is precision."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["scorecard"]["measurementQualityPolicy"] = "observe"
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        profile = contract["profiles"]["scorecard"]
        definition = next(
            candidate
            for candidate in contract["workloads"]
            if candidate["id"] == entry["id"]
        )
        cap = performance_evidence.expected_measurement_sample_count(
            profile,
            definition,
        ) * int(profile["maximumMeasurementSampleMultiplier"])
        interval = int(profile["calibrationIntervalSamples"])
        per_sample = entry["samplesNanoseconds"][0]

        entry["sampleCount"] = cap
        entry["calibrationNanoseconds"] = [100.0] * cap
        entry["calibrationPulseNanoseconds"] = [100.0] * math.ceil(cap / interval)
        entry["calibrationPulseIndices"] = [index // interval for index in range(cap)]
        # One extreme observation pushes the error past the ceiling while the
        # total duration stays comfortably above the minimum.
        samples = self._samples_missing_the_error_budget(per_sample, cap, profile)
        self._replace_workload_samples(entry, samples)
        entry["terminationReason"] = "sample_cap_reached"

        with contextlib.redirect_stdout(io.StringIO()) as captured:
            workloads = self.validate(report, contract)

        capped = next(
            workload
            for workload in workloads
            if workload["id"] == entry["id"]
        )
        self.assertEqual("sample_cap_reached", capped["terminationReason"])
        self.assertTrue(capped["minimumDurationReached"])

        # Accepting the report is not enough: a cap caused by precision alone
        # must still be announced, or observe silently publishes a run nobody
        # was told was truncated.
        diagnostic = captured.getvalue()
        self.assertIn("Measurement quality observation", diagnostic)
        self.assertIn("stopped at the sample cap", diagnostic)
        self.assertIn("Relative standard error", diagnostic)
        self.assertEqual(
            1,
            diagnostic.count("Measurement quality observation"),
            "One outcome must produce exactly one verdict.",
        )

    def test_precision_only_cap_is_inconclusive_under_enforce(self) -> None:
        """Route a precision-only cap into the retry path, not into silence."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["scorecard"]["measurementQualityPolicy"] = "enforce"
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        cap = self._grow_to_sample_cap(entry)
        per_sample = entry["samplesNanoseconds"][0]
        self._replace_workload_samples(
            entry,
            self._samples_missing_the_error_budget(per_sample, cap),
        )
        entry["terminationReason"] = "sample_cap_reached"

        with self.assertRaises(performance_evidence.MeasurementQualityError) as raised:
            self.validate(report, contract)

        message = str(raised.exception)
        self.assertIn("stopped at the sample cap", message)
        self.assertIn("Relative standard error", message)
        self.assertIn(str(cap), message)

    def test_reaching_both_targets_at_the_cap_stays_precise(self) -> None:
        """Accept a precise run that happens to end exactly at the cap."""
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        profile = self.contract["profiles"]["scorecard"]
        definition = next(
            candidate
            for candidate in self.contract["workloads"]
            if candidate["id"] == entry["id"]
        )
        cap = performance_evidence.expected_measurement_sample_count(
            profile,
            definition,
        ) * int(profile["maximumMeasurementSampleMultiplier"])
        interval = int(profile["calibrationIntervalSamples"])
        per_sample = entry["samplesNanoseconds"][0]

        entry["sampleCount"] = cap
        entry["calibrationNanoseconds"] = [100.0] * cap
        entry["calibrationPulseNanoseconds"] = [100.0] * math.ceil(cap / interval)
        entry["calibrationPulseIndices"] = [index // interval for index in range(cap)]
        self._replace_workload_samples(entry, [per_sample] * cap)

        workloads = self.validate(report, self.contract)

        precise = next(
            workload
            for workload in workloads
            if workload["id"] == entry["id"]
        )
        self.assertEqual("precision_reached", precise["terminationReason"])
        self.assertEqual(cap, precise["sampleCount"])

    def test_non_boolean_duration_flag_is_rejected(self) -> None:
        """Refuse a truthy string where the contract requires a boolean."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["minimumDurationReached"] = "false"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "minimumDurationReached",
        ):
            self.validate(report, self.contract)


class CappedEvidenceBaselineTests(PerformanceEvidenceFixtureMixin, unittest.TestCase):
    """Keep a truncated measurement out of the accepted baseline."""

    def test_seed_refuses_evidence_that_never_met_the_minimum_duration(self) -> None:
        """Reject promoting a capped run to the reference every run is judged against."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["scorecard"]["measurementQualityPolicy"] = "observe"
        workloads = performance_evidence.validate_workload_report(
            self._workload_report("mysql84"),
            contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        capped_id = workloads[0]["id"]
        workloads[0]["minimumDurationReached"] = False

        with self.assertRaises(performance_evidence.PerformanceEvidenceError) as raised:
            performance_evidence.reject_truncated_measurements(workloads, "Seed input")

        message = str(raised.exception)
        self.assertIn(capped_id, message)
        self.assertIn("Recalibrate", message)

    def test_seed_refuses_a_cap_caused_by_precision_alone(self) -> None:
        """Block promotion when only precision was missed, duration was fine.

        This is the case that proves the guard keys on the termination reason
        rather than on the duration flag. Before the axes were separated, this
        combination could not even be expressed.
        """
        workloads = [
            {
                "id": "workload.a",
                "terminationReason": "sample_cap_reached",
                "minimumDurationReached": True,
            }
        ]

        with self.assertRaises(performance_evidence.PerformanceEvidenceError) as raised:
            performance_evidence.reject_truncated_measurements(workloads, "Seed input")

        self.assertIn("workload.a", str(raised.exception))

    def test_seed_accepts_evidence_that_met_the_minimum_duration(self) -> None:
        """Keep a conforming measurement promotable."""
        workloads = performance_evidence.validate_workload_report(
            self._workload_report("mysql84"),
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )

        performance_evidence.reject_truncated_measurements(workloads, "Seed input")

    def test_accepted_baseline_never_carries_a_capped_workload(self) -> None:
        """Pin the invariant against the baseline that ships in the repository."""
        baseline_path = (
            REPOSITORY_ROOT / "benchmarks" / "baselines" / "doka-benchmark-baseline.json"
        )
        baseline = json.loads(baseline_path.read_text(encoding="utf-8"))

        def walk(node: object) -> None:
            if isinstance(node, dict):
                self.assertNotEqual(
                    False,
                    node.get("minimumDurationReached", True),
                    "The accepted baseline contains a truncated measurement.",
                )
                # A cap caused by precision alone leaves the duration flag true,
                # so the termination reason has to be checked in its own right.
                self.assertNotEqual(
                    "sample_cap_reached",
                    node.get("terminationReason"),
                    "The accepted baseline contains a capped measurement.",
                )
                for value in node.values():
                    walk(value)
            elif isinstance(node, list):
                for value in node:
                    walk(value)

        walk(baseline)


if __name__ == "__main__":
    unittest.main()
