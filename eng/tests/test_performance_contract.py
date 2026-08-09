"""Contract and source-identity tests for performance evidence."""

from __future__ import annotations

import copy
import math
import subprocess
import tempfile
import unittest
from pathlib import Path

from eng.performance import cli as performance_evidence
from eng.tests._performance_fixtures import PerformanceEvidenceFixtureMixin


class PerformanceContractTests(PerformanceEvidenceFixtureMixin, unittest.TestCase):
    """Verify the versioned workload contract and its source identity."""

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

    def test_adaptive_sample_multiplier_must_be_a_positive_integer(self) -> None:
        """Reject sampling caps that disable or ambiguously bound extension."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["smoke"]["maximumMeasurementSampleMultiplier"] = 0

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "maximumMeasurementSampleMultiplier",
        ):
            performance_evidence.validate_contract(contract)

    def test_sample_cap_covers_the_population_the_baseline_needed(self) -> None:
        """Reject an extension cap below the population the baseline required.

        The cap entered the contract at four times the required population
        without being measured against the accepted baseline, where most
        workloads had already needed more. Every enforcing run then stopped at
        the cap with a relative standard error far inside its target and was
        discarded as inconclusive, because a workload reaching the cap has by
        definition missed one of its two quality targets.
        """
        baseline = performance_evidence.load_json(
            self._repo_root
            / "benchmarks"
            / "baselines"
            / "doka-benchmark-baseline.json"
        )
        definitions = {
            definition["id"]: definition
            for definition in self.contract["workloads"]
        }
        recorded_targets = {
            record["target"]: record for record in baseline["baselines"]
        }

        # Engines differ in how many samples a workload needs, so a check that
        # reads one baseline entry would leave the others free to drift.
        self.assertEqual(
            set(self.contract["requiredTargets"]),
            set(recorded_targets),
            "Every required target must contribute its own baseline populations.",
        )

        for name, profile in self.contract["profiles"].items():
            if profile["measurementQualityPolicy"] != "enforce":
                continue

            multiplier = profile["maximumMeasurementSampleMultiplier"]

            for target, recorded in sorted(recorded_targets.items()):
                # Sample population and measured duration are proportional, so
                # a profile demanding a longer measurement needs the recorded
                # population scaled by the ratio of the two duration floors.
                scale = (
                    profile["minimumMeasurementDurationMilliseconds"]
                    / self.contract["profiles"][recorded["profile"]][
                        "minimumMeasurementDurationMilliseconds"
                    ]
                )

                for workload in recorded["workloads"]:
                    identifier = workload["id"]
                    self.assertIn(identifier, definitions)
                    cap = multiplier * performance_evidence.expected_measurement_sample_count(
                        profile,
                        definitions[identifier],
                    )
                    needed = math.ceil(workload["sampleCount"] * scale)

                    with self.subTest(
                        profile=name,
                        target=target,
                        workload=identifier,
                    ):
                        self.assertLessEqual(
                            needed,
                            cap,
                            f"Profile '{name}' caps '{identifier}' on '{target}' "
                            f"at {cap} samples, but reaching the duration floor "
                            f"needs {needed}.",
                        )

    def test_measurement_quality_policy_is_explicit_and_bounded(self) -> None:
        """Reject profiles that cannot distinguish observation from enforcement."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["scorecard"]["measurementQualityPolicy"] = "retry"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "measurementQualityPolicy",
        ):
            performance_evidence.validate_contract(contract)

    def test_every_profile_validates_calibration_and_boolean_fields(self) -> None:
        """Keep profile validation independent of timeout-policy iteration."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["smoke"][
            "maximumCalibrationRelativeStandardError"
        ] = -1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "maximumCalibrationRelativeStandardError",
        ):
            performance_evidence.validate_contract(contract)

        contract = copy.deepcopy(self.contract)
        contract["profiles"]["smoke"]["baselineRequired"] = "false"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "baselineRequired must be a boolean",
        ):
            performance_evidence.validate_contract(contract)

        contract = copy.deepcopy(self.contract)
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


if __name__ == "__main__":
    unittest.main()
