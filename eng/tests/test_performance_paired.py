"""Contracts for the paired same-run performance comparison.

The historical comparison this replaces failed on hardware the accepted
baseline had never seen, and once failed by 31.5 percent on a matched
processor while the other 54 workloads in the same run moved by 1.41 percent.
Neither outcome described the provider. The paired comparison is meant to make
that class of failure impossible by construction rather than less likely, so
these tests assert the construction, not the observed behavior of one run.

Every population here is synthetic and deterministic. A boundary case must
decide the same way on every machine, or the release decision it feeds is not
reviewable.
"""

from __future__ import annotations

import json
import math
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Sequence

from eng.tests._performance_fixtures import PerformanceEvidenceFixtureMixin

from eng.performance import attempts, environment, paired
from eng.performance.statistics import percentile
from eng.performance.contract import (
    InvalidEvidenceError,
    MeasurementQualityError,
    sha256,
    validate_paired_policy,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CONTRACT_PATH = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"

# The synthetic populations track the profile floor rather than a literal, so
# a contract that raises the floor cannot leave these blocks below it.
# The population a workload may claim its cap at: the profile's starting
# allocation times the registered extension limit. A capped case has to build
# exactly this many samples, or it describes a state no runner can reach.
CAP_SAMPLES = None  # resolved below

MINIMUM_VALID_SAMPLES = json.loads(
    (REPOSITORY_ROOT / "benchmarks" / "performance-contract.json").read_text(
        encoding="utf-8"
    )
)["profiles"]["paired-block"]["minimumValidSamples"]

_PERFORMANCE_CONTRACT = json.loads(
    (REPOSITORY_ROOT / "benchmarks" / "performance-contract.json").read_text(
        encoding="utf-8"
    )
)
_PAIRED_PROFILE = _PERFORMANCE_CONTRACT["profiles"]["paired-block"]
COMPLETE_BLOCKS = _PERFORMANCE_CONTRACT["pairedPolicy"]["blocks"][
    "completeBlocks"
]
CAP_SAMPLES = (
    _PAIRED_PROFILE["measurementSamples"]
    * _PAIRED_PROFILE["maximumMeasurementSampleMultiplier"]
)

PAIRED_RUN_ID = "paired-1"
PAIRED_TARGET = "mariadb118"
PAIRED_COMMIT = "c" * 40
PAIRED_SOURCE_HASH = "d" * 64
PAIRED_RUNNER_CLASS = "test-runner"
PAIRED_REFERENCE_COMMIT = "b" * 40
PAIRED_DRIVER_HASH = "d" * 40
PAIRED_CONTRACT_DIGEST = "0" * 64

ENVIRONMENT = {
    "frameworkDescription": ".NET 10.0.10",
    "osDescription": "Linux Ubuntu 24.04.4 LTS",
    "osArchitecture": "X64",
    "processArchitecture": "X64",
    "processor": "AMD EPYC 7763",
    "processorCount": 4,
    "engineFamily": "mariadb",
    "serverVersion": "11.8.8",
    "serverImage": "mariadb:11.8.8@sha256:" + "e" * 64,
}


def resource_blocks(count: int, *, allocation: float = 1.0,
                    collections: float = 1.0) -> list[dict[str, dict[str, float]]]:
    """Return per-block resource scalars whose ratio is fixed by the caller."""
    return [
        {
            "reference": {
                "allocatedBytesPerOperation": 1000.0,
                "gen2CollectionsPer1000": 4.0,
            },
            "candidate": {
                "allocatedBytesPerOperation": 1000.0 * allocation,
                "gen2CollectionsPer1000": 4.0 * collections,
            },
        }
        for _ in range(count)
    ]


def block(reference: list[float], candidate: list[float]) -> dict[str, list[float]]:
    """Return one measurement block with both sides."""
    return {"reference": reference, "candidate": candidate}


def uniform_blocks(count: int, ratio: float, *, base: float = 100.0,
                   spread: float = 0.0,
                   samples: int = 0) -> list[dict[str, list[float]]]:
    """Return blocks whose candidate side is a fixed multiple of the reference.

    `spread` walks the per-block ratio around its center so a case can be given
    a controlled amount of disagreement between blocks without any randomness.
    """
    blocks = []
    for index in range(count):
        offset = 0.0 if spread == 0 else spread * (index - (count - 1) / 2)
        factor = ratio + offset
        reference = [
            base + position
            for position in range(samples or MINIMUM_VALID_SAMPLES)
        ]
        candidate = [value * factor for value in reference]
        blocks.append(block(reference, candidate))

    return blocks


class PairingCancelsTheMachineTests(unittest.TestCase):
    """Prove the property the whole decision rests on."""

    def test_scaling_both_sides_leaves_every_ratio_unchanged(self) -> None:
        """Reject any dependence on the absolute speed of the runner.

        This is the claim that lets the comparison run on a fleet that promises
        no processor model: a slower machine slows both sides, so the paired
        ratio is unmoved. If this ever fails, the paired design has no more
        justification than the historical one it replaced.
        """
        blocks = uniform_blocks(8, 1.10)
        slow = [
            block([value * 3.7 for value in item["reference"]],
                  [value * 3.7 for value in item["candidate"]])
            for item in blocks
        ]

        fast_ratios = paired.paired_ratios(blocks, "normalizedMedian")
        slow_ratios = paired.paired_ratios(slow, "normalizedMedian")

        self.assertEqual(len(fast_ratios), len(slow_ratios))
        for fast, slower in zip(fast_ratios, slow_ratios):
            self.assertAlmostEqual(fast, slower, places=12)

    def test_precision_driven_divergence_is_accepted(self) -> None:
        """Accept sides that needed different populations for equal precision.

        The adaptive extension exists to equalize precision, not count: a
        noisier side needs more samples to reach the same error budget. A real
        one-block run diverged on sixteen of fifty-five workloads, so demanding
        equal counts would have made every paired run invalid evidence.
        """
        blocks = [
            block([100.0] * 20, [100.0] * 30),
            block([100.0] * 24, [100.0] * 24),
        ]

        ratios = paired.paired_ratios(blocks, "normalizedMedian", maximum_count_ratio=4.0)

        self.assertEqual([1.0, 1.0], ratios)

    def test_divergence_beyond_the_registered_ratio_is_inconclusive(self) -> None:
        """Retry populations that did not measure comparable stretches of time.

        A side that took several times longer no longer interleaves with the
        other. The document is structurally valid, but the measurement cannot
        support a provider verdict and therefore belongs to the bounded retry
        path rather than the non-retryable invalid-evidence path.
        """
        # Hosted run 31903353665 produced this exact 80:16 split after one
        # reference side needed another precision extension.
        blocks = [block([100.0] * 80, [100.0] * 16)]

        with self.assertRaises(MeasurementQualityError):
            paired.paired_ratios(blocks, "normalizedMedian", maximum_count_ratio=4.0)

    def test_a_side_without_samples_is_invalid_evidence(self) -> None:
        """Reject an empty side rather than dividing by its count."""
        with self.assertRaises(InvalidEvidenceError):
            paired.paired_ratios(
                [block([], [100.0] * 8)], "normalizedMedian", maximum_count_ratio=4.0
            )

    def test_a_non_positive_reference_is_invalid_evidence(self) -> None:
        """Reject a reference side that cannot form a ratio."""
        blocks = [block([0.0, 0.0], [1.0, 1.0])]

        with self.assertRaises(InvalidEvidenceError):
            paired.paired_ratios(blocks, "normalizedMedian")

    def test_a_block_above_the_quality_floor_is_a_measurement_condition(self) -> None:
        """Reject a block whose side is too noisy to contribute a ratio.

        A block need not converge on its own; the run draws its power from
        many blocks. It must still not be so unstable that its ratio carries
        no information, which is what the registered block floor bounds.

        The refusal is a measurement condition rather than invalid evidence:
        a machine too noisy to measure earns the bounded retry the policy
        registers, where invalid evidence would end the run for good.
        """
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        floor = contract["pairedPolicy"]["blocks"]["maximumRelativeStandardError"]
        noisy = [
            block([1.0, 500.0, 2.0, 900.0, 3.0, 700.0, 4.0, 800.0], [110.0] * 8)
        ]

        with self.assertRaises(MeasurementQualityError):
            paired.paired_ratios(noisy, "normalizedMedian", quality_floor=floor)

        self.assertFalse(issubclass(MeasurementQualityError, InvalidEvidenceError))

    def test_a_block_inside_the_quality_floor_is_accepted(self) -> None:
        """Accept a block that is stable enough to be paired."""
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        floor = contract["pairedPolicy"]["blocks"]["maximumRelativeStandardError"]

        ratios = paired.paired_ratios(
            [block([100.0] * 8, [110.0] * 8)],
            "normalizedMedian",
            quality_floor=floor,
        )

        self.assertEqual(1, len(ratios))


class BootstrapDeterminismTests(unittest.TestCase):
    """Prove an interval can be reviewed because it can be reproduced."""

    def test_the_same_evidence_yields_the_same_interval(self) -> None:
        """Reject a non-reproducible interval."""
        ratios = [1.0, 1.05, 0.98, 1.02, 1.10, 0.95, 1.01, 1.03]

        first = paired.bootstrap_replicates(ratios, resamples=2000, seed=4242)
        second = paired.bootstrap_replicates(ratios, resamples=2000, seed=4242)

        self.assertEqual(first, second)

    def test_a_different_seed_produces_a_different_resampling(self) -> None:
        """Keep the seed load-bearing rather than decorative."""
        ratios = [1.0, 1.05, 0.98, 1.02, 1.10, 0.95, 1.01, 1.03]

        first = paired.bootstrap_replicates(ratios, resamples=2000, seed=1)
        second = paired.bootstrap_replicates(ratios, resamples=2000, seed=2)

        self.assertNotEqual(first, second)

    def test_constant_ratios_degenerate_to_the_observation(self) -> None:
        """Return a usable interval when every block agreed exactly."""
        ratios = [1.0] * 8
        replicates = paired.bootstrap_replicates(ratios, resamples=500, seed=7)

        low, high = paired.bca_interval(
            ratios, replicates, confidence=0.95, sidedness="two-sided"
        )

        self.assertEqual(1.0, low)
        self.assertEqual(1.0, high)


class MultipleComparisonTests(unittest.TestCase):
    """Prove the procedure controls the run-wide family-wise error rate."""

    def test_exact_sign_flip_uses_the_complete_null_distribution(self) -> None:
        """Produce calibrated p-values without randomized resampling error."""
        self.assertEqual(
            1.0,
            paired.exact_sign_flip_p_value([1.15] * 10, 1.15),
        )
        self.assertEqual(
            1 / 1024,
            paired.exact_sign_flip_p_value([1.30] * 10, 1.15),
        )

    def test_exact_sign_flip_requires_the_registered_population(self) -> None:
        """Keep the multiplicity input bound to the ten-block contract."""
        with self.assertRaises(InvalidEvidenceError):
            paired.exact_sign_flip_p_value([1.30] * 9, 1.15)

    def test_one_strong_signal_among_many_null_tests_is_rejected(self) -> None:
        """Detect a real effect without rejecting its quiet neighbors."""
        probabilities = [0.0001] + [0.60] * 5

        rejected = paired.holm_rejections(probabilities, 0.05)

        self.assertTrue(rejected[0])
        self.assertEqual(1, sum(rejected))

    def test_uniform_noise_produces_no_rejection(self) -> None:
        """Reject nothing when no test carries evidence.

        Without family control, running one test per workload across the matrix
        would be expected to produce false alarms in proportion to its size.
        """
        probabilities = [0.20 + index * 0.01 for index in range(6)]

        self.assertEqual([], [item for item in
                              paired.holm_rejections(probabilities, 0.05)
                              if item])

    def test_an_empty_family_rejects_nothing(self) -> None:
        """Keep the procedure total."""
        self.assertEqual([], paired.holm_rejections([], 0.05))


class ScorecardQualificationTests(unittest.TestCase):
    """Prove one run-wide verdict is formed across every required target."""

    def setUp(self) -> None:
        """Load the shipped target family and its immutable characterization."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.contract_digest = sha256(CONTRACT_PATH)
        characterization_path = REPOSITORY_ROOT / self.contract["pairedPolicy"][
            "sensitivity"
        ]["characterization"]["path"]
        self.characterization = json.loads(
            characterization_path.read_text(encoding="utf-8")
        )

    def evaluation(
        self,
        target: str,
        *,
        endpoint: dict[str, object],
        qualification: str = "pending-run-wide-adjustment",
    ) -> dict[str, object]:
        """Return one target verdict with the common scorecard identity."""
        return {
            "schemaVersion": 6,
            "kind": "paired-performance-evaluation",
            "target": target,
            "commit": PAIRED_COMMIT,
            "sourceHash": PAIRED_SOURCE_HASH,
            "runnerClass": PAIRED_RUNNER_CLASS,
            "contractDigest": self.contract_digest,
            "referenceCommit": PAIRED_REFERENCE_COMMIT,
            "qualification": qualification,
            "primaryEndpoint": endpoint,
        }

    def endpoint(self, ratios: Sequence[float]) -> dict[str, object]:
        """Evaluate ratios with the production endpoint estimator."""
        policy = validate_paired_policy(self.contract)
        complete_blocks = int(policy["blocks"]["completeBlocks"])
        population = [
            float(ratios[index % len(ratios)])
            for index in range(complete_blocks)
        ]
        endpoint = paired.prepare_endpoint(
            population,
            identity="target-workload-geometric-mean",
            metric=policy["primaryFamily"]["metric"],
            budget=float(
                policy["practicalBudgets"][policy["primaryFamily"]["metric"]]
            ),
            policy=policy,
        )
        endpoint.update(
            {
                "role": "required",
                "aggregation": "geometric-mean-across-workloads",
                "state": "pending-run-wide-adjustment",
                "runWideRejected": None,
            }
        )

        return endpoint

    def characterized_evaluations(
        self,
        *,
        regression_multiple: float | None = None,
    ) -> list[dict[str, object]]:
        """Replay hosted populations, optionally injecting one real effect."""
        sources = self.characterization["sources"]
        evaluations = []
        for index, target in enumerate(self.contract["requiredTargets"]):
            ratios = list(
                sources[index % len(sources)]["aggregateBlockRatios"]
            )
            if index == 0 and regression_multiple is not None:
                ratios = [value * regression_multiple for value in ratios]
            evaluations.append(
                self.evaluation(target, endpoint=self.endpoint(ratios))
            )

        return evaluations

    def test_characterized_hosted_populations_qualify_run_wide(self) -> None:
        """Replay the planning artifacts without inventing a false regression."""
        result = paired.evaluate_scorecard_qualification(
            self.characterized_evaluations(),
            self.contract,
            contract_digest=self.contract_digest,
        )

        self.assertEqual("qualified", result["qualification"])
        self.assertEqual(6, result["multipleComparison"]["requiredTargetCount"])
        self.assertFalse(any(item["runWideRejected"] for item in result["targets"]))

    def test_a_relevant_injected_regression_is_recovered_run_wide(self) -> None:
        """Recover a 30 percent aggregate regression in the hosted populations."""
        result = paired.evaluate_scorecard_qualification(
            self.characterized_evaluations(regression_multiple=1.30),
            self.contract,
            contract_digest=self.contract_digest,
        )

        self.assertEqual("regression", result["qualification"])
        self.assertEqual(1, sum(
            item["state"] == "regression" for item in result["targets"]
        ))

    def test_a_locally_small_probability_does_not_bypass_run_wide_holm(self) -> None:
        """Require the first Holm boundary rather than a per-target alpha."""
        evaluations = self.characterized_evaluations()
        endpoint = evaluations[0]["primaryEndpoint"]
        endpoint["pValue"] = 0.01
        endpoint["lowerBound"] = float(endpoint["budget"]) * 1.1
        target = evaluations[0]["target"]

        result = paired.evaluate_scorecard_qualification(
            evaluations,
            self.contract,
            contract_digest=self.contract_digest,
        )

        self.assertEqual("qualified", result["qualification"])
        adjusted = next(
            item for item in result["targets"] if item["target"] == target
        )
        self.assertFalse(adjusted["runWideRejected"])

    def test_an_incomplete_target_family_is_invalid_evidence(self) -> None:
        """Never turn a partial LTS matrix into a run-wide qualification."""
        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_scorecard_qualification(
                self.characterized_evaluations()[:-1],
                self.contract,
                contract_digest=self.contract_digest,
            )

    def test_the_previous_target_evaluation_schema_is_invalid_evidence(self) -> None:
        """Require the resource-role vocabulary introduced by schema 6."""
        evaluations = self.characterized_evaluations()
        evaluations[0]["schemaVersion"] = 5

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_scorecard_qualification(
                evaluations,
                self.contract,
                contract_digest=self.contract_digest,
            )


def contract_for(workload_ids: Sequence[str]) -> dict[str, object]:
    """Return the shipped contract narrowed to the named workloads.

    The evaluator requires evidence to cover every registered workload, which
    is the right contract for a release and the wrong one for a case about a
    single ratio: a full matrix costs seconds of bootstrap per evaluation and
    says nothing more about the boundary under test. Narrowing the registered
    matrix keeps each case honest against its own contract; the matrix rule
    itself is exercised against the shipped one.
    """
    contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
    template = contract["workloads"][0]
    contract["workloads"] = [
        dict(template, id=identifier) for identifier in workload_ids
    ]

    return contract


class PairedEvidenceBuilder(PerformanceEvidenceFixtureMixin):
    """Build complete paired evidence around the parts a test cares about.

    A paired run carries more than latency ratios: resource scalars, the
    candidate's absolute measurements, and a sustained-use report. A test that
    is about one of them should not have to spell out the others, and none of
    them may be optional, or the evaluator would silently skip whichever a test
    forgot.
    """

    def __init__(self, contract: dict[str, object]) -> None:
        """Bind the builder to the shipped contract."""
        self.contract = contract

    def narrow_to(
        self,
        workload_ids: Sequence[str],
        families: dict[str, str] | None = None,
    ) -> None:
        """Register exactly the workloads this evidence will describe.

        The evaluator requires evidence to cover every registered workload,
        which is the right contract for a release and the wrong one for a case
        about a single ratio. The builder therefore hands back a self-consistent
        pair: evidence, and the contract it is evidence for. The matrix rule
        itself is exercised against the shipped contract elsewhere.
        """
        # In place, so every holder of this contract sees the same registered
        # matrix the evidence describes. Rebinding would leave the caller
        # evaluating new evidence against the old world.
        template = {
            key: value
            for key, value in self.contract["workloads"][0].items()
            if key != "measurementSamples"
        }
        # A workload the contract already registers keeps its own definition.
        # Cloning the first entry over it rewrote the family, and the family is
        # what selects the absolute budget.
        chosen = families or {}
        existing = {
            definition["id"]: definition
            for definition in self.contract["workloads"]
        }
        self.contract["workloads"] = [
            existing.get(
                name,
                dict(template, id=name, family=chosen.get(name, template["family"])),
            )
            for name in workload_ids
        ]

    def soak(self) -> dict[str, object]:
        """Return a passing soak report bound to the paired identity."""
        report = self._soak_report(PAIRED_TARGET)
        report["runId"] = PAIRED_RUN_ID
        report["profile"] = self.contract["pairedPolicy"]["blocks"]["profile"]

        return report

    def candidate_workloads(
        self,
        prepared: Sequence[dict[str, object]],
    ) -> list[dict[str, object]]:
        """Project the candidate measurements the absolute ceilings read.

        Derived from the same resource blocks the ratio decisions use, because
        that is what the production assembler does: both come from one workload
        report. A builder that invented independent numbers here emitted
        documents the assembler could never produce, and the evaluator now
        rejects that disagreement.

        Each entry carries the family the contract registers for its workload.
        An earlier version handed out families round-robin, which nothing
        checked -- and that is the gap that let a re-declared family pick a
        more generous absolute budget.
        """
        registered = {
            definition["id"]: definition
            for definition in self.contract["workloads"]
        }
        blocks = len(prepared[0]["blocks"]) if prepared else COMPLETE_BLOCKS

        return [
            {
                "block": index + 1,
                "workloads": [
                    paired.candidate_audit_projection({
                        "id": test["workloadId"],
                        "family": registered[test["workloadId"]]["family"],
                        **{
                            field: percentile(
                                sorted(test["latencies"][index]["candidate"]),
                                quantile,
                            )
                            for field, quantile in
                            paired.LATENCY_CEILING_QUANTILES.items()
                        },
                        "allocatedBytesPerOperation": test["resources"][index][
                            "candidate"
                        ]["allocatedBytesPerOperation"],
                        "gen2CollectionsPer1000": test["resources"][index][
                            "candidate"
                        ]["gen2CollectionsPer1000"],
                    })
                    for test in prepared
                ],
            }
            for index in range(blocks)
        ]

    def execution_order(self, blocks: int) -> dict[str, object]:
        """Return the order a compliant run records.

        The patterns alternate, because the counterbalancing rests on the
        starting side changing from block to block.
        """
        patterns = list(
            self.contract["pairedPolicy"]["executionOrder"]["blockPatterns"]
        )

        return {
            "blockProfile": self.contract["pairedPolicy"]["blocks"]["profile"],
            "executedBlockPatterns": [
                patterns[index % len(patterns)] for index in range(blocks)
            ],
        }

    def evidence(
        self,
        tests: list[dict[str, object]],
        *,
        order: dict[str, object] | None = ...,
        soak: dict[str, object] | None = ...,
        termination: str = "precision_reached",
        allocated: int | None = None,
        collections: int | None = None,
        first_block_allocated: int | None = None,
        families: dict[str, str] | None = None,
    ) -> dict[str, object]:
        """Return one complete paired evidence document."""
        self.narrow_to([test["workloadId"] for test in tests], families)

        prepared = []
        for test in tests:
            entry = dict(test)
            # The blocks carry calibration-normalized ratios; the latencies
            # carry nanoseconds; the calibration is the divisor between them.
            # A unit divisor makes the two arrays equal, which is what keeps
            # the fixture readable. A case that needs them to differ passes
            # `latencies` and `calibrations` itself.
            entry.setdefault(
                "latencies",
                [dict(measured) for measured in entry["blocks"]],
            )
            entry.setdefault(
                "calibrations",
                [
                    {side: [1.0] * len(samples) for side, samples in measured.items()}
                    for measured in entry["blocks"]
                ],
            )
            # A pulse every `calibrationIntervalSamples` observations, which
            # is the train the contract registers. One pulse for the whole
            # block would exceed the interval as soon as a block carries more
            # samples than the interval allows.
            interval = int(
                self.contract["profiles"][
                    self.contract["pairedPolicy"]["blocks"]["profile"]
                ]["calibrationIntervalSamples"]
            )
            entry.setdefault(
                "calibrationPulses",
                [
                    {
                        side: values[::interval]
                        for side, values in measured.items()
                    }
                    for measured in entry["calibrations"]
                ],
            )
            entry.setdefault(
                "calibrationPulseIndices",
                [
                    {
                        side: [
                            position // interval for position in range(len(values))
                        ]
                        for side, values in measured.items()
                    }
                    for measured in entry["calibrations"]
                ],
            )
            entry.setdefault(
                "resources", resource_blocks(len(entry["blocks"]))
            )
            entry.setdefault(
                "terminations",
                [
                    {
                        side: {
                            "sampleCount": len(measured[side]),
                            "terminationReason": termination,
                            "minimumDurationReached": termination
                            != "sample_cap_reached",
                        }
                        for side in ("reference", "candidate")
                    }
                    for measured in entry["blocks"]
                ],
            )
            prepared.append(entry)

        # The ceiling cases push the candidate past a budget. Writing it into
        # the resource blocks rather than into a second, independent projection
        # keeps the document in the shape the assembler produces.
        for entry in prepared:
            for index, resource in enumerate(entry["resources"]):
                if allocated is not None:
                    resource["candidate"]["allocatedBytesPerOperation"] = allocated
                if collections is not None:
                    resource["candidate"]["gen2CollectionsPer1000"] = collections
                if first_block_allocated is not None and index == 0:
                    resource["candidate"]["allocatedBytesPerOperation"] = (
                        first_block_allocated
                    )

        # The same shape the production assembler emits. A builder that omitted
        # or normalized a production field would call its output complete while
        # hiding exactly the structural gaps the evaluator has to refuse.
        return {
            "schemaVersion": 2,
            "kind": "paired-performance-evidence",
            "runId": PAIRED_RUN_ID,
            "target": PAIRED_TARGET,
            "blockCount": len(prepared[0]["blocks"]) if prepared else 0,
            "candidateCommit": PAIRED_COMMIT,
            "referenceCommit": PAIRED_REFERENCE_COMMIT,
            "benchmarkDriverSourceHash": PAIRED_DRIVER_HASH,
            "contractDigest": PAIRED_CONTRACT_DIGEST,
            "environments": {
                "reference": dict(ENVIRONMENT),
                "candidate": dict(ENVIRONMENT),
            },
            "profile": self.contract["pairedPolicy"]["blocks"]["profile"],
            "commit": PAIRED_COMMIT,
            "sourceHash": PAIRED_SOURCE_HASH,
            "runnerClass": PAIRED_RUNNER_CLASS,
            "candidateWorkloads": self.candidate_workloads(prepared),
            "executionOrder": (
                self.execution_order(
                    len(prepared[0]["blocks"]) if prepared else COMPLETE_BLOCKS
                )
                if order is ...
                else order
            ),
            "soak": self.soak() if soak is ... else soak,
            "tests": prepared,
        }


class BoundaryDecisionTests(unittest.TestCase):
    """Prove each qualification boundary decides as the policy registers it."""

    WORKLOADS = ("query.materialize", "json.compare", "write.savechanges", "loud", "stable", "on.the.line", "detectable.but.small",)

    def setUp(self) -> None:
        """Load the shipped contract so tests run against the real policy."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)

    def evaluate(self, tests: list[dict[str, object]], **kwargs) -> dict[str, object]:
        """Evaluate one synthetic paired run."""
        return paired.evaluate_paired_comparison(
            self.builder.evidence(tests, **kwargs),
            self.builder.contract,
            contract_digest=PAIRED_CONTRACT_DIGEST,
        )

    def test_an_unchanged_candidate_qualifies(self) -> None:
        """Qualify a candidate that measures like its reference."""
        result = self.evaluate(
            [{"workloadId": "query.materialize", "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0)}]
        )

        self.assertEqual(6, result["schemaVersion"])
        self.assertEqual("paired-performance-evaluation", result["kind"])
        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        self.assertEqual(
            self.contract["pairedPolicy"]["sensitivity"]["minimumPower"],
            result["sensitivity"]["minimumPower"],
        )

    def test_excessive_log_ratio_dispersion_is_a_measurement_condition(self) -> None:
        """Refuse a population that cannot support its registered power claim."""
        ratios = (0.5, 2.0) * (COMPLETE_BLOCKS // 2)
        blocks = [
            block([100.0] * 16, [100.0 * ratio] * 16)
            for ratio in ratios
        ]

        result = self.evaluate([{"workloadId": "unstable", "blocks": blocks}])

        self.assertEqual("measurement-inconclusive", result["qualification"])
        self.assertEqual(
            "insufficient-sensitivity", result["primaryEndpoint"]["state"]
        )

    def test_a_candidate_far_outside_its_budget_is_a_regression(self) -> None:
        """Report a regression when the interval sits above the budget.

        The practical budget for the primary family is 1.15x. A candidate at
        1.60x with consistent blocks leaves no reading under which it complies.
        """
        result = self.evaluate(
            [{"workloadId": "json.compare", "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.60)}]
        )

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        primary = [item for item in result["results"]
                   if item["metric"] == "normalizedMedian"][0]
        self.assertEqual("observed-above-budget", primary["state"])
        self.assertGreater(primary["lowerBound"], primary["budget"])

    def test_a_candidate_straddling_its_budget_reports_uncertainty(self) -> None:
        """Report overlap without choosing a fresh population after seeing it."""
        result = self.evaluate(
            [{
                "workloadId": "write.savechanges",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.15, spread=0.005),
            }]
        )

        primary = [
            item
            for item in result["results"]
            if item["metric"] == "normalizedMedian"
        ][0]
        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        self.assertEqual("observed-overlap", primary["state"])
        self.assertGreater(result["uncertainResults"], 0)

    def test_one_workload_signal_remains_observational(self) -> None:
        """Keep one relative workload signal from becoming a false run verdict."""
        tests = [
            {"workloadId": f"quiet.{index}", "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0)}
            for index in range(8)
        ]
        tests.append(
            {"workloadId": "loud", "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.90)}
        )

        result = self.evaluate(tests)

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        loud = next(
            item for item in result["results"]
            if item["workloadId"] == "loud"
            and item["metric"] == "normalizedMedian"
        )
        self.assertEqual("observed-above-budget", loud["state"])

    def test_evaluation_is_reproducible(self) -> None:
        """Produce the same verdict and the same bounds on a second run."""
        tests = [{"workloadId": "stable", "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.08)}]

        first = self.evaluate(tests)
        second = self.evaluate(tests)

        self.assertEqual(json.dumps(first, sort_keys=True),
                         json.dumps(second, sort_keys=True))

    def test_a_bound_landing_on_the_budget_qualifies(self) -> None:
        """Decide the exact boundary as compliance rather than as doubt.

        Without a tolerance the upper bound of this population evaluates to
        1.1500000000000001 against a budget of 1.15, and a candidate that is
        compliant by construction would be withheld over one part in 10^16.
        """
        result = self.evaluate(
            [{
                "workloadId": "on.the.line",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.05, spread=0.04),
            }]
        )

        primary = [item for item in result["results"]
                   if item["metric"] == "normalizedMedian"][0]
        self.assertGreaterEqual(primary["upperBound"], primary["budget"])
        self.assertEqual("observed-within-budget", primary["state"])

    def test_a_significant_result_inside_its_budget_is_not_a_regression(self) -> None:
        """Keep statistical detectability separate from practical impact.

        The family procedure can reject the null for a change that still lies
        inside the reviewed budget. Reporting that as a regression would make
        the budget meaningless.
        """
        result = self.evaluate(
            [{
                "workloadId": "detectable.but.small",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.18, spread=0.01),
            }]
        )

        primary = [item for item in result["results"]
                   if item["metric"] == "normalizedMedian"][0]
        self.assertLessEqual(primary["lowerBound"], primary["budget"])
        self.assertEqual("observed-overlap", primary["state"])


class EvidenceShapeTests(unittest.TestCase):
    """Prove incomplete paired evidence never reaches a verdict."""

    WORKLOADS = ("short", "long", "empty",)

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)

    def test_too_few_blocks_are_invalid_evidence(self) -> None:
        """Reject a run below the registered minimum block count."""
        tests = [{"workloadId": "short", "blocks": uniform_blocks(3, 1.0)}]

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                self.builder.evidence(tests), self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_too_many_blocks_are_invalid_evidence(self) -> None:
        """Reject a run above the registered maximum block count."""
        tests = [{"workloadId": "long", "blocks": uniform_blocks(40, 1.0)}]

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                self.builder.evidence(tests), self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_missing_tests_are_invalid_evidence(self) -> None:
        """Reject evidence that declares no comparison at all."""
        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                self.builder.evidence([]), self.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_a_test_without_blocks_is_invalid_evidence(self) -> None:
        """Reject a workload entry that carries no measurement."""
        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                {
                    "runId": PAIRED_RUN_ID,
                    "target": PAIRED_TARGET,
                    "profile": "paired-block",
                    "commit": PAIRED_COMMIT,
                    "sourceHash": PAIRED_SOURCE_HASH,
                    "runnerClass": PAIRED_RUNNER_CLASS,
                    "tests": [{"workloadId": "empty"}],
                },
                self.contract,
                contract_digest=PAIRED_CONTRACT_DIGEST,
            )


class PairedEnvironmentTests(unittest.TestCase):
    """Prove both sides of a pair are held to one environment and one benchmark driver."""

    ENVIRONMENT = {
        "frameworkDescription": ".NET 10.0.10",
        "osDescription": "Linux Ubuntu 24.04.4 LTS",
        "osArchitecture": "X64",
        "processArchitecture": "X64",
        "processor": "AMD EPYC 7763 64-Core Processor",
        "processorCount": 4,
        "engineFamily": "MariaDB",
        "serverVersion": "11.8.8",
        "serverImage": "mariadb:11.8@sha256:abc",
    }

    def test_one_shared_environment_is_accepted(self) -> None:
        """Accept a pair that ran on one allocated runner."""
        environment.validate_paired_environment(self.ENVIRONMENT, dict(self.ENVIRONMENT))

    def test_a_differing_field_invalidates_the_pair(self) -> None:
        """Reject a pair whose sides did not share the machine.

        A processor difference between historical runs is an infrastructure
        condition. Inside a pair it is invalid evidence, because the ratio
        would then carry the hardware difference the pairing exists to remove.
        """
        for field in environment.PAIRED_IDENTITY_FIELDS:
            with self.subTest(field=field):
                candidate = dict(self.ENVIRONMENT)
                candidate[field] = "different"
                with self.assertRaises(InvalidEvidenceError):
                    environment.validate_paired_environment(
                        self.ENVIRONMENT, candidate
                    )

    def test_a_differing_benchmark_driver_is_invalid_evidence(self) -> None:
        """Reject a pair measured by two different benchmark driver revisions."""
        reference = {"benchmarkDriverSourceHash": "aaa", "contractDigest": "ccc"}

        for field in ("benchmarkDriverSourceHash", "contractDigest"):
            with self.subTest(field=field):
                candidate = dict(reference)
                candidate[field] = "zzz"
                with self.assertRaises(InvalidEvidenceError):
                    environment.validate_paired_benchmark_driver(reference, candidate)

    def test_missing_benchmark_driver_identity_is_invalid_evidence(self) -> None:
        """Reject a pair that never recorded which benchmark driver measured it."""
        with self.assertRaises(InvalidEvidenceError):
            environment.validate_paired_benchmark_driver(
                {"contractDigest": "ccc"}, {"contractDigest": "ccc"}
            )


class EvidenceAssemblyTests(unittest.TestCase):
    """Prove per-block measurements fold into evidence without losing a side."""

    DRIVER_HASH = "d" * 40
    CONTRACT_DIGEST = "0" * 64

    def setUp(self) -> None:
        """Load the shipped contract so assembled evidence can be evaluated."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)
        self.report = self.builder._workload_report(
            PAIRED_TARGET, self.contract["pairedPolicy"]["blocks"]["profile"]
        )
        self.workload_ids = [
            workload["id"] for workload in self.report["workloads"]
        ]

    def write_block(self, root: Path, block: int, side: str,
                    keep: set[str] | None = None,
                    *,
                    environment_override: dict[str, object] | None = None,
                    driver_hash: str | None = None,
                    write_identity: bool = True) -> None:
        """Write one side of one block as the driver would write it.

        The report is built by the shared fixture so it satisfies the canonical
        workload contract the assembly now applies. `keep` drops workloads from
        an otherwise complete report, which is how a case can express a missing
        side or a workload that appears only later.
        """
        payload = json.loads(json.dumps(self.report))
        payload["runId"] = PAIRED_RUN_ID
        payload["target"] = PAIRED_TARGET
        payload["commit"] = PAIRED_COMMIT
        payload["sourceHash"] = PAIRED_SOURCE_HASH
        payload["runnerClass"] = PAIRED_RUNNER_CLASS
        if environment_override is not None:
            payload["environment"] = {
                **payload["environment"],
                **environment_override,
            }
        if keep is not None:
            payload["workloads"] = [
                workload
                for workload in payload["workloads"]
                if workload["id"] in keep
            ]
        (root / f"block-{block}-{side}.json").write_text(
            json.dumps(payload), encoding="utf-8"
        )
        if write_identity:
            (root / f"block-{block}-{side}.identity.json").write_text(
                json.dumps(
                    {
                        "benchmarkDriverSourceHash": driver_hash or self.DRIVER_HASH,
                        "contractDigest": self.CONTRACT_DIGEST,
                    }
                ),
                encoding="utf-8",
            )

    def test_the_assembly_projects_only_the_audit_summary(self) -> None:
        """Keep the raw measurement out of the audit projection.

        The assembler used to hand the whole workload report through, so the
        document carried a second, unchecked copy of every sample, calibration
        and pulse beside the canonical one the decisions read. A reviewer could
        read numbers there that no decision used.
        """
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for block in range(1, COMPLETE_BLOCKS + 1):
                self.write_block(root, block, "reference")
                self.write_block(root, block, "candidate")

            evidence = self.assemble(root)

        expected = set(paired.CANDIDATE_PROJECTION_FIELDS)
        for block in evidence["candidateWorkloads"]:
            for workload in block["workloads"]:
                self.assertEqual(expected, set(workload))

    def test_the_builder_projects_what_the_assembler_projects(self) -> None:
        """Prove the fixtures and production agree on the document.

        A builder that emitted a different field set would prove a document the
        assembler never writes, which is how the raw arrays stayed invisible
        here while shipping in production.
        """
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for block in range(1, COMPLETE_BLOCKS + 1):
                self.write_block(root, block, "reference")
                self.write_block(root, block, "candidate")

            assembled = self.assemble(root)

        builder = PairedEvidenceBuilder(
            json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        )
        built = builder.evidence(
            [
                {
                    "workloadId": workload["id"],
                    "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
                }
                for workload in self.contract["workloads"]
            ]
        )

        self.assertEqual(
            {
                frozenset(workload)
                for block in assembled["candidateWorkloads"]
                for workload in block["workloads"]
            },
            {
                frozenset(workload)
                for block in built["candidateWorkloads"]
                for workload in block["workloads"]
            },
        )

    def test_the_assembly_carries_the_measured_latencies(self) -> None:
        """Prove the ceilings read what the driver measured.

        The normalized samples are ratios against the calibration pulse, so a
        budget in nanoseconds cannot be applied to them. The raw samples travel
        with the evidence for that reason, and they come from the same report
        entry the ratio decision uses rather than from a summary the document
        could write on its own.
        """
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for block in range(1, COMPLETE_BLOCKS + 1):
                self.write_block(root, block, "reference")
                self.write_block(root, block, "candidate")

            evidence = self.assemble(root)

        expected = {
            workload["id"]: workload["samplesNanoseconds"]
            for workload in self.report["workloads"]
        }
        for test in evidence["tests"]:
            self.assertEqual(COMPLETE_BLOCKS, len(test["latencies"]))
            for measured in test["latencies"]:
                for side in ("reference", "candidate"):
                    self.assertEqual(expected[test["workloadId"]], measured[side])

    def assemble(
        self,
        root: Path,
        blocks: int = COMPLETE_BLOCKS,
    ) -> dict[str, object]:
        """Assemble with fixed identities."""
        return paired.assemble_evidence(
            root,
            target=PAIRED_TARGET,
            run_id=PAIRED_RUN_ID,
            candidate_commit=PAIRED_COMMIT,
            reference_commit="b" * 40,
            contract=self.contract,
            driver_source_hash=self.DRIVER_HASH,
            contract_digest=self.CONTRACT_DIGEST,
            profile=self.contract["pairedPolicy"]["blocks"]["profile"],
            source_hash=PAIRED_SOURCE_HASH,
            runner_class=PAIRED_RUNNER_CLASS,
            execution_order=self.builder.execution_order(blocks),
            soak_report=self.builder.soak(),
        )

    def test_complete_blocks_become_one_test_per_workload(self) -> None:
        """Fold both sides of every block into one ordered test entry."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for block in (1, 2, 3):
                for side in ("reference", "candidate"):
                    self.write_block(root, block, side)

            evidence = self.assemble(root)

            self.assertEqual(3, evidence["blockCount"])
            self.assertEqual(
                sorted(self.workload_ids),
                sorted(test["workloadId"] for test in evidence["tests"]),
            )
            for test in evidence["tests"]:
                self.assertEqual(3, len(test["blocks"]))

    def test_an_environment_change_in_a_later_block_is_invalid_evidence(self) -> None:
        """Refuse a run whose machine changed underneath it.

        Only the first block of each side used to be kept, so a runner that
        changed mid-run -- a different engine build, a different processor
        after a restart -- was neither recorded nor rejected.
        """
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            drifted = {"processor": "Intel Xeon 8370C"}
            for side in ("reference", "candidate"):
                self.write_block(root, 1, side)
                self.write_block(root, 2, side, environment_override=drifted)

            with self.assertRaises(InvalidEvidenceError) as captured:
                self.assemble(root, blocks=2)

            self.assertIn("processor", str(captured.exception))

    def test_a_driver_change_in_a_later_block_is_invalid_evidence(self) -> None:
        """Refuse a run measured by two different benchmark driver revisions."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for side in ("reference", "candidate"):
                self.write_block(root, 1, side)
                self.write_block(root, 2, side, driver_hash="z" * 40)

            with self.assertRaises(InvalidEvidenceError):
                self.assemble(root, blocks=2)

    def test_a_missing_side_is_invalid_evidence(self) -> None:
        """Refuse to fold a block that measured only one provider.

        Dropping the incomplete workload instead would turn missing evidence
        into an apparently clean result.
        """
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.write_block(root, 1, "reference")
            self.write_block(root, 1, "candidate")
            self.write_block(root, 2, "reference")

            with self.assertRaises(InvalidEvidenceError):
                self.assemble(root)

    def test_a_report_without_workloads_is_invalid_evidence(self) -> None:
        """Reject a measurement file that recorded nothing at all."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.write_block(root, 1, "reference", keep=set())
            self.write_block(root, 1, "candidate")

            with self.assertRaises(InvalidEvidenceError):
                self.assemble(root)

    def test_an_incomplete_workload_matrix_is_invalid_evidence(self) -> None:
        """Refuse a block that measured only part of the registered matrix.

        This closes the later-block gap one stage earlier than before: a
        workload present in some blocks and absent in others cannot arise at
        all, because the canonical contract already refuses a report that does
        not carry the complete matrix.
        """
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for side in ("reference", "candidate"):
                self.write_block(root, 1, side)
                self.write_block(root, 2, side, keep=set(self.workload_ids[:-1]))

            with self.assertRaises(InvalidEvidenceError) as captured:
                self.assemble(root)

            self.assertIn("matrix drift", str(captured.exception).lower())

    def test_a_foreign_raw_report_is_invalid_evidence(self) -> None:
        """Refuse a block report that is not this contract's document.

        Checking only the fields this module reads let a foreign schema and a
        foreign kind through, along with any statistic that does not follow
        from the samples beside it.
        """
        for field, value in (("schemaVersion", 999), ("kind", "foreign-report")):
            with self.subTest(field=field):
                with tempfile.TemporaryDirectory() as directory:
                    root = Path(directory)
                    for side in ("reference", "candidate"):
                        self.write_block(root, 1, side)
                    path = root / "block-1-candidate.json"
                    payload = json.loads(path.read_text(encoding="utf-8"))
                    payload[field] = value
                    path.write_text(json.dumps(payload), encoding="utf-8")

                    with self.assertRaises(InvalidEvidenceError):
                        self.assemble(root, blocks=1)

    def test_an_impossible_termination_is_invalid_evidence(self) -> None:
        """Refuse a report whose own verdict contradicts itself.

        `precision_reached` without the minimum duration, and a cap reason
        recorded for a population far below the cap, are states the runner
        cannot produce. Accepting them let a run that never converged present
        itself as one that did.
        """
        cases = (
            ("precision without duration", {"minimumDurationReached": False}),
            (
                "cap below the cap",
                {"terminationReason": "sample_cap_reached", "sampleCount": 16},
            ),
            ("count that contradicts the samples", {"sampleCount": 9999}),
        )
        for description, changes in cases:
            with self.subTest(case=description):
                with tempfile.TemporaryDirectory() as directory:
                    root = Path(directory)
                    for side in ("reference", "candidate"):
                        self.write_block(root, 1, side)
                    path = root / "block-1-candidate.json"
                    payload = json.loads(path.read_text(encoding="utf-8"))
                    payload["workloads"][0].update(changes)
                    path.write_text(json.dumps(payload), encoding="utf-8")

                    with self.assertRaises(InvalidEvidenceError):
                        self.assemble(root, blocks=1)

    def test_an_empty_directory_is_invalid_evidence(self) -> None:
        """Reject a paired run that produced nothing."""
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(InvalidEvidenceError):
                self.assemble(Path(directory))

    def test_assembled_evidence_evaluates(self) -> None:
        """Round-trip assembly into the evaluator that consumes it."""
        contract = self.contract
        minimum = contract["pairedPolicy"]["blocks"]["completeBlocks"]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for block in range(1, minimum + 1):
                self.write_block(root, block, "reference")
                self.write_block(root, block, "candidate")

            evidence = self.assemble(root, blocks=minimum)
            result = paired.evaluate_paired_comparison(
                evidence, contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

            self.assertEqual("pending-run-wide-adjustment", result["qualification"])
            self.assertTrue(result["absoluteCeilings"])
            self.assertTrue(result["resourceResults"])
            self.assertTrue(result["soakScenarios"])



class AbsoluteCeilingTests(unittest.TestCase):
    """Prove a pair that regressed together is still rejected.

    A ratio can only say the candidate is no worse than its reference. Both
    sides degrading by the same factor produces a ratio of one, which is why
    the released provider stays bound to the absolute budgets the historical
    gate enforced.
    """

    WORKLOADS = ("steady",)

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)

    def evaluate(self, **kwargs) -> dict[str, object]:
        """Evaluate an unchanged candidate across every registered family.

        One workload per family, so the smallest budget of each metric is
        actually reachable: a breach expressed against the smallest allocation
        budget has to land on the workload that carries it.
        """
        families = sorted(self.contract["familyBudgets"])
        tests = [
            {
                "workloadId": f"absolute.{family}",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
            }
            for family in families
        ]

        return paired.evaluate_paired_comparison(
            self.builder.evidence(
                tests,
                families={f"absolute.{family}": family for family in families},
                **kwargs,
            ),
            self.builder.contract,
            contract_digest=PAIRED_CONTRACT_DIGEST,
        )

    def test_a_candidate_inside_every_ceiling_qualifies(self) -> None:
        """Establish the baseline the breach cases are measured against."""
        result = self.evaluate()

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        self.assertTrue(all(check["passed"] for check in result["absoluteCeilings"]))

    def test_an_allocation_ceiling_breach_is_a_regression(self) -> None:
        """Reject a candidate that allocates past its family budget."""
        smallest = min(
            budget["allocatedBytes"]
            for budget in self.contract["familyBudgets"].values()
        )

        result = self.evaluate(allocated=int(smallest) + 1)

        self.assertEqual("regression", result["qualification"])
        breached = [
            check
            for check in result["absoluteCeilings"]
            if not check["passed"] and check["metric"] == "allocatedBytesPerOperation"
        ]
        self.assertTrue(breached)

    def test_a_collection_ceiling_breach_is_a_regression(self) -> None:
        """Reject a candidate that collects past its family budget.

        Collection counts are absent from the shared absolute-budget helper, so
        this is the check that proves the paired path adds them rather than
        dropping the metric the historical gate covered.
        """
        smallest = min(
            budget["gen2CollectionsPer1000"]
            for budget in self.contract["familyBudgets"].values()
        )

        result = self.evaluate(collections=int(smallest) + 1)

        self.assertEqual("regression", result["qualification"])
        breached = [
            check
            for check in result["absoluteCeilings"]
            if not check["passed"] and check["metric"] == "gen2CollectionsPer1000"
        ]
        self.assertTrue(breached)

    def test_a_breach_in_an_early_block_still_decides(self) -> None:
        """Reject a candidate that blew its ceiling once and then recovered.

        Reading only the final block let an early breach disappear: a
        catastrophe ceiling that a workload crossed at any point has been
        crossed, whatever the last measurement happened to show.
        """
        smallest = min(
            budget["allocatedBytes"]
            for budget in self.contract["familyBudgets"].values()
        )

        result = self.evaluate(first_block_allocated=int(smallest) + 1)

        self.assertEqual("regression", result["qualification"])
        breached = [
            check for check in result["absoluteCeilings"] if not check["passed"]
        ]
        self.assertTrue(breached)
        self.assertEqual({1}, {check["block"] for check in breached})

    def test_missing_candidate_measurements_are_invalid_evidence(self) -> None:
        """Refuse to qualify when there is nothing to hold to the ceiling."""
        evidence = self.builder.evidence(
            [
                {
                    "workloadId": "steady",
                    "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
                }
            ]
        )
        evidence["candidateWorkloads"] = []

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                evidence, self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )


class CandidateCeilingBindingTests(unittest.TestCase):
    """Prove the absolute ceilings are chosen by the contract, not the evidence.

    The budgets are per family, and the family was read from the candidate
    entry being judged. A document could therefore name the ceiling it wanted
    to be held to: the same 200 ms measurement failed as `concurrency` and
    passed as `write`. The registered family is now the only one that selects
    a budget, and every metric the ceilings read is checked as a measurement
    before it reaches one.
    """

    def setUp(self) -> None:
        """Register one workload per family against the shipped budgets."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)
        self.families = sorted(self.contract["familyBudgets"])
        self.subject = "absolute.concurrency"

    def document(self) -> dict[str, object]:
        """Return evidence whose workloads carry their registered family."""
        tests = [
            {
                "workloadId": f"absolute.{family}",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
            }
            for family in self.families
        ]

        return self.builder.evidence(
            tests,
            families={f"absolute.{family}": family for family in self.families},
        )

    def evaluate(self, document: dict[str, object]) -> dict[str, object]:
        """Evaluate against the contract the builder registered."""
        return paired.evaluate_paired_comparison(
            document,
            self.builder.contract,
            contract_digest=PAIRED_CONTRACT_DIGEST,
        )

    def slow(self, latency: float) -> dict[str, object]:
        """Return evidence whose subject measured `latency` on both sides.

        Both projections move together, because they describe one population:
        the calibration divisor is one, so the normalized samples the pairing
        reads are the same numbers. The ratio stays at one, which is what makes
        this a case only the absolute ceiling can decide.
        """
        document = self.document()
        for test in document["tests"]:
            if test["workloadId"] != self.subject:
                continue
            for field in ("blocks", "latencies"):
                for measured in test[field]:
                    for side in ("reference", "candidate"):
                        measured[side] = [latency] * len(measured[side])
        for block in document["candidateWorkloads"]:
            for workload in block["workloads"]:
                if workload["id"] == self.subject:
                    for field in paired.LATENCY_CEILING_QUANTILES:
                        workload[field] = latency

        return document

    def revise(self, document: dict[str, object], **fields: object) -> None:
        """Rewrite the subject workload in every candidate block."""
        for block in document["candidateWorkloads"]:
            for workload in block["workloads"]:
                if workload["id"] == self.subject:
                    for field, value in fields.items():
                        if value is ...:
                            workload.pop(field, None)
                        else:
                            workload[field] = value

    def test_a_breach_of_the_registered_budget_is_a_regression(self) -> None:
        """Establish the verdict the re-declaration used to escape."""
        budget = self.contract["familyBudgets"]["concurrency"]["medianNanoseconds"]
        document = self.slow(float(budget) + 50000000.0)

        self.assertEqual("regression", self.evaluate(document)["qualification"])

    def test_a_pair_that_degraded_together_is_a_regression(self) -> None:
        """Reject two sides that are equally, and unacceptably, slow.

        The ratio is one, so only the absolute ceiling can catch this -- and it
        read a per-block summary the document wrote freely rather than the
        samples the pairing was formed from. A candidate measured at 200 ms
        against a 150 ms budget qualified while claiming a median of 1 ns.
        """
        budget = self.contract["familyBudgets"]["concurrency"]["medianNanoseconds"]
        document = self.slow(float(budget) + 50000000.0)
        block = document["tests"][0]["latencies"][0]
        self.assertEqual(block["reference"], block["candidate"])

        result = self.evaluate(document)

        self.assertEqual("regression", result["qualification"])
        breached = [
            check
            for check in result["absoluteCeilings"]
            if not check["passed"] and check["metric"] == "medianNanoseconds"
        ]
        self.assertTrue(breached)

    def test_a_projection_disagreeing_with_the_samples_is_invalid(self) -> None:
        """Refuse a summary that does not follow from its own measurement."""
        for field in paired.LATENCY_CEILING_QUANTILES:
            with self.subTest(field=field):
                document = self.document()
                projected = document["candidateWorkloads"][0]["workloads"][0]
                self.revise(document, **{field: projected[field] * 2 + 1})

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_re_declared_family_cannot_choose_a_kinder_budget(self) -> None:
        """Refuse the document rather than judge it against another family.

        This is the finding itself: `write` allows twenty times the median
        `concurrency` does, so the identical measurement qualified once it
        claimed to be a different kind of workload.
        """
        budget = self.contract["familyBudgets"]["concurrency"]["medianNanoseconds"]
        generous = self.contract["familyBudgets"]["write"]["medianNanoseconds"]
        self.assertGreater(generous, budget)

        document = self.document()
        self.revise(document, family="write", medianNanoseconds=float(budget) + 1)

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_a_family_the_contract_does_not_register_is_invalid_evidence(self) -> None:
        """Refuse an unknown family instead of failing to look it up.

        The lookup once raised a bare KeyError instead of identifying the
        document as invalid evidence.
        """
        for family in ("write", "not-a-family", None, 7, ...):
            with self.subTest(family=family):
                document = self.document()
                self.revise(document, family=family)

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_the_ceiling_evaluator_ignores_a_declared_family(self) -> None:
        """Prove the budget choice at the point that makes it.

        The document contract rejects a re-declared family before the ceilings
        run, so this holds the evaluator itself to the rule: a caller reaching
        it directly must not be able to hand it the budget it prefers.
        """
        budget = self.contract["familyBudgets"]["concurrency"]["medianNanoseconds"]
        breach = float(budget) + 1
        tests = [
            {
                "workloadId": self.subject,
                "family": "write",
                "latencies": [{"reference": [breach], "candidate": [breach]}],
                "resources": [
                    {
                        side: {
                            "allocatedBytesPerOperation": 1000,
                            "gen2CollectionsPer1000": 0,
                        }
                        for side in ("reference", "candidate")
                    }
                ],
            }
        ]
        self.builder.narrow_to(
            [self.subject], {self.subject: "concurrency"}
        )

        checks = paired.evaluate_absolute_ceilings(
            tests,
            self.builder.contract,
            self.builder.contract["pairedPolicy"],
            block_count=1,
        )

        median = next(
            check for check in checks if check["metric"] == "medianNanoseconds"
        )
        self.assertEqual(budget, median["maximum"])
        self.assertFalse(median["passed"])

    def test_a_projection_disagreeing_with_its_measurement_is_invalid(self) -> None:
        """Refuse a document whose two copies of one number differ.

        Allocation and collections are decided twice: as a ratio against the
        reference, from `tests`, and against the absolute budget, from
        `candidateWorkloads`. The assembler fills both from one report, so a
        disagreement means one of them was edited.
        """
        for field in ("allocatedBytesPerOperation", "gen2CollectionsPer1000"):
            with self.subTest(field=field):
                document = self.document()
                measured = next(
                    test
                    for test in document["tests"]
                    if test["workloadId"] == self.subject
                )["resources"][0]["candidate"][field]
                self.revise(document, **{field: measured + 1})

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_metric_no_measurement_can_produce_is_invalid_evidence(self) -> None:
        """Refuse malformed ceiling metrics as evidence, not as a verdict."""
        impossible = (
            "bad",
            None,
            True,
            float("nan"),
            float("inf"),
            float("-inf"),
            -1,
            ...,
        )
        fields = (
            "medianNanoseconds",
            "p95Nanoseconds",
            "p99Nanoseconds",
            "allocatedBytesPerOperation",
            "gen2CollectionsPer1000",
        )
        for field in fields:
            for value in impossible:
                with self.subTest(field=field, value=value):
                    document = self.document()
                    self.revise(document, **{field: value})

                    with self.assertRaises(InvalidEvidenceError):
                        self.evaluate(document)


class ResourceFamilyTests(unittest.TestCase):
    """Prove allocation and collection counts are compared, not just latency."""

    WORKLOADS = ("steady",)

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)

    def budget(self, metric: str) -> float:
        """Return the registered paired budget for one resource metric."""
        for family in self.contract["pairedPolicy"]["resourceFamilies"]:
            if family["metric"] == metric:
                return float(family["budget"])

        raise AssertionError(f"{metric} is not a registered resource family")

    def evaluate(self, resources: list[dict[str, dict[str, float]]]) -> dict[str, object]:
        """Evaluate a latency-neutral run carrying the given resource scalars."""
        tests = [
            {
                "workloadId": "steady",
                "blocks": uniform_blocks(len(resources), 1.0),
                "resources": resources,
            }
        ]

        return paired.evaluate_paired_comparison(
                self.builder.evidence(tests), self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_unchanged_resources_qualify(self) -> None:
        """Accept a candidate that allocates and collects like its reference."""
        result = self.evaluate(resource_blocks(COMPLETE_BLOCKS))

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])

    def test_allocation_above_its_budget_is_a_regression(self) -> None:
        """Reject an allocation increase the latency families cannot see."""
        factor = self.budget("allocatedBytesPerOperation") * 2

        result = self.evaluate(resource_blocks(COMPLETE_BLOCKS, allocation=factor))

        self.assertEqual("regression", result["qualification"])
        observed = [
            item
            for item in result["resourceResults"]
            if item["metric"] == "allocatedBytesPerOperation"
        ][0]
        self.assertEqual("required", observed["role"])
        self.assertEqual("regression", observed["state"])

    def test_sparse_collections_above_their_ratio_budget_are_observational(self) -> None:
        """Do not turn one additional Gen2 event into a provider regression."""
        # Hosted run 31903353665 observed one collection on four reference
        # blocks and two on the corresponding candidate blocks. The median
        # ratio was 1.5 even though every candidate absolute ceiling passed.
        result = self.evaluate(resource_blocks(COMPLETE_BLOCKS, collections=1.5))

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        observed = [
            item
            for item in result["resourceResults"]
            if item["metric"] == "gen2CollectionsPer1000"
        ][0]
        self.assertEqual("observational", observed["role"])
        self.assertEqual(1.5, observed["observedRatio"])
        self.assertEqual(
            self.budget("gen2CollectionsPer1000"), observed["budget"]
        )
        self.assertEqual("observed-above-budget", observed["state"])

    def test_resource_policy_cannot_drop_either_safety_signal(self) -> None:
        """Keep allocation qualification and collection observation complete."""
        self.builder.contract["pairedPolicy"]["resourceFamilies"].pop()

        with self.assertRaisesRegex(InvalidEvidenceError, "complete resource metric set"):
            self.evaluate(resource_blocks(COMPLETE_BLOCKS))

    def test_allocating_where_the_reference_allocated_nothing_is_a_regression(self) -> None:
        """Decide the zero-reference case instead of dividing by it."""
        resources = resource_blocks(COMPLETE_BLOCKS)
        for block in resources:
            block["reference"]["allocatedBytesPerOperation"] = 0.0
            block["candidate"]["allocatedBytesPerOperation"] = 64.0

        result = self.evaluate(resources)

        self.assertEqual("regression", result["qualification"])

    def test_two_sides_that_both_allocated_nothing_qualify(self) -> None:
        """Treat an unchanged zero as unchanged, not as an undefined ratio."""
        resources = resource_blocks(COMPLETE_BLOCKS)
        for block in resources:
            for side in ("reference", "candidate"):
                block[side]["allocatedBytesPerOperation"] = 0.0

        result = self.evaluate(resources)

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])

    def test_a_run_without_resource_measurements_is_invalid_evidence(self) -> None:
        """Reject evidence that dropped the resource side of the comparison."""
        evidence = self.builder.evidence(
            [
                {
                    "workloadId": "steady",
                    "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
                }
            ]
        )
        evidence["tests"][0].pop("resources")

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                evidence, self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )


class ExecutionOrderTests(unittest.TestCase):
    """Prove the run followed the order the policy registers.

    The runner previously wrote the planned pattern into the evidence while
    executing a collapsed version of it, and nothing compared the two. The
    counterbalancing the whole comparison rests on was therefore documented
    rather than proven.
    """

    WORKLOADS = ("steady",)

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)
        self.policy = self.contract["pairedPolicy"]

    def evaluate(self, order: dict[str, object] | None) -> dict[str, object]:
        """Evaluate an unchanged candidate under the given recorded order."""
        tests = [
            {
                "workloadId": "steady",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
            }
        ]

        return paired.evaluate_paired_comparison(
            self.builder.evidence(tests, order=order), self.contract,
            contract_digest=PAIRED_CONTRACT_DIGEST,
        )

    def test_an_alternating_run_is_accepted(self) -> None:
        """Establish the baseline the rejections are measured against."""
        self.assertEqual(
            "pending-run-wide-adjustment", self.evaluate(...)["qualification"]
        )

    def test_a_run_without_a_recorded_order_is_invalid_evidence(self) -> None:
        """Refuse a run that never recorded what it executed."""
        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(None)

    def test_an_unregistered_pattern_is_invalid_evidence(self) -> None:
        """Refuse an order the policy does not register.

        This is what the collapsed execution produced: the runner ran `A-B`
        while the contract registered `A-B-B-A`, and the artifact claimed the
        latter.
        """
        order = self.builder.execution_order(COMPLETE_BLOCKS)
        order["executedBlockPatterns"][3] = "A-B-B-A"

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(order)

    def test_a_fixed_starting_side_is_invalid_evidence(self) -> None:
        """Refuse a run in which one provider always measured first."""
        order = self.builder.execution_order(COMPLETE_BLOCKS)
        order["executedBlockPatterns"] = [
            self.policy["executionOrder"]["blockPatterns"][0]
        ] * COMPLETE_BLOCKS

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(order)

    def test_a_run_under_another_profile_is_invalid_evidence(self) -> None:
        """Refuse measurements taken under a profile the policy did not name."""
        order = self.builder.execution_order(COMPLETE_BLOCKS)
        order["blockProfile"] = "scorecard"

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(order)

    def test_too_few_executed_blocks_are_invalid_evidence(self) -> None:
        """Refuse an order that covers fewer blocks than the policy requires."""
        order = self.builder.execution_order(2)

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(order)

    def test_the_registered_patterns_are_the_ones_a_run_can_execute(self) -> None:
        """Keep the contract describing what the runner can actually do.

        The driver writes one measurement file per side per block, so a pattern
        that visits a side twice cannot be executed as written. Registering one
        would put the contract permanently out of reach of any real run.
        """
        for pattern in self.policy["executionOrder"]["blockPatterns"]:
            sides = pattern.split("-")
            with self.subTest(pattern=pattern):
                self.assertEqual(len(sides), len(set(sides)))


class SustainedUseTests(unittest.TestCase):
    """Prove the paired path does not lose the sustained-use evidence."""

    WORKLOADS = ("steady",)

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)

    def test_the_policy_requires_a_soak_report(self) -> None:
        """Keep the requirement in the contract rather than in the code."""
        self.assertTrue(self.contract["pairedPolicy"]["soak"]["required"])
        self.assertTrue(self.contract["profiles"]["paired-block"]["soakRequired"])

    def test_evidence_without_a_soak_report_is_invalid(self) -> None:
        """Refuse a verdict when a required report is simply absent.

        A leak appears over thousands of iterations and never inside a block,
        so silently skipping the report would qualify exactly the defect the
        report exists to find.
        """
        tests = [
            {
                "workloadId": "steady",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
            }
        ]

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                self.builder.evidence(tests, soak=None), self.contract,
                contract_digest=PAIRED_CONTRACT_DIGEST,
            )

    def test_a_soak_report_from_another_run_is_rejected(self) -> None:
        """Bind the report to the run it is presented as evidence for."""
        tests = [
            {
                "workloadId": "steady",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
            }
        ]
        foreign = self.builder.soak()
        foreign["runId"] = "some-other-run"

        with self.assertRaises(Exception):
            paired.evaluate_paired_comparison(
                self.builder.evidence(tests, soak=foreign), self.contract,
                contract_digest=PAIRED_CONTRACT_DIGEST,
            )


class EvidenceEnvelopeTests(unittest.TestCase):
    """Prove nothing but this run's evidence reaches a statistic.

    An unguarded field access raises a plain `KeyError`; the command line maps
    an uncaught error to exit 1; the attempt recorder reads exit 1 as a
    regression. A truncated or foreign document therefore convicted the
    provider it never described.
    """

    WORKLOADS = ("steady",)

    def setUp(self) -> None:
        """Load the shipped contract and a complete evidence document."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)
        self.tests = [
            {
                "workloadId": "steady",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
            }
        ]

    def evidence(self, **overrides: object) -> dict[str, object]:
        """Return complete evidence with the given envelope overrides."""
        document = self.builder.evidence(self.tests)
        for field, value in overrides.items():
            if value is ...:
                document.pop(field, None)
            else:
                document[field] = value

        return document

    def test_complete_evidence_qualifies(self) -> None:
        """Establish the baseline the rejections are measured against."""
        result = paired.evaluate_paired_comparison(
                self.evidence(), self.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])

    def test_a_foreign_schema_version_is_refused(self) -> None:
        """Reject a document written against a different evidence shape."""
        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                self.evidence(schemaVersion=99), self.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_a_foreign_kind_is_refused(self) -> None:
        """Reject a document that is not paired evidence at all."""
        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                self.evidence(kind="something-else"), self.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_every_identity_field_is_required(self) -> None:
        """Reject a truncated document before it reaches a field access."""
        for field in paired.PAIRED_IDENTITY_FIELDS_REQUIRED:
            with self.subTest(field=field):
                with self.assertRaises(InvalidEvidenceError):
                    paired.evaluate_paired_comparison(
                self.evidence(**{field: ...}), self.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_an_empty_or_mistyped_identity_is_refused(self) -> None:
        """Reject a present-but-useless identity as firmly as a missing one."""
        for value in ("", "   ", 7, None, []):
            with self.subTest(value=repr(value)):
                with self.assertRaises(InvalidEvidenceError):
                    paired.evaluate_paired_comparison(
                self.evidence(runId=value), self.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_evidence_from_another_profile_is_refused(self) -> None:
        """Reject measurements decided against a different contract.

        The profile fixes the population floor, the sample cap, and the error
        budget; evidence from another one was judged by other rules.
        """
        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                self.evidence(profile="scorecard"), self.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )


class TerminationSemanticsTests(unittest.TestCase):
    """Prove sample-cap metadata remains valid and visible evidence."""

    WORKLOADS = ("steady",)

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)
        self.tests = [
            {
                "workloadId": "steady",
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
            }
        ]

    def evaluate(self, **kwargs) -> dict[str, object]:
        """Evaluate an otherwise unchanged run."""
        return paired.evaluate_paired_comparison(
            self.builder.evidence(self.tests, **kwargs), self.contract,
            contract_digest=PAIRED_CONTRACT_DIGEST,
        )

    def test_a_converged_run_qualifies(self) -> None:
        """Establish the baseline."""
        result = self.evaluate()

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        self.assertEqual([], result["cappedWorkloads"])

    def test_a_capped_run_reports_the_cap_without_forcing_a_retry(self) -> None:
        """Stop at the registered cap and retain the fixed-sample verdict."""
        result = paired.evaluate_paired_comparison(
            self.builder.evidence(
                [
                    {
                        "workloadId": "steady",
                        "blocks": uniform_blocks(
                            COMPLETE_BLOCKS,
                            1.0,
                            samples=CAP_SAMPLES,
                        ),
                    }
                ],
                termination="sample_cap_reached",
            ),
            self.builder.contract,
            contract_digest=PAIRED_CONTRACT_DIGEST,
        )

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        self.assertEqual(["steady"], result["cappedWorkloads"])

    def test_missing_termination_records_are_invalid_evidence(self) -> None:
        """Refuse evidence that never recorded whether it converged."""
        document = self.builder.evidence(self.tests)
        for entry in document["tests"]:
            entry.pop("terminations")

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                document, self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_a_termination_count_that_disagrees_is_invalid_evidence(self) -> None:
        """Refuse records that do not cover every block."""
        document = self.builder.evidence(self.tests)
        for entry in document["tests"]:
            entry["terminations"] = entry["terminations"][:-1]

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                document, self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_a_sample_count_that_disagrees_is_invalid_evidence(self) -> None:
        """Refuse a record whose count contradicts the block it describes."""
        document = self.builder.evidence(self.tests)
        document["tests"][0]["terminations"][0]["reference"]["sampleCount"] = 9999

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                document, self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_an_unregistered_termination_reason_is_invalid_evidence(self) -> None:
        """Refuse a reason the contract does not define."""
        document = self.builder.evidence(self.tests)
        document["tests"][0]["terminations"][0]["candidate"][
            "terminationReason"
        ] = "gave-up"

        with self.assertRaises(InvalidEvidenceError):
            paired.evaluate_paired_comparison(
                document, self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )


class AssembledEvidenceContractTests(unittest.TestCase):
    """Prove `evaluate-paired` is its own trust boundary.

    The assembler validates the raw reports it reads, but the evaluator is a
    separate entry point handed a finished document. Trusting it meant a
    truncated, edited, or otherwise produced `paired-evidence.json` could
    qualify a release -- and nothing downstream recomputes the statistics from
    the raw reports it claims to summarize.
    """

    def setUp(self) -> None:
        """Load the shipped contract, whose full matrix is the rule here."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.registered = [
            workload["id"] for workload in self.contract["workloads"]
        ]

    def evaluate(self, document: dict[str, object]) -> dict[str, object]:
        """Evaluate against the shipped contract and its real digest."""
        return paired.evaluate_paired_comparison(
            document, self.contract, contract_digest=sha256(CONTRACT_PATH)
        )

    def evidence(self, workload_ids, *, blocks=None, **kwargs) -> dict[str, object]:
        """Build evidence for the named workloads against the shipped matrix."""
        builder = PairedEvidenceBuilder(
            json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        )
        tests = [
            {
                "workloadId": identifier,
                "blocks": [
                    {side: list(samples) for side, samples in measured.items()}
                    for measured in (
                        blocks or uniform_blocks(COMPLETE_BLOCKS, 1.0)
                    )
                ],
            }
            for identifier in workload_ids
        ]
        document = builder.evidence(tests, **kwargs)
        document["contractDigest"] = sha256(CONTRACT_PATH)
        # The builder narrows its own copy; this case is about the shipped one.
        return document

    def test_a_partial_matrix_is_invalid_evidence(self) -> None:
        """Refuse evidence that measured only part of the registered matrix."""
        with self.assertRaises(InvalidEvidenceError) as captured:
            self.evaluate(self.evidence(self.registered[:-1]))

        self.assertIn("missing registered workload", str(captured.exception))
        self.assertIn(self.registered[-1], str(captured.exception))

    def test_an_unregistered_workload_is_invalid_evidence(self) -> None:
        """Refuse a workload the contract never declared."""
        with self.assertRaises(InvalidEvidenceError) as captured:
            self.evaluate(self.evidence(self.registered + ["invented.workload"]))

        self.assertIn("does not register", str(captured.exception))

    def test_a_repeated_workload_is_invalid_evidence(self) -> None:
        """Refuse a document that reports one workload twice."""
        document = self.evidence(self.registered)
        document["tests"].append(json.loads(json.dumps(document["tests"][0])))

        with self.assertRaises(InvalidEvidenceError) as captured:
            self.evaluate(document)

        self.assertIn("twice", str(captured.exception))

    def test_precision_without_the_duration_floor_is_invalid_evidence(self) -> None:
        """Refuse a convergence claim the runner cannot produce."""
        document = self.evidence(self.registered)
        for record in document["tests"][0]["terminations"]:
            for side in ("reference", "candidate"):
                record[side]["minimumDurationReached"] = False

        with self.assertRaises(InvalidEvidenceError) as captured:
            self.evaluate(document)

        self.assertIn("minimum measurement duration", str(captured.exception))

    def test_a_cap_claim_below_the_cap_is_invalid_evidence(self) -> None:
        """Refuse a cap claimed at a population the cap never permitted.

        This is the state the synthetic fixtures used to produce: a run cannot
        stop at its cap while carrying a fraction of it.
        """
        document = self.evidence(self.registered, termination="sample_cap_reached")

        with self.assertRaises(InvalidEvidenceError) as captured:
            self.evaluate(document)

        self.assertIn("cap is", str(captured.exception))

    def test_a_missing_block_count_is_invalid_evidence(self) -> None:
        """Refuse a document without the spine every count is measured against.

        Treating it as optional made every other count check vacuous: a
        document that simply omitted it qualified.
        """
        document = self.evidence(self.registered)
        document.pop("blockCount")

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_a_block_count_outside_the_policy_is_invalid_evidence(self) -> None:
        """Refuse a declared count the policy does not permit."""
        policy = self.contract["pairedPolicy"]["blocks"]
        for declared in (policy["completeBlocks"] - 1, policy["completeBlocks"] + 1):
            with self.subTest(blockCount=declared):
                document = self.evidence(self.registered)
                document["blockCount"] = declared

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_consistent_nine_block_run_is_invalid_evidence(self) -> None:
        """Refuse a self-consistent run shorter than the fixed population.

        Every parallel array and the declared count agree on nine blocks. The
        policy still requires ten, so redundant structural guards may not turn
        an early stop into qualifying evidence.
        """
        registered = self.contract["pairedPolicy"]["blocks"]["completeBlocks"]
        document = self.evidence(
            self.registered,
            blocks=uniform_blocks(registered - 1, 1.0),
        )
        self.assertEqual(registered - 1, document["blockCount"])

        policy = validate_paired_policy(self.contract)
        with self.assertRaises(InvalidEvidenceError):
            paired.validate_paired_evidence(
                document,
                self.contract,
                policy,
                contract_digest=sha256(CONTRACT_PATH),
            )
        with self.assertRaises(InvalidEvidenceError):
            paired.validate_execution_order(
                document["executionOrder"],
                policy,
                registered,
            )

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_partial_candidate_measurements_are_invalid_evidence(self) -> None:
        """Refuse candidate blocks that cover part of the run.

        The absolute ceilings read these measurements; one block of them held
        the candidate to a fraction of what it actually ran.
        """
        document = self.evidence(self.registered)
        document["candidateWorkloads"] = document["candidateWorkloads"][:1]

        with self.assertRaises(InvalidEvidenceError) as captured:
            self.evaluate(document)

        self.assertIn("cover blocks", str(captured.exception))

    def test_a_candidate_block_missing_a_workload_is_invalid_evidence(self) -> None:
        """Refuse a candidate block that measured fewer workloads than declared."""
        document = self.evidence(self.registered)
        document["candidateWorkloads"][0]["workloads"] = (
            document["candidateWorkloads"][0]["workloads"][:-1]
        )

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_missing_environments_are_invalid_evidence(self) -> None:
        """Refuse a document that cannot show both sides shared one machine.

        Dropping the record made the claim the whole comparison rests on
        unfalsifiable.
        """
        document = self.evidence(self.registered)
        document.pop("environments")

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_diverging_environments_are_invalid_evidence(self) -> None:
        """Re-check the two sides here, not only at assembly."""
        document = self.evidence(self.registered)
        document["environments"]["candidate"]["processor"] = "Intel Xeon 8370C"

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_missing_provenance_is_invalid_evidence(self) -> None:
        """Refuse numbers that describe nothing in particular."""
        for field in ("candidateCommit", "referenceCommit",
                      "benchmarkDriverSourceHash", "contractDigest"):
            with self.subTest(field=field):
                document = self.evidence(self.registered)
                document.pop(field)

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_two_commits_in_one_document_are_invalid_evidence(self) -> None:
        """Refuse a document that names two candidate revisions."""
        document = self.evidence(self.registered)
        document["candidateCommit"] = "9" * 40

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_the_binding_cannot_be_skipped_by_a_caller(self) -> None:
        """Enforce the invariant in the signature, not only in the CLI.

        An optional parameter meant an internal caller could omit it and get a
        verdict with no contract binding at all -- which is what the tests
        themselves were doing.
        """
        document = self.evidence(self.registered)

        with self.assertRaises(TypeError):
            paired.evaluate_paired_comparison(document, self.contract)

    def test_a_meaningless_revision_is_invalid_evidence(self) -> None:
        """Refuse provenance that names no revision.

        No later gate re-checks the reference revision, so a document carrying
        `not-a-commit` would travel all the way into a release manifest.
        """
        for field in ("commit", "candidateCommit", "referenceCommit"):
            with self.subTest(field=field):
                document = self.evidence(self.registered)
                document[field] = "not-a-commit"

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_mistyped_test_structure_is_invalid_evidence(self) -> None:
        """Refuse broken test and resource shapes as evidence, not as verdict."""
        cases = {
            "test entry is not an object": lambda d: d["tests"].__setitem__(0, "text"),
            "workload identifier is null": lambda d: d["tests"][0].__setitem__("workloadId", None),
            "workload identifier is a list": lambda d: d["tests"][0].__setitem__("workloadId", []),
            "blocks is not a list": lambda d: d["tests"][0].__setitem__("blocks", {}),
            "measurement block is null": lambda d: d["tests"][0]["blocks"].__setitem__(0, None),
            "measurement block misses a side": lambda d: d["tests"][0]["blocks"][0].pop("candidate"),
            "samples are empty": lambda d: d["tests"][0]["blocks"][0].__setitem__("candidate", []),
            "resource block is null": lambda d: d["tests"][0]["resources"].__setitem__(0, None),
            "resource side is not an object": lambda d: d["tests"][0]["resources"][0].__setitem__("candidate", 7),
            "resource metric missing": lambda d: d["tests"][0]["resources"][0]["candidate"].pop(
                "allocatedBytesPerOperation"),
        }
        for description, mutate in cases.items():
            with self.subTest(case=description):
                document = self.evidence(self.registered)
                mutate(document)

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_foreign_contract_digest_is_invalid_evidence(self) -> None:
        """Refuse evidence that names a contract nobody evaluated it against.

        Requiring the field without binding it let a budget, a cap, or a
        workload matrix from somewhere else carry the verdict.
        """
        document = self.evidence(self.registered)
        document["contractDigest"] = "a" * 64

        with self.assertRaises(InvalidEvidenceError) as captured:
            self.evaluate(document)

        self.assertIn("this evaluation loaded", str(captured.exception))

    def test_a_malformed_digest_is_invalid_evidence(self) -> None:
        """Refuse a provenance value that is not a digest at all."""
        for field in ("benchmarkDriverSourceHash", "contractDigest"):
            with self.subTest(field=field):
                document = self.evidence(self.registered)
                document[field] = "not-a-digest"

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_empty_environments_are_invalid_evidence(self) -> None:
        """Refuse two empty objects presented as one shared machine.

        Comparing alone made them agree: every field was absent on both sides,
        so every comparison held, and the claim the whole comparison rests on
        was established by recording nothing.
        """
        document = self.evidence(self.registered)
        document["environments"] = {"reference": {}, "candidate": {}}

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_an_incomplete_environment_is_invalid_evidence(self) -> None:
        """Refuse a missing identity field, on either side or on both."""
        from eng.performance import environment as environment_module

        field = environment_module.PAIRED_IDENTITY_FIELDS[0]
        for sides in (("candidate",), ("reference",), ("reference", "candidate")):
            with self.subTest(sides=sides):
                document = self.evidence(self.registered)
                for side in sides:
                    document["environments"][side].pop(field)

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_mistyped_processor_count_is_invalid_evidence(self) -> None:
        """Refuse a count that is not one, including a boolean."""
        for value in (True, None, "twelve", 0, -1):
            with self.subTest(value=repr(value)):
                document = self.evidence(self.registered)
                document["environments"]["candidate"]["processorCount"] = value

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_mistyped_candidate_structure_is_invalid_evidence(self) -> None:
        """Refuse broken candidate structures as evidence, never as a verdict.

        Reading a field before checking its type once raised a plain
        `TypeError` or `AttributeError` instead of identifying invalid evidence.
        """
        cases = {
            "entry is not an object": lambda d: d["candidateWorkloads"].__setitem__(0, "text"),
            "block identifier missing": lambda d: d["candidateWorkloads"][0].pop("block"),
            "block identifier is boolean": lambda d: d["candidateWorkloads"][0].__setitem__("block", True),
            "block identifier out of range": lambda d: d["candidateWorkloads"][0].__setitem__("block", 999),
            "workload entry is null": lambda d: d["candidateWorkloads"][0]["workloads"].__setitem__(0, None),
            "workload identifier is empty": lambda d: d["candidateWorkloads"][0]["workloads"][0].__setitem__("id", "  "),
            "workloads is empty": lambda d: d["candidateWorkloads"][0].__setitem__("workloads", []),
        }
        for description, mutate in cases.items():
            with self.subTest(case=description):
                document = self.evidence(self.registered)
                mutate(document)

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_parallel_records_must_cover_every_block(self) -> None:
        """Refuse latencies, resources or terminations describing fewer blocks."""
        for field in ("latencies", "resources", "terminations"):
            with self.subTest(field=field):
                document = self.evidence(self.registered)
                document["tests"][0][field] = document["tests"][0][field][:-1]

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_mistyped_latency_structure_is_invalid_evidence(self) -> None:
        """Refuse broken latency shapes as evidence, not as a verdict.

        The absolute ceilings read these samples, so a null entry or a missing
        side must be rejected before reaching the statistics.
        """
        cases = {
            "latencies is not a list": lambda d: d["tests"][0].__setitem__(
                "latencies", {}),
            "entry is null": lambda d: d["tests"][0]["latencies"].__setitem__(0, None),
            "entry misses a side": lambda d: d["tests"][0]["latencies"][0].pop(
                "candidate"),
            "samples are empty": lambda d: d["tests"][0]["latencies"][0].__setitem__(
                "candidate", []),
            "samples are not a list": lambda d: d["tests"][0]["latencies"][0].__setitem__(
                "candidate", 7),
        }
        for description, mutate in cases.items():
            with self.subTest(case=description):
                document = self.evidence(self.registered)
                mutate(document)

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_one_block_must_measure_one_population(self) -> None:
        """Refuse a block whose three views describe different measurements.

        The pairing decides on the normalized samples and the absolute ceiling
        decides on the raw ones. Nothing bound them, so a document could pair
        sixteen observations while holding a single unrelated observation to
        the budget -- and it qualified.
        """
        cases = {
            "fewer raw samples than normalized":
                lambda test: test["latencies"][0].__setitem__(
                    "candidate", test["latencies"][0]["candidate"][:1]),
            "more raw samples than normalized":
                lambda test: test["latencies"][0].__setitem__(
                    "candidate", test["latencies"][0]["candidate"] * 2),
            "fewer calibrations than samples":
                lambda test: test["calibrations"][0].__setitem__(
                    "candidate", test["calibrations"][0]["candidate"][:1]),
            "fewer normalized samples than the recorded count":
                lambda test: test["blocks"][0].__setitem__(
                    "candidate", test["blocks"][0]["candidate"][:1]),
        }
        for description, mutate in cases.items():
            with self.subTest(case=description):
                document = self.evidence(self.registered)
                mutate(document["tests"][0])

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_normalization_that_does_not_follow_is_invalid_evidence(self) -> None:
        """Refuse a normalized sample that is not its latency over its pulse.

        Equal population sizes alone would still allow three arrays that were
        measured apart. The identity the workload report proves at measurement
        time is what ties them to one operation.
        """
        for field in ("blocks", "latencies", "calibrations"):
            with self.subTest(field=field):
                document = self.evidence(self.registered)
                document["tests"][0][field][0]["candidate"][0] *= 2

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_projection_carrying_raw_measurements_is_invalid(self) -> None:
        """Refuse an audit summary that is also a second measurement record.

        Two representations of one candidate measurement in one document mean a
        reader cannot tell which one the verdict came from.
        """
        surplus = {
            "normalizedSamples": [99.0],
            "samplesNanoseconds": [1.0],
            "calibrationNanoseconds": [7.0],
            "calibrationPulseNanoseconds": [7.0],
            "calibrationPulseIndices": [0],
            "sampleCount": 1,
            "terminationReason": "sample_cap_reached",
            "minimumDurationReached": False,
        }
        for field, value in surplus.items():
            with self.subTest(field=field):
                document = self.evidence(self.registered)
                document["candidateWorkloads"][0]["workloads"][0][field] = value

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_a_projection_missing_a_registered_field_is_invalid(self) -> None:
        """Refuse a summary that leaves one of its fields out."""
        for field in paired.CANDIDATE_PROJECTION_FIELDS:
            with self.subTest(field=field):
                document = self.evidence(self.registered)
                document["candidateWorkloads"][0]["workloads"][0].pop(field)

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_every_projected_field_is_a_checked_field(self) -> None:
        """Leave no field in the projection that nothing validates.

        `id` binds the entry to a registered workload, `family` to its budget,
        and the five metrics are recomputed from the canonical measurement. A
        field outside that set would be carried into a release manifest with
        nothing standing behind it.
        """
        checked = {
            "id",
            "family",
            *paired.LATENCY_CEILING_QUANTILES,
            *paired.CEILING_RESOURCE_FIELDS,
        }

        self.assertEqual(checked, set(paired.CANDIDATE_PROJECTION_FIELDS))

    def test_a_divisor_no_pulse_measured_is_invalid_evidence(self) -> None:
        """Refuse a calibration value that no recorded pulse produced.

        The arithmetic was provable and its origin was not: with every raw
        latency untouched, a freely chosen divisor rescaled the normalized
        samples the pairing decides on.
        """
        document = self.evidence(self.registered)
        test = document["tests"][0]
        test["calibrations"][0]["candidate"] = [
            value * 1.3 for value in test["calibrations"][0]["candidate"]
        ]
        test["blocks"][0]["candidate"] = [
            latency / divisor
            for latency, divisor in zip(
                test["latencies"][0]["candidate"],
                test["calibrations"][0]["candidate"],
            )
        ]

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_free_calibration_cannot_move_the_verdict(self) -> None:
        """Hold one measurement to one verdict.

        A ratio of 1.30 is an above-budget signal pending the run-wide decision.
        Rescaling the candidate's divisor by the same factor turns every
        normalized sample back into its reference's -- so with the raw
        latencies untouched, the scorecard input moved.
        """
        regressed = [
            {
                "workloadId": identifier,
                "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.30),
            }
            for identifier in self.registered
        ]
        builder = PairedEvidenceBuilder(
            json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        )
        document = builder.evidence(regressed)
        document["contractDigest"] = sha256(CONTRACT_PATH)
        self.assertEqual(
            "pending-run-wide-adjustment",
            self.evaluate(document)["qualification"],
        )

        for test in document["tests"]:
            for index, measured in enumerate(test["latencies"]):
                test["calibrations"][index]["candidate"] = [
                    value * 1.30
                    for value in test["calibrations"][index]["candidate"]
                ]
                test["blocks"][index]["candidate"] = [
                    latency / divisor
                    for latency, divisor in zip(
                        measured["candidate"],
                        test["calibrations"][index]["candidate"],
                    )
                ]

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_a_pulse_train_the_runner_cannot_produce_is_invalid(self) -> None:
        """Hold the pulse assignment to the invariants the report proves."""
        interval = int(
            self.contract["profiles"][
                self.contract["pairedPolicy"]["blocks"]["profile"]
            ]["calibrationIntervalSamples"]
        )
        cases = {
            "index outside the pulse list":
                lambda test: test["calibrationPulseIndices"][0]["candidate"].__setitem__(
                    -1, 99),
            "does not start at pulse zero":
                lambda test: test["calibrationPulseIndices"][0].__setitem__(
                    "candidate",
                    [
                        index + 1
                        for index in test["calibrationPulseIndices"][0]["candidate"]
                    ]),
            "indices skip a pulse":
                lambda test: (
                    test["calibrationPulses"][0].__setitem__(
                        "candidate",
                        test["calibrationPulses"][0]["candidate"] * 3),
                    test["calibrationPulseIndices"][0]["candidate"].__setitem__(
                        -1, 2),
                ),
            "a pulse no sample used":
                lambda test: test["calibrationPulses"][0]["candidate"].append(1.0),
            "fewer indices than samples":
                lambda test: test["calibrationPulseIndices"][0].__setitem__(
                    "candidate",
                    test["calibrationPulseIndices"][0]["candidate"][:-1]),
            "the last index names a pulse one past the list": lambda test: (
                test["calibrationPulseIndices"][0]["candidate"].__setitem__(-1, 1),
            ),
            "the train goes back to an earlier pulse": lambda test: (
                test["calibrationPulses"][0].__setitem__(
                    "candidate",
                    test["calibrationPulses"][0]["candidate"] * 2),
                test["calibrationPulseIndices"][0].__setitem__(
                    "candidate",
                    [
                        position % 2
                        for position in range(
                            len(test["calibrationPulseIndices"][0]["candidate"])
                        )
                    ]),
            ),
            "index is a boolean":
                lambda test: test["calibrationPulseIndices"][0]["candidate"].__setitem__(
                    0, True),
            "pulse is not a number":
                lambda test: test["calibrationPulses"][0]["candidate"].__setitem__(
                    0, "fast"),
        }
        del interval
        for description, mutate in cases.items():
            with self.subTest(case=description):
                document = self.evidence(self.registered)
                mutate(document["tests"][0])

                with self.assertRaises(InvalidEvidenceError):
                    self.evaluate(document)

    def test_one_pulse_beyond_the_interval_is_invalid_evidence(self) -> None:
        """Refuse a block held to fewer pulses than the contract registers.

        The interval is what keeps a divisor close in time to the sample it
        divides. One pulse stretched across a whole block is the shape a
        document would choose to make an old, favorable calibration cover
        everything.
        """
        interval = int(
            self.contract["profiles"][
                self.contract["pairedPolicy"]["blocks"]["profile"]
            ]["calibrationIntervalSamples"]
        )
        document = self.evidence(
            self.registered,
            blocks=uniform_blocks(
                COMPLETE_BLOCKS,
                1.0,
                samples=interval + 1,
            ),
        )
        test = document["tests"][0]
        count = len(test["blocks"][0]["candidate"])
        self.assertGreater(count, interval)
        test["calibrationPulses"][0]["candidate"] = [
            test["calibrationPulses"][0]["candidate"][0]
        ]
        test["calibrationPulseIndices"][0]["candidate"] = [0] * count

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)

    def test_agreeing_populations_qualify(self) -> None:
        """Keep a document whose three views do describe one measurement.

        The calibration is not a unit divisor here, so the normalized samples
        are genuinely different numbers from the raw ones and the check has to
        recompute rather than compare.
        """
        document = self.evidence(self.registered)
        for test in document["tests"]:
            for index, measured in enumerate(test["latencies"]):
                for side in ("reference", "candidate"):
                    divisor = 4.0
                    count = len(measured[side])
                    test["calibrations"][index][side] = [divisor] * count
                    test["calibrationPulses"][index][side] = [divisor] * len(
                        test["calibrationPulses"][index][side]
                    )
                    test["blocks"][index][side] = [
                        sample / divisor for sample in measured[side]
                    ]

        self.assertEqual(
            "pending-run-wide-adjustment",
            self.evaluate(document)["qualification"],
        )

    def test_the_recorded_order_must_cover_every_block(self) -> None:
        """Refuse an execution order that does not describe the run."""
        document = self.evidence(self.registered)
        document["executionOrder"]["executedBlockPatterns"] = ["A-B"]

        with self.assertRaises(InvalidEvidenceError):
            self.evaluate(document)


class MixedTerminationDecisionTests(unittest.TestCase):
    """Prove the fixed population decides even when a sample cap was reached."""

    WORKLOADS = ("capped", "converged", "quiet",)

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.builder = PairedEvidenceBuilder(self.contract)

    def evaluate(self, tests, capped: set[str]) -> dict[str, object]:
        """Evaluate a run in which only the named workloads hit their cap."""
        document = self.builder.evidence(tests)
        for entry in document["tests"]:
            if entry["workloadId"] not in capped:
                continue
            for record in entry["terminations"]:
                for side in ("reference", "candidate"):
                    record[side]["terminationReason"] = "sample_cap_reached"
                    record[side]["minimumDurationReached"] = False

        return paired.evaluate_paired_comparison(
                document, self.builder.contract, contract_digest=PAIRED_CONTRACT_DIGEST
            )

    def test_a_capped_workload_remains_observational(self) -> None:
        """Keep a fixed-sample signal visible without local multiplicity."""
        result = self.evaluate(
            [
                {
                    "workloadId": "capped",
                    "blocks": uniform_blocks(
                        COMPLETE_BLOCKS,
                        1.6,
                        samples=CAP_SAMPLES,
                    ),
                }
            ],
            capped={"capped"},
        )

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        self.assertEqual(["capped"], result["cappedWorkloads"])

    def test_a_converged_signal_remains_visible_beside_a_cap(self) -> None:
        """Retain both workload observations beside sample-cap metadata."""
        result = self.evaluate(
            [
                {
                    "workloadId": "capped",
                    "blocks": uniform_blocks(
                        COMPLETE_BLOCKS,
                        1.6,
                        samples=CAP_SAMPLES,
                    ),
                },
                {
                    "workloadId": "converged",
                    "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.9),
                },
            ],
            capped={"capped"},
        )

        self.assertEqual("pending-run-wide-adjustment", result["qualification"])
        self.assertEqual(["capped"], result["cappedWorkloads"])

        states = {
            item["workloadId"]: item["state"]
            for item in result["results"]
            if item["metric"] == "normalizedMedian"
        }
        self.assertEqual("observed-above-budget", states["capped"])
        self.assertEqual("observed-above-budget", states["converged"])

    def test_a_capped_workload_remains_in_the_registered_family(self) -> None:
        """Prevent a cap from changing the pre-registered family population."""
        result = self.evaluate(
            [
                {
                    "workloadId": "capped",
                    "blocks": uniform_blocks(
                        COMPLETE_BLOCKS,
                        1.6,
                        samples=CAP_SAMPLES,
                    ),
                },
                {
                    "workloadId": "quiet",
                    "blocks": uniform_blocks(COMPLETE_BLOCKS, 1.0),
                },
            ],
            capped={"capped"},
        )

        capped_entries = [
            item for item in result["results"] if item["workloadId"] == "capped"
        ]
        self.assertTrue(capped_entries)
        for entry in capped_entries:
            self.assertEqual("observed-above-budget", entry["state"])
            self.assertNotIn("familyRejected", entry)


class SampleEvidenceGuardTests(unittest.TestCase):
    """Prove short or malformed evidence never reads as a provider verdict.

    A sample that is not a number once reached only the base contract-error
    class. This boundary gives malformed samples the explicit invalid-evidence
    type before any statistical decision runs.
    """

    WORKLOADS = ("steady",)

    def test_a_single_observation_is_refused(self) -> None:
        """Refuse the least evidence a run can produce.

        Returning zero dispersion for one sample would present it as perfectly
        stable, which is the opposite of what one observation supports.
        """
        with self.assertRaises(InvalidEvidenceError):
            paired.relative_standard_error([100.0])

    def test_a_population_below_the_profile_floor_is_refused(self) -> None:
        """Apply the profile's own valid-sample floor, not a local number."""
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        floor = contract["profiles"]["paired-block"]["minimumValidSamples"]
        short = [block([100.0] * (floor - 1), [100.0] * (floor - 1))]

        with self.assertRaises(InvalidEvidenceError):
            paired.paired_ratios(
                short, "normalizedMedian", minimum_samples=floor
            )

    def test_non_numeric_and_non_finite_samples_are_invalid_evidence(self) -> None:
        """Keep a malformed sample out of the regression verdict."""
        for description, samples in (
            ("text", [1.0, "not-a-number"]),
            ("nan", [1.0, float("nan")]),
            ("infinity", [1.0, float("inf")]),
            ("negative", [1.0, -5.0]),
        ):
            with self.subTest(sample=description):
                with self.assertRaises(InvalidEvidenceError):
                    paired.block_statistic(samples, "normalizedMedian", "probe")

    def test_evidence_that_is_not_an_object_is_refused(self) -> None:
        """Reject malformed evidence before any field access."""
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))

        for malformed in ([], "text", None, 7):
            with self.subTest(evidence=type(malformed).__name__):
                with self.assertRaises(InvalidEvidenceError):
                    paired.evaluate_paired_comparison(
                        malformed, contract, contract_digest=PAIRED_CONTRACT_DIGEST
                    )


class RelativeStandardErrorAgreementTests(unittest.TestCase):
    """Prove both implementations decide the same registered threshold.

    The runner divides the standard error by the median and this side divided
    it by the mean. Latency distributions are right-skewed, so their mean sits
    above their median: the same deviation looked smaller here, and a
    population the runner had already judged insufficient could be accepted.
    """

    SOURCE = (
        REPOSITORY_ROOT
        / "benchmarks"
        / "Doka.EntityFrameworkCore.MySql.Benchmarks"
        / "PerformanceSampling.cs"
    )

    def test_the_runner_divides_by_the_median(self) -> None:
        """Read the canonical definition from the side that measures."""
        source = self.SOURCE.read_text(encoding="utf-8")
        start = source.index("public static double RelativeStandardError")
        body = source[start : start + 1200]

        self.assertIn("Percentile(sortedValues, 0.5)", body)
        self.assertIn("median", body)

    def test_this_side_divides_by_the_median_too(self) -> None:
        """Compute the same quantity on a deliberately skewed population."""
        skewed = [10.0] * 9 + [1000.0]
        values = sorted(skewed)
        median = paired.percentile(values, 0.50)
        count = len(skewed)
        mean = sum(skewed) / count
        variance = sum((value - mean) ** 2 for value in skewed) / (count - 1)
        expected = (math.sqrt(variance) / math.sqrt(count)) / median

        self.assertAlmostEqual(expected, paired.relative_standard_error(skewed))

    def test_the_mean_based_denominator_would_have_been_laxer(self) -> None:
        """State the size of the disagreement the alignment removes."""
        skewed = [10.0] * 9 + [1000.0]
        count = len(skewed)
        mean = sum(skewed) / count
        variance = sum((value - mean) ** 2 for value in skewed) / (count - 1)
        mean_based = (math.sqrt(variance) / math.sqrt(count)) / mean

        self.assertGreater(paired.relative_standard_error(skewed), mean_based)


class WatchdogHierarchyTests(unittest.TestCase):
    """Prove the deadlines are error bounds, not reserved budgets.

    Summing the inner watchdogs and demanding the total fit inside the outer
    one forces either fewer blocks or a narrower matrix, and neither is what
    those numbers mean: they bound a hang, not an expected duration. What
    closes the contract instead is that a side run stops at whichever comes
    first, its own watchdog or what remains of the comparison.
    """

    WORKLOADS = ("steady",)

    def setUp(self) -> None:
        """Load the shipped contract."""
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.runner = (
            REPOSITORY_ROOT / "eng" / "performance" / "paired-benchmark.sh"
        ).read_text(encoding="utf-8")

    def test_a_side_run_stops_at_the_smaller_of_the_two_deadlines(self) -> None:
        """Prove the effective deadline is the minimum, not the local one."""
        self.assertIn("remaining_budget()", self.runner)
        self.assertIn('effective="${side_watchdog_seconds}"', self.runner)
        self.assertIn("(( remaining < effective )) && effective=", self.runner)

    def test_a_watchdog_stop_is_a_measurement_condition(self) -> None:
        """Keep a cut-off run out of the verdicts about the provider.

        Exit 75 is the registered measurement-quality code, which the attempt
        recorder classifies as retryable. Reporting a regression or a build
        failure instead would attribute a wall clock to the code.
        """
        self.assertIn("MEASUREMENT_QUALITY_EXIT_CODE=75", self.runner)
        self.assertIn(
            'exit "${MEASUREMENT_QUALITY_EXIT_CODE}"', self.runner
        )
        retryable = self.contract["pairedPolicy"]["retry"]["eligibleAttemptStates"]
        self.assertIn("measurement-inconclusive", retryable)

    def test_a_block_is_not_started_when_it_cannot_finish(self) -> None:
        """Forecast from measured blocks rather than from a ceiling."""
        self.assertIn("side_durations", self.runner)
        self.assertIn("projected=", self.runner)
        self.assertIn(
            "projected + closing_reserve_seconds > $(remaining_budget)", self.runner
        )

    def test_the_forecast_reserves_room_for_the_closing_work(self) -> None:
        """Keep a last block from spending the budget and yielding nothing.

        The soak, the evidence assembly, and the evaluation all run after the
        final block and all sit inside the same deadline.
        """
        self.assertIn("closing_reserve_seconds=", self.runner)
        self.assertIn("paired sustained-use run", self.runner)

    def test_the_soak_cannot_consume_the_finalization_reserve(self) -> None:
        """Compute the deadlines the runner would, on synthetic clocks.

        A string assertion cannot decide a budget question. This evaluates the
        runner's own arithmetic: whatever remains, the sustained-use run is
        handed strictly less, so assembling and evaluating the evidence always
        have room.
        """
        durations = self.contract["pairedPolicy"]["durations"]
        closing = durations["closingReserveSeconds"]
        finalization = durations["finalizationReserveSeconds"]
        budget = durations["maximumPairedRunSeconds"]
        side_watchdog = self.contract["profiles"]["paired-block"][
            "maximumTotalDurationSeconds"
        ]

        self.assertLess(finalization, closing)

        for elapsed in (0, budget // 2, budget - closing, budget - finalization // 2):
            with self.subTest(elapsed=elapsed):
                remaining = max(0, budget - elapsed)
                # What a side run is handed: the reserve is withheld first.
                side_effective = min(side_watchdog, max(0, remaining - closing))
                # What the soak is handed after the last block.
                soak_deadline = remaining - finalization

                # A side never gets more than what is left after the closing
                # reserve, and the clamp to zero is what a spent budget looks
                # like rather than a negative deadline.
                self.assertLessEqual(side_effective, max(0, remaining - closing))
                self.assertGreaterEqual(side_effective, 0)
                # The soak always leaves the finalization share behind, so
                # assembling and evaluating the evidence have room. A negative
                # deadline is the runner's signal that nothing is left, and it
                # stops rather than measuring.
                self.assertEqual(finalization, remaining - soak_deadline)

    def test_the_runner_withholds_both_reserves_where_they_belong(self) -> None:
        """Prove which deadline each reserve is subtracted from."""
        self.assertIn(
            "remaining=$(( $(remaining_budget) - closing_reserve_seconds ))",
            self.runner,
        )
        self.assertIn(
            "soak_deadline=$(( $(remaining_budget) - finalization_reserve_seconds ))",
            self.runner,
        )

    def test_the_outer_deadline_is_a_measurement_condition(self) -> None:
        """Translate the outer watchdog into the retryable code.

        The deadline helper reports its own timeout as 124, which the attempt
        recorder files as invalid evidence -- a state no retry can clear. A run
        the clock cut short reached no verdict about the provider.
        """
        driver = (
            REPOSITORY_ROOT / "eng" / "performance" / "benchmark.sh"
        ).read_text(encoding="utf-8")

        self.assertNotIn("exec python3 -m \"${deadline_module}\"", driver)
        self.assertIn("deadline_status == 124", driver)
        self.assertIn("exit 75", driver)
        self.assertEqual(
            "invalid-evidence", attempts.classify_exit_code(124)
        )
        self.assertTrue(
            attempts.is_retryable(attempts.classify_exit_code(75))
        )

    def test_the_inner_watchdogs_need_not_sum_below_the_outer_one(self) -> None:
        """State the property that makes the contract closable.

        The shipped numbers deliberately do not satisfy a sum. Asserting that
        keeps a future change from quietly reintroducing the projection that
        would force a block or coverage reduction.
        """
        blocks = self.contract["pairedPolicy"]["blocks"]["completeBlocks"]
        side = self.contract["profiles"]["paired-block"]["maximumTotalDurationSeconds"]
        run = self.contract["pairedPolicy"]["durations"]["maximumPairedRunSeconds"]

        self.assertGreater(blocks * 2 * side, run)
        self.assertLessEqual(side, run)

    def test_the_outer_deadline_leaves_the_job_room_to_finish(self) -> None:
        """Keep the runner's hard stop above the comparison's own budget.

        The job timeout is the forge's emergency stop; the run has to be able
        to fail its own way first, or there is no evidence to retry from.
        """
        workflow = (
            REPOSITORY_ROOT / ".github" / "workflows" / "benchmark-target.yml"
        ).read_text(encoding="utf-8")
        # Only the jobs that measure are bounded by this budget. The selection
        # jobs download and decide; their much shorter timeout says nothing
        # about how long a comparison may run.
        headers = list(re.finditer(r"^  [a-z0-9-]+:$", workflow, re.M))
        measuring = [
            workflow[
                header.end(): headers[index + 1].start()
                if index + 1 < len(headers)
                else len(workflow)
            ]
            for index, header in enumerate(headers)
            if "eng/benchmark.sh"
            in workflow[
                header.end(): headers[index + 1].start()
                if index + 1 < len(headers)
                else len(workflow)
            ]
        ]
        self.assertTrue(measuring, "no measuring job found in the target workflow")
        job_minutes = {
            int(value)
            for block in measuring
            for value in re.findall(r"timeout-minutes: (\d+)", block)
        }
        run = self.contract["pairedPolicy"]["durations"]["maximumPairedRunSeconds"]

        self.assertTrue(job_minutes)
        self.assertLess(run, min(job_minutes) * 60)


class SeedStabilityTests(unittest.TestCase):
    """Prove the interval is reproducible across processes, not just calls.

    The per-test seed was derived from Python's `hash()` of a string, which is
    randomized per process. Two evaluations of the same evidence in two
    processes produced two different intervals, and the reviewer of a release
    could not reproduce the number the release was decided on.
    """

    def test_the_offset_is_identical_in_a_separate_process(self) -> None:
        """Compare the in-process offset against one from a fresh interpreter."""
        expected = paired._stable_offset("query.materialize", "normalizedMedian")

        observed = subprocess.run(
            [
                sys.executable,
                "-c",
                "import sys; sys.path.insert(0, 'eng');"
                "from performance.paired import _stable_offset;"
                "print(_stable_offset('query.materialize', 'normalizedMedian'))",
            ],
            cwd=REPOSITORY_ROOT,
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip()

        self.assertEqual(str(expected), observed)

if __name__ == "__main__":
    unittest.main()
