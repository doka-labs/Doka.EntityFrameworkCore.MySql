"""Contract and source-identity tests for performance evidence."""

from __future__ import annotations

import copy
import json
import math
import subprocess
import tempfile
import unittest
from pathlib import Path

from eng.performance import cli as performance_evidence
from eng.performance import attempts as attempts_module
from eng.performance import contract as contract_module
from eng.tests._performance_fixtures import PerformanceEvidenceFixtureMixin


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CONTRACT_PATH = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"


class PerformanceContractTests(PerformanceEvidenceFixtureMixin, unittest.TestCase):
    """Verify the versioned workload contract and its source identity."""

    def test_contract_covers_every_declared_matrix_dimension(self) -> None:
        """Accept the checked-in contract only when every coverage token has a workload."""
        performance_evidence.validate_contract(self.contract)

    def test_adaptive_operation_batch_must_fit_the_runner_integer_range(self) -> None:
        """Reject a reviewed multiplier the C# runner cannot represent."""
        contract = copy.deepcopy(self.contract)
        contract["workloads"][0]["operationsPerSample"] = 2_147_483_647

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "adaptive operation batch exceeds",
        ):
            performance_evidence.validate_contract(contract)

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

        # A contract bump deliberately precedes the reviewed seed proposal.
        # Once versions agree, however, a partial accepted matrix is invalid.
        if baseline["contractVersion"] == self.contract["contractVersion"]:
            self.assertEqual(
                set(self.contract["requiredTargets"]),
                set(recorded_targets),
                "Every required target must contribute baseline populations.",
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

    def test_adaptive_operation_batching_requires_duration_headroom(self) -> None:
        """Reject a pilot target below the registered measurement floor."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["paired-block"][
            "operationBatchingDurationHeadroomPercent"
        ] = 99

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "operationBatchingDurationHeadroomPercent",
        ):
            performance_evidence.validate_contract(contract)

    def test_fixed_profiles_carry_no_adaptive_batching_knobs(self) -> None:
        """Reject dormant pilot settings on profiles that never consume them."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["scorecard"][
            "operationBatchingDurationHeadroomPercent"
        ] = 101

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "configures adaptive operation-batching knobs",
        ):
            performance_evidence.validate_contract(contract)

    def test_adaptive_operation_batching_requires_pilot_samples(self) -> None:
        """Reject an adaptive profile without scheduler-noise observations."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["paired-block"][
            "operationBatchingPilotSamples"
        ] = 0

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "without pilot samples",
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


class PairedPolicyTests(unittest.TestCase):
    """Prove the paired policy is pre-registered and complete.

    D-026 forbids implementation defaults for this block. A default would let
    an incomplete contract still produce a release verdict, which is exactly
    the situation where two implementations can disagree about what qualified.
    """

    def setUp(self) -> None:
        """Load the shipped contract for each case."""
        self.contract = json.loads(
            CONTRACT_PATH.read_text(encoding="utf-8")
        )

    def test_the_shipped_policy_is_accepted(self) -> None:
        """Keep the checked-in contract inside its own policy shape."""
        policy = contract_module.validate_paired_policy(self.contract)

        self.assertEqual("normalizedMedian", policy["primaryFamily"]["metric"])

    def test_every_declared_field_is_required(self) -> None:
        """Reject the policy when any single registered value is missing."""
        policy = self.contract["pairedPolicy"]
        for block, section in policy.items():
            if not isinstance(section, dict):
                continue
            for field in section:
                with self.subTest(block=block, field=field):
                    broken = json.loads(json.dumps(self.contract))
                    del broken["pairedPolicy"][block][field]
                    with self.assertRaises(contract_module.InvalidEvidenceError):
                        contract_module.validate_paired_policy(broken)

    def test_every_block_is_required(self) -> None:
        """Reject the policy when a whole registered block is missing."""
        for block in list(self.contract["pairedPolicy"]):
            with self.subTest(block=block):
                broken = json.loads(json.dumps(self.contract))
                del broken["pairedPolicy"][block]
                with self.assertRaises(contract_module.InvalidEvidenceError):
                    contract_module.validate_paired_policy(broken)

    def test_a_missing_policy_is_rejected(self) -> None:
        """Reject a contract that declares no paired policy at all."""
        broken = json.loads(json.dumps(self.contract))
        del broken["pairedPolicy"]

        with self.assertRaises(contract_module.InvalidEvidenceError):
            contract_module.validate_paired_policy(broken)

    def test_unknown_methods_and_procedures_are_rejected(self) -> None:
        """Reject a named method the evaluator does not implement.

        An unknown name is worse than a missing one: it reads as a deliberate
        choice while no implementation honors it.
        """
        cases = (
            ("interval", "method", "jackknife"),
            ("interval", "sidedness", "left-sided"),
            ("multipleComparison", "procedure", "sidak"),
            ("sensitivity", "method", "analytical-normal"),
            ("sensitivity", "familyCase", "all-regressions"),
            ("retry", "combination", "pool-samples"),
        )
        for block, field, value in cases:
            with self.subTest(block=block, field=field):
                broken = json.loads(json.dumps(self.contract))
                broken["pairedPolicy"][block][field] = value
                with self.assertRaises(contract_module.InvalidEvidenceError):
                    contract_module.validate_paired_policy(broken)

    def test_a_regression_state_cannot_be_made_retryable(self) -> None:
        """Keep a retry from selecting away a verdict about the code."""
        for state in ("regression", "recalibration-required", "invalid-evidence"):
            with self.subTest(state=state):
                broken = json.loads(json.dumps(self.contract))
                broken["pairedPolicy"]["retry"]["eligibleAttemptStates"] = [state]
                with self.assertRaises(contract_module.InvalidEvidenceError):
                    contract_module.validate_paired_policy(broken)

    def test_every_declared_family_needs_a_practical_budget(self) -> None:
        """Reject a family whose practical bound is absent or unmatched."""
        broken = json.loads(json.dumps(self.contract))
        del broken["pairedPolicy"]["practicalBudgets"]["normalizedP95"]
        with self.assertRaises(contract_module.InvalidEvidenceError):
            contract_module.validate_paired_policy(broken)

        surplus = json.loads(json.dumps(self.contract))
        surplus["pairedPolicy"]["practicalBudgets"]["normalizedMean"] = 1.1
        with self.assertRaises(contract_module.InvalidEvidenceError):
            contract_module.validate_paired_policy(surplus)

    def test_out_of_range_values_are_rejected(self) -> None:
        """Reject values that parse but cannot describe a usable decision."""
        cases = (
            ("interval", "confidenceLevel", 1.0),
            ("interval", "confidenceLevel", 0.0),
            ("interval", "resampleCount", 100),
            ("multipleComparison", "familyWiseErrorRate", 1.0),
            ("blocks", "completeBlocks", 9),
            ("blocks", "completeBlocks", 11),
            ("sensitivity", "minimumPower", 0.79),
            ("sensitivity", "simulationConfidenceLevel", 0.94),
            ("sensitivity", "simulationTrials", 199),
            ("sensitivity", "minimumDetectableBudgetMultiple", 1.0),
        )
        for block, field, value in cases:
            with self.subTest(block=block, field=field, value=value):
                broken = json.loads(json.dumps(self.contract))
                broken["pairedPolicy"][block][field] = value
                with self.assertRaises(contract_module.InvalidEvidenceError):
                    contract_module.validate_paired_policy(broken)

    def test_complete_block_count_must_be_integral(self) -> None:
        """Reject a fractional population no runner can execute."""
        broken = json.loads(json.dumps(self.contract))
        broken["pairedPolicy"]["blocks"]["completeBlocks"] = 10.5

        with self.assertRaises(contract_module.InvalidEvidenceError):
            contract_module.validate_paired_policy(broken)

    def test_sensitivity_dispersion_must_be_positive(self) -> None:
        """Reject a planning model with no usable dispersion ceiling."""
        broken = json.loads(json.dumps(self.contract))
        broken["pairedPolicy"]["sensitivity"][
            "maximumLogRatioStandardDeviation"
        ] = 0

        with self.assertRaises(contract_module.InvalidEvidenceError):
            contract_module.validate_paired_policy(broken)



class DeclaredProcedureTests(unittest.TestCase):
    """Prove the contract admits only procedures the evaluator performs.

    Accepting an alternative nothing implements is worse than omitting it: the
    policy would read as a deliberate methodological choice while every run
    applied the one procedure there ever was, and a reviewer would trust a
    document the evidence does not support.
    """

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))

    def reject(self, path: list[str], value: str) -> None:
        """Set one nested policy value and require the contract to refuse it."""
        contract = json.loads(json.dumps(self.contract))
        node = contract["pairedPolicy"]
        for key in path[:-1]:
            node = node[key]
        node[path[-1]] = value
        with self.assertRaises(contract_module.InvalidEvidenceError):
            contract_module.validate_paired_policy(contract)

    def test_an_unimplemented_interval_method_is_refused(self) -> None:
        """Only the bias-corrected accelerated bootstrap is implemented."""
        self.reject(["interval", "method"], "percentile-bootstrap")

    def test_an_unimplemented_comparison_procedure_is_refused(self) -> None:
        """Only run-wide Holm controls the required target family here."""
        self.reject(["multipleComparison", "procedure"], "benjamini-hochberg")

    def test_an_unimplemented_retry_combination_is_refused(self) -> None:
        """A retry replaces the prior attempt; nothing combines decisions."""
        self.reject(["retry", "combination"], "independent-decision")

    def test_a_narrower_workload_scope_is_refused(self) -> None:
        """The comparison measures every registered workload, or none."""
        self.reject(["primaryFamily", "workloadScope"], "primary-only")

    def test_the_shipped_values_are_the_implemented_ones(self) -> None:
        """State which procedure each declaration actually selects."""
        policy = self.contract["pairedPolicy"]

        self.assertEqual("bca-bootstrap", policy["interval"]["method"])
        self.assertEqual("holm", policy["multipleComparison"]["procedure"])
        self.assertEqual("replace-attempt", policy["retry"]["combination"])
        self.assertEqual("complete-matrix", policy["primaryFamily"]["workloadScope"])


class MeasurementQualityExitContractTests(unittest.TestCase):
    """Prove the driver and the attempt path agree on the typed exit code.

    A calibration that would not settle used to leave the driver as an ordinary
    unhandled exception, which exits 1. The attempt path classifies 1 as
    `regression`: a verdict about the provider, not retryable. A busy runner
    could therefore convict a provider whose code it never finished measuring.
    """

    DRIVER_ROOT = REPOSITORY_ROOT / "benchmarks" / "Doka.EntityFrameworkCore.MySql.Benchmarks"

    def test_the_driver_declares_the_registered_exit_code(self) -> None:
        """Hold the C# constant to the one the attempt path reads."""
        source = (self.DRIVER_ROOT / "MeasurementQualityException.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn(
            f"public const int ExitCode = {contract_module.MEASUREMENT_QUALITY_EXIT_CODE};",
            source,
        )

    def test_the_driver_returns_it_rather_than_falling_through(self) -> None:
        """Reject an entry point that lets the typed failure exit as 1."""
        program = (self.DRIVER_ROOT / "Program.cs").read_text(encoding="utf-8")

        self.assertIn("catch (MeasurementQualityException", program)
        self.assertIn("return MeasurementQualityException.ExitCode;", program)
        # The typed handler has to precede the general one, or it never runs.
        self.assertLess(
            program.index("catch (MeasurementQualityException"),
            program.index("catch (Exception exception)"),
        )

    def test_that_code_is_retryable_and_is_not_a_verdict(self) -> None:
        """Prove the classification the exit code buys."""
        state = attempts_module.classify_exit_code(
            contract_module.MEASUREMENT_QUALITY_EXIT_CODE
        )

        self.assertEqual("measurement-inconclusive", state)
        self.assertTrue(attempts_module.is_retryable(state))
        self.assertEqual("regression", attempts_module.classify_exit_code(1))

    def test_the_calibration_check_consults_the_policy(self) -> None:
        """Reject a driver that enforces regardless of the profile."""
        runner = (self.DRIVER_ROOT / "PerformanceWorkloadRunner.cs").read_text(
            encoding="utf-8"
        )
        calibration = runner[runner.index("calibrationRelativeStandardError >") :][:1200]

        self.assertIn("measurementQualityPolicy", calibration)
        self.assertIn("MeasurementQualityException", calibration)
        self.assertIn("Measurement quality observation", calibration)


class PairedPolicyEnforcementTests(unittest.TestCase):
    """Prove every registered paired value has a consumer that can reject.

    A contract field nobody compares against describes nothing. Each case here
    changes one registered value and requires the contract to refuse it, which
    is the only evidence that the value is load-bearing rather than decorative.
    """

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))

    def reject(self, mutate) -> None:
        """Apply one mutation and require the contract to refuse it."""
        contract = json.loads(json.dumps(self.contract))
        mutate(contract)
        with self.assertRaises(contract_module.InvalidEvidenceError):
            contract_module.validate_paired_policy(contract)

    def test_the_shipped_contract_is_accepted(self) -> None:
        """Establish the baseline the rejections are measured against."""
        contract_module.validate_paired_policy(self.contract)

    def test_a_start_population_the_profile_does_not_use_is_refused(self) -> None:
        """Bind the registered starting population to the profile."""
        def mutate(contract):
            contract["pairedPolicy"]["blocks"]["startingSamplesPerSidePerBlock"] = 1337

        self.reject(mutate)

    def test_a_start_below_the_valid_sample_floor_is_refused(self) -> None:
        """Refuse a start the profile would not accept as a measurement.

        Starting small is what keeps a paired run affordable; starting below
        the floor would produce blocks the profile itself calls invalid.
        """
        def mutate(contract):
            profile = contract["profiles"]["paired-block"]
            profile["measurementSamples"] = profile["minimumValidSamples"] - 1
            contract["pairedPolicy"]["blocks"]["startingSamplesPerSidePerBlock"] = (
                profile["minimumValidSamples"] - 1
            )

        self.reject(mutate)

    def test_the_error_budget_drives_the_population(self) -> None:
        """Prove the block profile starts small and extends on precision.

        A fixed larger population spends the same wall clock on a workload
        that converged in a quarter of it, and a paired run pays that cost
        twice per block.
        """
        profile = self.contract["profiles"]["paired-block"]

        self.assertEqual(profile["minimumValidSamples"], profile["measurementSamples"])
        self.assertEqual(
            profile["minimumValidSamples"], profile["expensiveMeasurementSamples"]
        )
        self.assertGreater(profile["maximumMeasurementSampleMultiplier"], 1)
        self.assertGreater(profile["maximumRelativeStandardError"], 0)

    def test_a_block_profile_that_is_not_registered_is_refused(self) -> None:
        """Refuse a policy that names a profile the contract does not define."""
        def mutate(contract):
            contract["pairedPolicy"]["blocks"]["profile"] = "no-such-profile"

        self.reject(mutate)

    def test_a_pattern_that_favors_one_side_is_refused(self) -> None:
        """Refuse an execution order that measures one provider more often."""
        def mutate(contract):
            contract["pairedPolicy"]["executionOrder"]["blockPatterns"] = [
                "A-B-B-B",
                "B-A-A-A",
            ]

        self.reject(mutate)

    def test_patterns_that_always_start_on_one_side_are_refused(self) -> None:
        """Refuse an order in which the starting side cannot alternate."""
        def mutate(contract):
            contract["pairedPolicy"]["executionOrder"]["blockPatterns"] = [
                "A-B-B-A"
            ]

        self.reject(mutate)

    def test_a_fixed_starting_side_is_refused(self) -> None:
        """Refuse a policy that gives one provider every warm-up advantage."""
        def mutate(contract):
            order = contract["pairedPolicy"]["executionOrder"]
            order["startingSideAlternatesPerBlock"] = False

        self.reject(mutate)

    def test_a_profile_above_the_run_budget_is_refused(self) -> None:
        """Refuse a block ceiling the paired run budget cannot contain."""
        def mutate(contract):
            budget = contract["pairedPolicy"]["durations"]["maximumPairedRunSeconds"]
            contract["profiles"]["paired-block"]["maximumTotalDurationSeconds"] = (
                budget + 1
            )

        self.reject(mutate)

    def test_a_workload_ceiling_above_the_registered_one_is_refused(self) -> None:
        """Refuse a per-workload ceiling above what the policy permits."""
        def mutate(contract):
            ceiling = contract["pairedPolicy"]["durations"]["maximumWorkloadSeconds"]
            contract["profiles"]["paired-block"][
                "maximumWorkloadDurationSeconds"
            ] = ceiling + 1

        self.reject(mutate)



if __name__ == "__main__":
    unittest.main()
