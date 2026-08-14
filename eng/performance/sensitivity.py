#!/usr/bin/env python3
"""Verify the pre-registered detection power of the paired comparison.

The release verdict uses the production BCa bootstrap and run-wide Holm
procedure. This module exercises that same decision against a fixed log-normal
planning model, rather than estimating power with a different test.
The simulation is deterministic so a contract review can reproduce the result
and a contract change cannot silently inherit an obsolete power claim.
"""

from __future__ import annotations

import math
import random
import re
from pathlib import Path
from statistics import NormalDist
from typing import Any

from .contract import (
    InvalidEvidenceError,
    close_enough,
    load_json,
    sha256,
    validate_paired_policy,
)
from .paired import bca_interval, bootstrap_replicates, exact_sign_flip_p_value


def validate_registered_characterization(
    contract: dict[str, Any],
    repository_root: Path,
) -> dict[str, Any]:
    """Verify the immutable characterization behind the sensitivity limit."""
    policy = validate_paired_policy(contract)
    binding = policy["sensitivity"]["characterization"]
    path = repository_root / binding["path"]
    if path.is_symlink() or not path.is_file():
        raise InvalidEvidenceError(
            "Registered sensitivity characterization must be a regular file."
        )
    if sha256(path) != binding["sha256"]:
        raise InvalidEvidenceError(
            "Registered sensitivity characterization digest does not match."
        )
    payload = load_json(path)
    if payload.get("schemaVersion") != 1 or payload.get("kind") != (
        "paired-dispersion-characterization"
    ):
        raise InvalidEvidenceError(
            "Registered sensitivity characterization has an unsupported contract."
        )
    if payload.get("decisionUse") != "planning-only-not-qualification":
        raise InvalidEvidenceError(
            "Characterization measurements must not double as qualification."
        )
    endpoint = payload.get("endpoint")
    if endpoint != {
        "metric": policy["primaryFamily"]["metric"],
        "aggregation": policy["primaryFamily"]["aggregation"],
    }:
        raise InvalidEvidenceError(
            "Characterization endpoint differs from the required endpoint."
        )
    bound = payload.get("confidenceBound")
    if not isinstance(bound, dict):
        raise InvalidEvidenceError("Characterization confidenceBound is required.")
    if bound.get("method") != (
        "nist-one-sided-chi-square-upper-standard-deviation"
    ) or not close_enough(float(bound.get("confidenceLevel", 0)), 0.99):
        raise InvalidEvidenceError(
            "Characterization must use the registered NIST 99 percent bound."
        )
    workflow_run_id = payload.get("sourceWorkflowRunId")
    if isinstance(workflow_run_id, bool) or not isinstance(workflow_run_id, int):
        raise InvalidEvidenceError(
            "Characterization sourceWorkflowRunId must be an integer."
        )
    for field in ("sourceCommit", "referenceCommit"):
        if not re.fullmatch(r"[0-9a-f]{40}", str(payload.get(field, ""))):
            raise InvalidEvidenceError(
                f"Characterization {field} must be a lowercase Git commit."
            )
    if not re.fullmatch(
        r"[0-9a-f]{64}", str(payload.get("contractDigest", ""))
    ):
        raise InvalidEvidenceError(
            "Characterization contractDigest must be a lowercase SHA-256."
        )
    runner_class = payload.get("runnerClass")
    if not isinstance(runner_class, str) or not runner_class.strip():
        raise InvalidEvidenceError("Characterization runnerClass is required.")

    sources = payload.get("sources")
    if not isinstance(sources, list) or len(sources) != 4:
        raise InvalidEvidenceError(
            "Characterization requires exactly four independent hosted attempts."
        )
    deviations: list[float] = []
    target_attempts: set[tuple[str, int]] = set()
    for index, source in enumerate(sources):
        target = source.get("target")
        attempt = source.get("attempt")
        if (
            not isinstance(target, str)
            or not target.strip()
            or isinstance(attempt, bool)
            or attempt not in (1, 2)
        ):
            raise InvalidEvidenceError(
                f"Characterization sources[{index}] identity is invalid."
            )
        identity = (target, attempt)
        if identity in target_attempts:
            raise InvalidEvidenceError(
                f"Characterization repeats source {target} attempt {attempt}."
            )
        target_attempts.add(identity)
        expected_run_id = f"github-{workflow_run_id}-{target}-attempt-{attempt}"
        if source.get("runId") != expected_run_id:
            raise InvalidEvidenceError(
                f"Characterization sources[{index}] runId is inconsistent."
            )
        artifact_id = source.get("artifactId")
        if (
            isinstance(artifact_id, bool)
            or not isinstance(artifact_id, int)
            or artifact_id < 1
        ):
            raise InvalidEvidenceError(
                f"Characterization sources[{index}] artifactId is invalid."
            )
        if not re.fullmatch(
            r"[0-9a-f]{64}",
            str(source.get("pairedEvidenceSha256", "")),
        ):
            raise InvalidEvidenceError(
                f"Characterization sources[{index}] evidence hash is invalid."
            )
        ratios = source.get("aggregateBlockRatios")
        if not isinstance(ratios, list) or len(ratios) < 2:
            raise InvalidEvidenceError(
                f"Characterization sources[{index}] has no block population."
            )
        logarithms = [math.log(float(value)) for value in ratios]
        mean = sum(logarithms) / len(logarithms)
        deviation = math.sqrt(
            sum((value - mean) ** 2 for value in logarithms)
            / (len(logarithms) - 1)
        )
        if not close_enough(
            deviation, float(source.get("logRatioStandardDeviation", -1))
        ):
            raise InvalidEvidenceError(
                f"Characterization sources[{index}] dispersion is inconsistent."
            )
        deviations.append(deviation)

    targets = {target for target, _ in target_attempts}
    if len(targets) != 2 or any(
        {(target, 1), (target, 2)} - target_attempts for target in targets
    ):
        raise InvalidEvidenceError(
            "Characterization must contain two attempts for each of two targets."
        )

    observed_maximum = max(deviations)
    if not close_enough(
        observed_maximum,
        float(bound.get("observedMaximumLogRatioStandardDeviation", -1)),
    ):
        raise InvalidEvidenceError(
            "Characterization observed maximum dispersion is inconsistent."
        )
    degrees_of_freedom = int(bound.get("degreesOfFreedom", 0))
    critical_value = float(bound.get("lowerTailCriticalValue", 0))
    if degrees_of_freedom != 7 or not close_enough(critical_value, 1.239):
        raise InvalidEvidenceError(
            "Characterization must use the registered NIST 99 percent bound."
        )
    upper_bound = observed_maximum * math.sqrt(
        degrees_of_freedom / critical_value
    )
    if not close_enough(
        upper_bound, float(bound.get("upperLogRatioStandardDeviation", -1))
    ):
        raise InvalidEvidenceError(
            "Characterization upper dispersion bound is inconsistent."
        )
    if not close_enough(
        upper_bound,
        float(policy["sensitivity"]["maximumLogRatioStandardDeviation"]),
    ):
        raise InvalidEvidenceError(
            "Sensitivity limit differs from its characterization bound."
        )

    return payload


def _standard_normal(generator: random.Random) -> float:
    """Draw one normal variate without relying on a runtime-specific cache.

    Python's public normal helpers may change their internal sampling strategy.
    The explicit Box-Muller transform binds the simulation to the registered
    pseudo-random stream instead of to that implementation detail.
    """
    first = 1 - generator.random()
    second = generator.random()

    return math.sqrt(-2 * math.log(first)) * math.cos(2 * math.pi * second)


def _wilson_lower_bound(
    successes: int,
    trials: int,
    confidence: float,
) -> float:
    """Return the one-sided Wilson lower bound for a success probability."""
    proportion = successes / trials
    quantile = NormalDist().inv_cdf(confidence)
    squared = quantile * quantile
    denominator = 1 + squared / trials
    center = (proportion + squared / (2 * trials)) / denominator
    half_width = (
        quantile
        * math.sqrt(
            proportion * (1 - proportion) / trials
            + squared / (4 * trials * trials)
        )
        / denominator
    )

    return center - half_width


def evaluate_registered_sensitivity(
    contract: dict[str, Any],
) -> dict[str, Any]:
    """Return deterministic evidence that the registered block plan has power.

    The planning case assumes exactly one real required-target regression.
    Holm then requires its exact p-value to pass the first-rank threshold,
    `alpha / required_targets`. Ratios are generated at the registered
    detectable effect and conservative characterization bound. A Wilson lower
    bound, rather than the point estimate, must meet the power target.
    """
    policy = validate_paired_policy(contract)
    sensitivity = policy["sensitivity"]
    target_roles = policy["targetRoles"]
    required_targets = sorted(
        target for target, role in target_roles.items() if role == "required"
    )
    if not required_targets:
        raise InvalidEvidenceError(
            "Sensitivity assurance requires at least one required target."
        )

    trials = int(sensitivity["simulationTrials"])
    simulation_seed = int(sensitivity["simulationSeed"])
    standard_deviation = float(
        sensitivity["maximumLogRatioStandardDeviation"]
    )
    detectable_multiple = float(
        sensitivity["minimumDetectableBudgetMultiple"]
    )
    blocks = int(policy["blocks"]["completeBlocks"])
    interval = policy["interval"]
    family_threshold = (
        float(policy["multipleComparison"]["familyWiseErrorRate"])
        / len(required_targets)
    )

    detections = 0
    for trial in range(trials):
        generator = random.Random(simulation_seed + trial)
        ratios = [
            math.exp(
                math.log(detectable_multiple)
                + standard_deviation * _standard_normal(generator)
            )
            for _ in range(blocks)
        ]
        replicates = bootstrap_replicates(
            ratios,
            resamples=int(interval["resampleCount"]),
            seed=int(interval["resamplingSeed"]) + trial,
        )
        lower_bound, _ = bca_interval(
            ratios,
            replicates,
            confidence=float(interval["confidenceLevel"]),
            sidedness=interval["sidedness"],
        )
        p_value = exact_sign_flip_p_value(ratios, 1.0)
        if p_value <= family_threshold and lower_bound > 1.0:
            detections += 1

    confidence = float(sensitivity["simulationConfidenceLevel"])
    power_lower_bound = _wilson_lower_bound(detections, trials, confidence)

    return {
        "method": sensitivity["method"],
        "familyCase": sensitivity["familyCase"],
        "blocks": blocks,
        "familySize": len(required_targets),
        "familyThreshold": family_threshold,
        "minimumPower": float(sensitivity["minimumPower"]),
        "simulationConfidenceLevel": confidence,
        "simulationTrials": trials,
        "detections": detections,
        "estimatedPower": detections / trials,
        "powerLowerBound": power_lower_bound,
        "maximumLogRatioStandardDeviation": standard_deviation,
        "minimumDetectableBudgetMultiple": detectable_multiple,
        "minimumDetectableRatios": {
            policy["primaryFamily"]["metric"]: float(
                policy["practicalBudgets"][policy["primaryFamily"]["metric"]]
            )
            * detectable_multiple
        },
    }
