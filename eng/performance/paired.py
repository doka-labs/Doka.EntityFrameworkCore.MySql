#!/usr/bin/env python3
"""Evaluate a paired same-run comparison under a pre-registered policy.

A historical comparison asks whether today's measurement resembles one taken
on another machine days ago. This module asks a different question: within one
allocated runner, measuring a reference and a candidate provider alternately,
does the candidate exceed its practical budget. Because both sides share the
processor, runtime, engine image, and database preparation, the hardware
cancels out of every paired ratio instead of having to be matched.

Every value that can move the decision comes from the contract's registered
paired policy. Nothing here carries an implementation default.
"""

from __future__ import annotations

import hashlib
import json
import math
import random
import re
from pathlib import Path
from typing import Any, Sequence

if __package__:
    from .contract import (
        PRECISION_REACHED,
        required_commit,
        required_sha256,
        SAMPLE_CAP_REACHED,
        TERMINATION_REASONS,
        InvalidEvidenceError,
        MeasurementQualityError,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
        validate_paired_policy,
    )
    from .environment import (
        validate_paired_benchmark_driver,
        validate_paired_environment,
    )
    from .reports import (
        validate_absolute_budgets,
        validate_workload_report,
    )
    from .soak import validate_soak_report
    from .statistics import percentile
else:
    from contract import (
        PRECISION_REACHED,
        required_commit,
        required_sha256,
        SAMPLE_CAP_REACHED,
        TERMINATION_REASONS,
        InvalidEvidenceError,
        MeasurementQualityError,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
        validate_paired_policy,
    )
    from environment import (
        validate_paired_benchmark_driver,
        validate_paired_environment,
    )
    from reports import (
        validate_absolute_budgets,
        validate_workload_report,
    )
    from soak import validate_soak_report
    from statistics import percentile


METRIC_QUANTILES = {
    "normalizedMedian": 0.50,
    "normalizedP95": 0.95,
    "normalizedP99": 0.99,
}

SIDES = ("reference", "candidate")


def _evidence_number(value: Any, label: str, *, minimum: float = 0.0) -> float:
    """Validate one measured value as evidence rather than as input.

    The generic numeric guard raises the base error, which the command line
    maps to exit 1 -- and the attempt recorder reads exit 1 as `regression`. A
    sample that is not a number says nothing about the provider, so it is
    re-raised in the invalid-evidence domain and leaves as exit 78.
    """
    try:
        return finite_number(value, label, minimum=minimum)
    except InvalidEvidenceError:
        raise
    except PerformanceEvidenceError as error:
        raise InvalidEvidenceError(str(error)) from error


def block_statistic(samples: Sequence[float], metric: str, label: str) -> float:
    """Reduce one side of one block to the metric's quantile.

    Statistics are formed inside a block and never pooled across blocks before
    pairing. Pooling first would average away the very pairing that makes the
    comparison valid.
    """
    if metric not in METRIC_QUANTILES:
        raise InvalidEvidenceError(f"{label} declares unknown metric '{metric}'.")
    if not samples:
        raise InvalidEvidenceError(f"{label} contains no samples.")
    values = [
        _evidence_number(sample, f"{label}[{index}]")
        for index, sample in enumerate(samples)
    ]

    return percentile(sorted(values), METRIC_QUANTILES[metric])


def relative_standard_error(samples: Sequence[float], label: str = "samples") -> float:
    """Return the relative standard error of one side of one block.

    Relative to the median, which is what the measuring runner uses. The two
    implementations decide the same registered threshold, so a mean-based
    denominator here would let this side accept a population the runner had
    already judged insufficient: latency distributions are right-skewed, so
    their mean sits above their median and the same deviation looks smaller
    against it.

    Fewer than two observations cannot express dispersion at all. Returning
    zero for a single sample would present the least evidence a run can produce
    as perfectly stable, so it is refused instead.
    """
    values = [
        _evidence_number(sample, f"{label}[{index}]")
        for index, sample in enumerate(samples)
    ]
    count = len(values)
    if count < 2:
        raise InvalidEvidenceError(
            f"{label} carries {count} observation(s); dispersion needs at "
            "least two."
        )
    median = percentile(sorted(values), 0.50)
    if median <= 0:
        raise InvalidEvidenceError(
            f"{label} has a non-positive median; a relative error has no "
            "meaning against it."
        )
    mean = sum(values) / count
    variance = sum((value - mean) ** 2 for value in values) / (count - 1)

    return (math.sqrt(variance) / math.sqrt(count)) / median


def paired_ratios(
    blocks: Sequence[dict[str, Any]],
    metric: str,
    *,
    quality_floor: float = float("inf"),
    maximum_count_ratio: float = float("inf"),
    minimum_samples: int = 2,
) -> list[float]:
    """Return one candidate-to-reference ratio per complete block.

    The two sides need not contribute the same number of samples. Extension is
    driven by precision, so a noisier side needs more of them to reach the same
    error budget; forcing equal counts would leave that side less precisely
    estimated, which is the opposite of comparable. Both sides are held to the
    same registered error budget instead.

    What does stay bounded is how far the two populations may drift apart. A
    side that took several times longer no longer interleaves with the other,
    and the shared machine the pairing depends on is then only shared in name.

    A block does not have to converge on its own -- the run\'s power comes from
    many blocks -- but it must not be so noisy that its ratio is meaningless.
    That floor is the registered block quality threshold, applied per side.
    """
    ratios: list[float] = []
    for index, block in enumerate(blocks):
        label = f"block[{index}]"
        if not isinstance(block, dict):
            raise InvalidEvidenceError(f"{label} is not an object.")
        sides = {}
        for side in SIDES:
            samples = block.get(side)
            if not isinstance(samples, list):
                raise InvalidEvidenceError(f"{label}.{side} is required.")
            sides[side] = samples
        counts = {side: len(sides[side]) for side in SIDES}
        if min(counts.values()) < minimum_samples:
            raise InvalidEvidenceError(
                f"{label} contributed {min(counts.values())} samples on a side; "
                f"the profile requires at least {minimum_samples} for a valid "
                "measurement."
            )
        observed_ratio = max(counts.values()) / min(counts.values())
        if observed_ratio > maximum_count_ratio:
            raise InvalidEvidenceError(
                f"{label} allocated {counts['reference']} reference and "
                f"{counts['candidate']} candidate samples, a ratio of "
                f"{observed_ratio:.2f} above the registered "
                f"{maximum_count_ratio:.2f}. Populations that far apart did not "
                "measure the same stretch of time."
            )
        for side in SIDES:
            error = relative_standard_error(sides[side], f"{label}.{side}")
            if error > quality_floor:
                # Not reaching the error budget is a statement about the
                # machine, not about the provider. Raising invalid evidence
                # here would exit 78, which is not retryable; a measurement
                # condition earns the bounded retry the policy registers.
                raise MeasurementQualityError(
                    f"{label}.{side} has relative standard error {error:.4f}, "
                    f"above the registered block floor {quality_floor:.4f}. A "
                    "block that noisy contributes a ratio the run cannot use."
                )
        reference = block_statistic(sides["reference"], metric, f"{label}.reference")
        candidate = block_statistic(sides["candidate"], metric, f"{label}.candidate")
        if reference <= 0:
            raise InvalidEvidenceError(
                f"{label}.reference produced a non-positive {metric}."
            )
        ratios.append(candidate / reference)

    return ratios


def _stable_offset(*parts: str) -> int:
    """Return a process-independent offset derived from the given key parts."""
    key = "\x00".join(parts).encode("utf-8")

    return int.from_bytes(hashlib.sha256(key).digest()[:8], "big") % 1000003


def _median(values: Sequence[float]) -> float:
    """Return the median of an unsorted sequence."""
    return percentile(sorted(values), 0.50)


def log_ratio_standard_deviation(ratios: Sequence[float]) -> float:
    """Return sample dispersion on the multiplicative comparison scale."""
    if len(ratios) < 2:
        raise InvalidEvidenceError(
            "Log-ratio dispersion requires at least two complete blocks."
        )
    logarithms = [math.log(value) for value in ratios]
    mean = sum(logarithms) / len(logarithms)
    variance = sum((value - mean) ** 2 for value in logarithms) / (
        len(logarithms) - 1
    )

    return math.sqrt(variance)


def _normal_quantile(probability: float) -> float:
    """Return the standard-normal quantile for a probability.

    Acklam's rational approximation is used so the module needs no third-party
    dependency; its absolute error is far below the resolution any release
    decision here depends on.
    """
    if not 0 < probability < 1:
        raise InvalidEvidenceError("Quantile probability must lie between 0 and 1.")

    a = (-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02,
         1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00)
    b = (-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02,
         6.680131188771972e+01, -1.328068155288572e+01)
    c = (-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
         -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00)
    d = (7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00,
         3.754408661907416e+00)
    low, high = 0.02425, 1 - 0.02425
    if probability < low:
        q = math.sqrt(-2 * math.log(probability))
        return (((((c[0]*q + c[1])*q + c[2])*q + c[3])*q + c[4])*q + c[5]) / \
               ((((d[0]*q + d[1])*q + d[2])*q + d[3])*q + 1)
    if probability > high:
        q = math.sqrt(-2 * math.log(1 - probability))
        return -(((((c[0]*q + c[1])*q + c[2])*q + c[3])*q + c[4])*q + c[5]) / \
                ((((d[0]*q + d[1])*q + d[2])*q + d[3])*q + 1)
    q = probability - 0.5
    r = q * q
    return (((((a[0]*r + a[1])*r + a[2])*r + a[3])*r + a[4])*r + a[5]) * q / \
           (((((b[0]*r + b[1])*r + b[2])*r + b[3])*r + b[4])*r + 1)


def _normal_cdf(value: float) -> float:
    """Return the standard-normal cumulative probability."""
    return 0.5 * (1 + math.erf(value / math.sqrt(2)))


def bootstrap_replicates(
    ratios: Sequence[float],
    *,
    resamples: int,
    seed: int,
) -> list[float]:
    """Resample the paired ratios deterministically.

    The seed is part of the registered policy, so two evaluations of the same
    evidence produce the same interval. A non-reproducible interval could not
    be reviewed.
    """
    generator = random.Random(seed)
    size = len(ratios)
    replicates = []
    for _ in range(resamples):
        sample = [ratios[generator.randrange(size)] for _ in range(size)]
        replicates.append(_median(sample))

    return replicates


def bca_interval(
    ratios: Sequence[float],
    replicates: Sequence[float],
    *,
    confidence: float,
    sidedness: str,
) -> tuple[float, float]:
    """Return the bias-corrected and accelerated interval for the median ratio.

    The plain percentile interval is biased when the statistic's distribution
    is skewed, which a ratio distribution generally is. BCa corrects for that
    bias and for the rate at which the variance changes with the statistic.
    """
    observed = _median(ratios)
    below = sum(1 for value in replicates if value < observed)
    if below in (0, len(replicates)):
        # Every replicate sits on one side of the observation, so the bias
        # correction is undefined. That happens when the ratios are effectively
        # constant; the interval then degenerates to the observation itself.
        return observed, observed

    bias = _normal_quantile(below / len(replicates))

    # Jackknife acceleration: how fast the statistic moves as single blocks are
    # removed. Highly influential blocks widen the interval on their side.
    jackknife = []
    for index in range(len(ratios)):
        reduced = [value for position, value in enumerate(ratios) if position != index]
        jackknife.append(_median(reduced))
    mean = sum(jackknife) / len(jackknife)
    deviations = [mean - value for value in jackknife]
    numerator = sum(value ** 3 for value in deviations)
    denominator = 6 * (sum(value ** 2 for value in deviations) ** 1.5)
    acceleration = numerator / denominator if denominator else 0.0

    if sidedness == "two-sided":
        alphas = ((1 - confidence) / 2, 1 - (1 - confidence) / 2)
    else:
        alphas = (0.0, confidence)

    bounds = []
    ordered = sorted(replicates)
    for alpha in alphas:
        if alpha <= 0:
            bounds.append(min(ordered))
            continue
        if alpha >= 1:
            bounds.append(max(ordered))
            continue
        z = _normal_quantile(alpha)
        adjusted = bias + (bias + z) / (1 - acceleration * (bias + z))
        position = _normal_cdf(adjusted)
        index = min(len(ordered) - 1, max(0, int(position * len(ordered))))
        bounds.append(ordered[index])

    return bounds[0], bounds[1]


def exact_sign_flip_p_value(
    ratios: Sequence[float],
    budget: float,
) -> float:
    """Return an exact one-sided p-value for paired log-ratio differences.

    Under the boundary null, each counterbalanced block's centered log ratio
    is exchangeable with its sign reversed. Enumerating all sign assignments
    forms the exact randomization distribution; the ten-block contract keeps
    that at 1,024 assignments and removes Monte Carlo error from the value Holm
    consumes.
    """
    if not ratios or budget <= 0 or any(ratio <= 0 for ratio in ratios):
        raise InvalidEvidenceError(
            "An exact sign-flip test requires positive ratios and budget."
        )
    if len(ratios) != 10:
        raise InvalidEvidenceError(
            "The exact sign-flip test requires the registered ten blocks."
        )

    centered = [math.log(ratio / budget) for ratio in ratios]
    observed = sum(centered)
    tolerance = max(1e-14, abs(observed) * 1e-14)
    assignments = 1 << len(centered)
    extreme = 0
    for mask in range(assignments):
        randomized = sum(
            value if mask & (1 << index) else -value
            for index, value in enumerate(centered)
        )
        if randomized >= observed - tolerance:
            extreme += 1

    return extreme / assignments


def holm_rejections(
    p_values: Sequence[float],
    family_wise_error_rate: float,
) -> list[bool]:
    """Return step-down rejections with run-wide family-wise error control.

    A release turns red when any required target rejects. The relevant error is
    therefore the probability of at least one false rejection, not the expected
    proportion of false discoveries inside several separately adjusted groups.
    Holm's procedure controls that run-wide error without assuming independent
    targets or a particular correlation structure.
    """
    if not p_values:
        return []

    indexed = sorted(enumerate(p_values), key=lambda item: item[1])
    total = len(p_values)
    rejected = [False] * total
    for position, (original, p_value) in enumerate(indexed):
        threshold = family_wise_error_rate / (total - position)
        if p_value > threshold and not close_enough(p_value, threshold):
            break
        rejected[original] = True

    return rejected


def prepare_endpoint(
    ratios: Sequence[float],
    *,
    identity: str,
    metric: str,
    budget: float,
    policy: dict[str, Any],
) -> dict[str, Any]:
    """Return the reproducible interval and p-value for one endpoint."""
    interval_policy = policy["interval"]
    seed = int(interval_policy["resamplingSeed"]) + _stable_offset(
        identity, metric
    )
    replicates = bootstrap_replicates(
        ratios,
        resamples=int(interval_policy["resampleCount"]),
        seed=seed,
    )
    low, high = bca_interval(
        ratios,
        replicates,
        confidence=float(interval_policy["confidenceLevel"]),
        sidedness=interval_policy["sidedness"],
    )

    return {
        "metric": metric,
        "blocks": len(ratios),
        "observedRatio": _median(ratios),
        "lowerBound": low,
        "upperBound": high,
        "budget": budget,
        "logRatioStandardDeviation": log_ratio_standard_deviation(ratios),
        "pValue": exact_sign_flip_p_value(ratios, budget),
    }


def _geometric_mean(values: Sequence[float]) -> float:
    """Return the geometric mean used for normalized workload ratios."""
    if not values or any(value <= 0 for value in values):
        raise InvalidEvidenceError(
            "A normalized workload aggregate requires positive ratios."
        )

    return math.exp(sum(math.log(value) for value in values) / len(values))


def evaluate_primary_endpoint(
    tests: Sequence[dict[str, Any]],
    *,
    metric: str,
    budget: float,
    policy: dict[str, Any],
    minimum_samples: int = 2,
) -> dict[str, Any]:
    """Evaluate the one required target-level latency endpoint.

    Each block contributes the geometric mean of every named workload's
    normalized median ratio. This keeps the complete workload matrix in the
    required score while avoiding hundreds of independent paths to a false red
    verdict. The cross-target Holm decision is deliberately deferred until all
    target evaluations are available in the scorecard job.
    """
    workload_ratios: list[list[float]] = []
    for test in tests:
        workload_ratios.append(
            paired_ratios(
                test["blocks"],
                metric,
                quality_floor=float(
                    policy["blocks"]["maximumRelativeStandardError"]
                ),
                maximum_count_ratio=float(
                    policy["blocks"]["maximumSampleCountRatio"]
                ),
                minimum_samples=minimum_samples,
            )
        )
    block_counts = {len(ratios) for ratios in workload_ratios}
    if len(block_counts) != 1:
        raise InvalidEvidenceError(
            "Primary endpoint workloads contributed different block counts."
        )
    block_count = next(iter(block_counts))
    aggregate_ratios = [
        _geometric_mean([ratios[block] for ratios in workload_ratios])
        for block in range(block_count)
    ]
    endpoint = prepare_endpoint(
        aggregate_ratios,
        identity="target-workload-geometric-mean",
        metric=metric,
        budget=budget,
        policy=policy,
    )
    endpoint.update(
        {
            "role": "required",
            "aggregation": "geometric-mean-across-workloads",
            "workloadCount": len(workload_ratios),
            "aggregateBlockRatios": aggregate_ratios,
            "runWideRejected": None,
        }
    )
    maximum_deviation = float(
        policy["sensitivity"]["maximumLogRatioStandardDeviation"]
    )
    if (
        endpoint["logRatioStandardDeviation"] > maximum_deviation
        and not close_enough(
            endpoint["logRatioStandardDeviation"], maximum_deviation
        )
    ):
        endpoint["state"] = "insufficient-sensitivity"
    else:
        endpoint["state"] = "pending-run-wide-adjustment"

    return endpoint


def evaluate_observational_family(
    tests: Sequence[dict[str, Any]],
    *,
    metric: str,
    budget: float,
    policy: dict[str, Any],
    minimum_samples: int = 2,
) -> list[dict[str, Any]]:
    """Describe one per-workload family without making a release claim."""
    results: list[dict[str, Any]] = []
    for test in tests:
        workload = test["workloadId"]
        ratios = paired_ratios(
            test["blocks"],
            metric,
            quality_floor=float(policy["blocks"]["maximumRelativeStandardError"]),
            maximum_count_ratio=float(policy["blocks"]["maximumSampleCountRatio"]),
            minimum_samples=minimum_samples,
        )
        item = prepare_endpoint(
            ratios,
            identity=workload,
            metric=metric,
            budget=budget,
            policy=policy,
        )
        item.update(
            {
                "workloadId": workload,
                "role": "observational",
                "aggregation": "per-workload",
            }
        )
        above_budget = item["lowerBound"] > budget and not close_enough(
            item["lowerBound"], budget
        )
        within_budget = item["upperBound"] <= budget or close_enough(
            item["upperBound"], budget
        )
        if above_budget:
            item["state"] = "observed-above-budget"
        elif within_budget:
            item["state"] = "observed-within-budget"
        else:
            item["state"] = "observed-overlap"
        results.append(item)

    return results


def paired_resource_ratios(
    resources: Sequence[dict[str, dict[str, float]]],
    metric: str,
    label: str,
) -> list[float]:
    """Return one candidate-to-reference ratio per block for a scalar resource.

    A resource metric is a count, not a sample distribution, so it is compared
    directly rather than through an interval. The zero cases are decided here
    instead of being allowed to produce a division error: a reference that
    allocated nothing and a candidate that allocates something is the clearest
    regression this guard exists to catch.
    """
    ratios: list[float] = []
    for index, block in enumerate(resources):
        sides = {}
        for side in SIDES:
            value = block.get(side, {}).get(metric)
            sides[side] = _evidence_number(
                value, f"{label}[{index}].{side}.{metric}"
            )
        if sides["reference"] == 0:
            ratios.append(1.0 if sides["candidate"] == 0 else float("inf"))
            continue
        ratios.append(sides["candidate"] / sides["reference"])

    return ratios


def evaluate_resource_families(
    tests: Sequence[dict[str, Any]],
    policy: dict[str, Any],
) -> list[dict[str, Any]]:
    """Compare every registered resource metric against its paired budget.

    Latency is what the interval machinery is for. Allocation and collection
    counts are near-deterministic for a fixed code path, so a bootstrap over
    them would describe the resampling rather than the provider. The median
    block ratio against a registered budget is the honest test.
    """
    results: list[dict[str, Any]] = []
    for family in policy["resourceFamilies"]:
        metric = family["metric"]
        budget = float(family["budget"])
        for test in tests:
            workload = test["workloadId"]
            resources = test.get("resources")
            if not isinstance(resources, list) or not resources:
                raise InvalidEvidenceError(
                    f"Workload '{workload}' carries no resource measurements."
                )
            ratios = paired_resource_ratios(
                resources, metric, f"tests[{workload}].resources"
            )
            observed = _median(ratios)
            within = observed <= budget or close_enough(observed, budget)
            results.append(
                {
                    "workloadId": workload,
                    "metric": metric,
                    "blocks": len(ratios),
                    "observedRatio": observed,
                    "budget": budget,
                    "state": "qualified" if within else "regression",
                }
            )

    return results


def _block_ceiling_checks(
    workloads: Sequence[dict[str, Any]],
    contract: dict[str, Any],
    policy: dict[str, Any],
) -> list[dict[str, Any]]:
    """Check one block's candidate measurements against the family budgets.

    The family comes from the contract, never from the entry being judged. A
    document that names its own budget family can choose the ceiling it is held
    to, which is how a 200 ms measurement re-declared from `concurrency` to
    `write` turned a regression into a qualified release.
    """
    definitions = {
        definition["id"]: definition for definition in contract["workloads"]
    }
    resolved: list[dict[str, Any]] = []
    for workload in workloads:
        definition = definitions.get(workload.get("id"))
        if definition is None:
            raise InvalidEvidenceError(
                f"Candidate measurement names workload {workload.get('id')!r}, "
                "which the contract does not register."
            )
        resolved.append({**workload, "family": definition["family"]})

    try:
        checks = list(validate_absolute_budgets(resolved, contract, strict=False))
    except PerformanceEvidenceError as error:
        # A malformed candidate metric says nothing about the provider, so it
        # must not leave through the general error domain the command line
        # maps to exit 1 and the attempt recorder reads as a regression.
        raise InvalidEvidenceError(str(error)) from error

    if "gen2CollectionsPer1000" in policy["absoluteCeilings"]["metrics"]:
        for workload in resolved:
            budget = contract["familyBudgets"][workload["family"]]
            maximum = finite_number(
                budget.get("gen2CollectionsPer1000"),
                f"familyBudgets.{workload['family']}.gen2CollectionsPer1000",
                minimum=0,
            )
            try:
                actual = finite_number(
                    workload.get("gen2CollectionsPer1000"),
                    f"{workload['id']}.gen2CollectionsPer1000",
                    minimum=0,
                )
            except PerformanceEvidenceError as error:
                raise InvalidEvidenceError(str(error)) from error
            checks.append(
                {
                    "workloadId": workload["id"],
                    "metric": "gen2CollectionsPer1000",
                    "actual": actual,
                    "maximum": maximum,
                    "passed": actual <= maximum,
                }
            )

    return checks


LATENCY_CEILING_QUANTILES = {
    "medianNanoseconds": 0.50,
    "p95Nanoseconds": 0.95,
    "p99Nanoseconds": 0.99,
}

CEILING_RESOURCE_FIELDS = ("allocatedBytesPerOperation", "gen2CollectionsPer1000")

# The audit projection's complete shape. It is a summary of the candidate side
# and nothing else: carrying the workload report's raw arrays here put a second
# copy of every measurement into the document, unchecked, beside the canonical
# one the decisions read.
CANDIDATE_PROJECTION_FIELDS = (
    "id",
    "family",
    *LATENCY_CEILING_QUANTILES,
    *CEILING_RESOURCE_FIELDS,
)


def candidate_audit_projection(workload: dict[str, Any]) -> dict[str, Any]:
    """Reduce one candidate workload entry to the audit summary.

    One producer for the shape, so the assembler and the fixtures cannot prove
    different documents. Missing fields are carried as absent rather than
    defaulted: the evaluator has to see the gap.
    """
    return {
        field: workload[field]
        for field in CANDIDATE_PROJECTION_FIELDS
        if field in workload
    }


def candidate_ceiling_measurement(
    test: dict[str, Any],
    definition: dict[str, Any],
    *,
    block: int,
) -> dict[str, Any]:
    """Reduce one block's candidate measurement to what the ceilings read.

    Everything here comes from the same arrays the paired decision is formed
    from. The latencies had been read from a separate per-block summary the
    document could write freely: a candidate whose samples all sat at 200 ms
    qualified against a 150 ms ceiling because the summary claimed 1 ns, and
    the ratio said nothing because the reference had degraded with it -- which
    is the exact case the absolute ceilings exist to catch.
    """
    label = f"tests[{test['workloadId']}].latencies[{block}].candidate"
    samples = test["latencies"][block - 1]["candidate"]
    if not isinstance(samples, list) or not samples:
        raise InvalidEvidenceError(f"{label} contains no samples.")
    values = sorted(
        _evidence_number(sample, f"{label}[{index}]")
        for index, sample in enumerate(samples)
    )

    measurement: dict[str, Any] = {
        "id": test["workloadId"],
        "family": definition["family"],
    }
    for field, quantile in LATENCY_CEILING_QUANTILES.items():
        measurement[field] = percentile(values, quantile)
    recorded = test["resources"][block - 1]["candidate"]
    for field in CEILING_RESOURCE_FIELDS:
        measurement[field] = recorded.get(field)

    return measurement


def evaluate_absolute_ceilings(
    tests: Sequence[dict[str, Any]],
    contract: dict[str, Any],
    policy: dict[str, Any],
    *,
    block_count: int,
) -> list[dict[str, Any]]:
    """Hold the candidate to the same absolute budgets the historical gate used.

    A ratio says the candidate is no worse than its reference. It cannot say
    either one is acceptable, so a pair that regressed together would qualify.
    These are the budgets that make that impossible.

    Every block is checked, and the worst observation decides. These are
    catastrophe ceilings: a workload that blew its budget once has blown it,
    and reading only the final block would let an early breach be averaged out
    of existence by whichever block happened to be measured last.
    """
    if not tests or block_count < 1:
        raise InvalidEvidenceError(
            "Paired evidence carries no candidate measurements; the absolute "
            "ceilings have nothing to check."
        )

    definitions = {
        definition["id"]: definition for definition in contract["workloads"]
    }
    worst: dict[tuple[str, str], dict[str, Any]] = {}
    for block in range(1, block_count + 1):
        measurements = []
        for test in tests:
            definition = definitions.get(test["workloadId"])
            if definition is None:
                raise InvalidEvidenceError(
                    f"Candidate measurement names workload "
                    f"{test['workloadId']!r}, which the contract does not "
                    "register."
                )
            measurements.append(
                candidate_ceiling_measurement(test, definition, block=block)
            )
        for check in _block_ceiling_checks(measurements, contract, policy):
            key = (check["workloadId"], check["metric"])
            check = {**check, "block": block}
            previous = worst.get(key)
            if previous is None or check["actual"] > previous["actual"]:
                worst[key] = check

    return [worst[key] for key in sorted(worst)]


def validate_execution_order(
    recorded: dict[str, Any] | None,
    policy: dict[str, Any],
    expected_blocks: int,
) -> list[str]:
    """Require the executed order to be the registered one.

    The counterbalancing this comparison rests on is a property of the order
    the run actually followed. Reading the plan back out of the contract would
    prove nothing; this reads what the runner recorded while it ran.
    """
    if not isinstance(recorded, dict):
        raise InvalidEvidenceError(
            "Paired evidence records no execution order; the counterbalancing "
            "the comparison rests on is unproven."
        )

    executed = recorded.get("executedBlockPatterns")
    if not isinstance(executed, list) or not executed:
        raise InvalidEvidenceError("The recorded execution order is empty.")
    if len(executed) != expected_blocks:
        raise InvalidEvidenceError(
            f"The run executed {len(executed)} blocks; the policy registers at "
            f"exactly {expected_blocks}."
        )

    registered = list(policy["executionOrder"]["blockPatterns"])
    unknown = sorted(set(executed) - set(registered))
    if unknown:
        raise InvalidEvidenceError(
            f"The run executed unregistered block pattern(s): {', '.join(unknown)}."
        )
    if recorded.get("blockProfile") != policy["blocks"]["profile"]:
        raise InvalidEvidenceError(
            f"The run measured under profile {recorded.get('blockProfile')!r} "
            f"rather than the registered {policy['blocks']['profile']!r}."
        )

    # The starting side has to change from block to block, which is what makes
    # a warm-up advantage cancel instead of accruing to one provider.
    if policy["executionOrder"]["startingSideAlternatesPerBlock"]:
        starts = [pattern.split("-")[0] for pattern in executed]
        for index in range(1, len(starts)):
            if starts[index] == starts[index - 1]:
                raise InvalidEvidenceError(
                    f"Blocks {index} and {index + 1} both started on side "
                    f"'{starts[index]}'; the policy requires the starting side "
                    "to alternate."
                )

    return executed


PAIRED_EVIDENCE_SCHEMA_VERSION = 2
PAIRED_EVIDENCE_KIND = "paired-performance-evidence"

PAIRED_IDENTITY_FIELDS_REQUIRED = (
    "runId",
    "target",
    "profile",
    "commit",
    "sourceHash",
    "runnerClass",
)


def validate_evidence_envelope(
    evidence: Any,
    policy: dict[str, Any],
) -> dict[str, Any]:
    """Reject anything that is not this run's evidence, before any statistic.

    Every failure here is invalid evidence. That matters more than it reads:
    an unguarded field access raises a plain `KeyError`, the command line maps
    an uncaught error to exit 1, and the attempt recorder classifies exit 1 as
    a regression. Without this gate, a truncated or foreign document convicts
    the provider it never described.
    """
    if not isinstance(evidence, dict):
        raise InvalidEvidenceError("Paired evidence is not an object.")

    if evidence.get("schemaVersion") != PAIRED_EVIDENCE_SCHEMA_VERSION:
        raise InvalidEvidenceError(
            f"Paired evidence declares schemaVersion "
            f"{evidence.get('schemaVersion')!r}, not "
            f"{PAIRED_EVIDENCE_SCHEMA_VERSION}."
        )
    if evidence.get("kind") != PAIRED_EVIDENCE_KIND:
        raise InvalidEvidenceError(
            f"Paired evidence declares kind {evidence.get('kind')!r}, not "
            f"{PAIRED_EVIDENCE_KIND!r}."
        )

    for field in PAIRED_IDENTITY_FIELDS_REQUIRED:
        value = evidence.get(field)
        if not isinstance(value, str) or not value.strip():
            raise InvalidEvidenceError(
                f"Paired evidence carries no usable '{field}'; the verdict "
                "could not be bound to the run that produced it."
            )

    # The profile is what fixes the population floor, the sample cap, and the
    # error budget. Evidence measured under another one was decided against a
    # different contract than the policy registers.
    registered = policy["blocks"]["profile"]
    if evidence["profile"] != registered:
        raise InvalidEvidenceError(
            f"Paired evidence was measured under profile "
            f"{evidence['profile']!r}, not the registered {registered!r}."
        )

    return evidence


def workload_sample_cap(
    profile: dict[str, Any],
    definition: dict[str, Any],
) -> int:
    """Return the population at which this workload is allowed to stop.

    The cap is the workload's own starting population -- its override, or the
    profile's standard or expensive allocation -- multiplied by the registered
    extension limit. A run may only claim it reached the cap at that number.
    """
    field = (
        "expensiveMeasurementSamples"
        if definition.get("cost", "standard") == "expensive"
        else "measurementSamples"
    )
    start = int(definition.get("measurementSamples", profile[field]))

    return start * int(profile["maximumMeasurementSampleMultiplier"])


_SHA256_SHAPE = re.compile(r"[0-9a-f]{64}")
_DIGEST_SHAPE = re.compile(r"[0-9a-f]{64}|[0-9a-f]{40}")


def validate_candidate_ceiling_entry(
    workload: dict[str, Any],
    definition: dict[str, Any],
    *,
    label: str,
) -> None:
    """Bind one candidate entry to the workload the contract registered.

    The absolute budgets are selected per family, and the family was read from
    the evidence itself. Re-declaring a workload into a more generous family
    turned a real ceiling breach into a qualified release: the same 200 ms
    measurement passed as `write` and failed as `concurrency`. The registered
    family is the only one that may choose a budget, so a document that
    disagrees with the contract is not evidence about the provider.

    The metrics are checked here as well. Reaching them through the general
    number helper left a malformed value as exit 1, which the attempt recorder
    reads as a regression.
    """
    declared = workload.get("family")
    if declared != definition["family"]:
        raise InvalidEvidenceError(
            f"{label} declares family {declared!r}; the contract registers it "
            f"as {definition['family']!r}."
        )

    for field in (*LATENCY_CEILING_QUANTILES, *CEILING_RESOURCE_FIELDS):
        value = workload[field]
        # `bool` is a subclass of `int`, so `True` would otherwise measure 1.
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            raise InvalidEvidenceError(
                f"{label} records {field}={value!r}, which is not a measurement."
            )
        if not math.isfinite(value) or value < 0:
            raise InvalidEvidenceError(
                f"{label} records {field}={value!r}, which no measurement "
                "can produce."
            )


def derive_calibration(
    pulses: Sequence[Any],
    indices: Sequence[Any],
    label: str,
    *,
    sample_count: int,
    maximum_interval: int,
) -> list[float]:
    """Rebuild each sample's divisor from the pulses that were measured.

    Carrying the divisor alone proved the arithmetic and not its origin: with
    every raw latency untouched, a freely chosen divisor rescaled a real
    regression into a qualification, because the ratio the pairing decides on
    is formed from the normalized samples. The divisor is therefore derived
    here from the pulse train, under the same invariants the workload report is
    held to at measurement time.
    """
    if len(indices) != sample_count:
        raise InvalidEvidenceError(
            f"{label} assigns {len(indices)} calibration pulses to "
            f"{sample_count} samples."
        )
    measured = [
        _evidence_number(pulse, f"{label}.pulses[{position}]", minimum=0.000001)
        for position, pulse in enumerate(pulses)
    ]
    assigned: list[int] = []
    for position, value in enumerate(indices):
        # `bool` is a subclass of `int`, so `True` would otherwise select pulse
        # one and read as an ordinary assignment.
        if isinstance(value, bool) or not isinstance(value, int) or value < 0:
            raise InvalidEvidenceError(
                f"{label}.pulseIndices[{position}]={value!r} is not a pulse."
            )
        assigned.append(value)

    # Implied by the sequence and coverage rules below -- a train that starts
    # at pulse one either skips backwards later or leaves pulse zero unused.
    # It is stated anyway because it is one of the invariants the report is
    # held to at measurement time, and it names the fault precisely.
    if assigned[0] != 0:
        raise InvalidEvidenceError(
            f"{label} does not start at calibration pulse zero."
        )

    per_pulse: dict[int, int] = {}
    previous = 0
    for position, index in enumerate(assigned):
        # Also implied by the coverage rule, which compares the used set
        # against the recorded pulses in both directions. Kept for the same
        # reason as the starting-pulse rule.
        if index >= len(measured):
            raise InvalidEvidenceError(
                f"{label}.pulseIndices[{position}]={index} names no measured "
                "calibration pulse."
            )
        # A run advances one pulse at a time. Any other step means the sample
        # was attributed to a pulse that did not measure it.
        if index not in (previous, previous + 1):
            raise InvalidEvidenceError(
                f"{label}.pulseIndices[{position}]={index} does not follow "
                f"pulse {previous}."
            )
        per_pulse[index] = per_pulse.get(index, 0) + 1
        previous = index

    if set(per_pulse) != set(range(len(measured))):
        unused = sorted(set(range(len(measured))) - set(per_pulse))
        raise InvalidEvidenceError(
            f"{label} records calibration pulse(s) no sample used: "
            f"{', '.join(str(index) for index in unused)}."
        )
    excessive = sorted(
        index for index, count in per_pulse.items() if count > maximum_interval
    )
    if excessive:
        raise InvalidEvidenceError(
            f"{label} reuses calibration pulse(s) beyond the registered "
            f"interval of {maximum_interval}: "
            f"{', '.join(str(index) for index in excessive)}."
        )

    return [measured[index] for index in assigned]


def validate_sample_populations(
    test: dict[str, Any],
    label: str,
    *,
    maximum_interval: int,
) -> None:
    """Require one measured population per block and side.

    A block carries the calibration-normalized samples the pairing decides on
    and the raw nanosecond samples the absolute ceilings decide on. They are
    two views of the same operations, and nothing said so: a document could
    pair sixteen observations while holding one unrelated observation to the
    ceiling, and it qualified. The runner cannot produce that, and the
    evaluator is a separate trust boundary that has to say so itself.

    The identity is the one the workload report proves at measurement time:
    each normalized sample is its raw latency divided by the calibration pulse
    that measured it.
    """
    for index, (normalized, raw, calibration, pulses, assignment) in enumerate(
        zip(
            test["blocks"],
            test["latencies"],
            test["calibrations"],
            test["calibrationPulses"],
            test["calibrationPulseIndices"],
        )
    ):
        for side in SIDES:
            populations = {
                "blocks": normalized[side],
                "latencies": raw[side],
                "calibrations": calibration[side],
            }
            sizes = {name: len(values) for name, values in populations.items()}
            if len(set(sizes.values())) != 1:
                measured = ", ".join(
                    f"{name}={size}" for name, size in sorted(sizes.items())
                )
                raise InvalidEvidenceError(
                    f"{label}[{index}].{side} measures different populations: "
                    f"{measured}."
                )
            derived = derive_calibration(
                pulses[side],
                assignment[side],
                f"{label}[{index}].{side}",
                sample_count=sizes["blocks"],
                maximum_interval=maximum_interval,
            )
            for position in range(sizes["blocks"]):
                divisor = _evidence_number(
                    populations["calibrations"][position],
                    f"{label}.calibrations[{index}].{side}[{position}]",
                    minimum=0.000001,
                )
                if not close_enough(divisor, derived[position]):
                    raise InvalidEvidenceError(
                        f"{label}.calibrations[{index}].{side}[{position}]="
                        f"{divisor} is not the {derived[position]} its "
                        "calibration pulse measured."
                    )
                latency = _evidence_number(
                    populations["latencies"][position],
                    f"{label}.latencies[{index}].{side}[{position}]",
                    minimum=0.000001,
                )
                observed = _evidence_number(
                    populations["blocks"][position],
                    f"{label}.blocks[{index}].{side}[{position}]",
                    minimum=0.000001,
                )
                if not close_enough(observed, latency / derived[position]):
                    raise InvalidEvidenceError(
                        f"{label}.blocks[{index}].{side}[{position}]="
                        f"{observed} does not follow from its latency "
                        f"{latency} and calibration {derived[position]}."
                    )


def validate_paired_evidence(
    evidence: dict[str, Any],
    contract: dict[str, Any],
    policy: dict[str, Any],
    *,
    contract_digest: str,
) -> dict[str, dict[str, Any]]:
    """Hold assembled evidence to the contract before any statistic runs.

    The assembler validates the raw reports it reads, but `evaluate-paired` is
    a separate entry point and a separate trust boundary: it is handed a
    finished document. Trusting that document meant a truncated, edited, or
    otherwise produced `paired-evidence.json` could qualify a release, and
    nothing downstream recomputes the statistics from the raw reports.
    """
    definitions = {
        workload["id"]: workload for workload in contract["workloads"]
    }
    tests = evidence["tests"]
    calibration_interval = int(
        contract["profiles"][policy["blocks"]["profile"]][
            "calibrationIntervalSamples"
        ]
    )

    # Types before values, for the test structures too. Building a set from
    # `workloadId` before checking it raised a plain TypeError on a list, and a
    # null block raised one on comparison -- both leave the command line as
    # exit 1, which the attempt recorder reads as a regression. Structurally
    # broken evidence must never look like a verdict about the provider.
    observed: list[str] = []
    for position, test in enumerate(tests):
        if not isinstance(test, dict):
            raise InvalidEvidenceError(f"tests[{position}] is not an object.")
        identifier = test.get("workloadId")
        if not isinstance(identifier, str) or not identifier.strip():
            raise InvalidEvidenceError(
                f"tests[{position}] records no usable workloadId."
            )
        observed.append(identifier)

        for field in (
            "blocks",
            "latencies",
            "calibrations",
            "calibrationPulses",
            "calibrationPulseIndices",
            "resources",
            "terminations",
        ):
            entries = test.get(field)
            if not isinstance(entries, list):
                raise InvalidEvidenceError(
                    f"tests[{identifier}].{field} is not a list."
                )
        for index, measured in enumerate(test["blocks"]):
            if not isinstance(measured, dict) or set(measured) != set(SIDES):
                raise InvalidEvidenceError(
                    f"tests[{identifier}].blocks[{index}] does not carry both "
                    "sides."
                )
            for side in SIDES:
                samples = measured[side]
                if not isinstance(samples, list) or not samples:
                    raise InvalidEvidenceError(
                        f"tests[{identifier}].blocks[{index}].{side} carries "
                        "no samples."
                    )
        for field in (
            "latencies",
            "calibrations",
            "calibrationPulses",
            "calibrationPulseIndices",
        ):
            for index, measured in enumerate(test[field]):
                if not isinstance(measured, dict) or set(measured) != set(SIDES):
                    raise InvalidEvidenceError(
                        f"tests[{identifier}].{field}[{index}] does not carry "
                        "both sides."
                    )
                for side in SIDES:
                    samples = measured[side]
                    if not isinstance(samples, list) or not samples:
                        raise InvalidEvidenceError(
                            f"tests[{identifier}].{field}[{index}].{side} "
                            "carries no samples."
                        )
        validate_sample_populations(
            test,
            f"tests[{identifier}]",
            maximum_interval=calibration_interval,
        )
        for index, resource in enumerate(test["resources"]):
            if not isinstance(resource, dict) or set(resource) != set(SIDES):
                raise InvalidEvidenceError(
                    f"tests[{identifier}].resources[{index}] does not carry "
                    "both sides."
                )
            for side in SIDES:
                measurements = resource[side]
                if not isinstance(measurements, dict):
                    raise InvalidEvidenceError(
                        f"tests[{identifier}].resources[{index}].{side} is not "
                        "an object."
                    )
                for family in policy["resourceFamilies"]:
                    metric = family["metric"]
                    if metric not in measurements:
                        raise InvalidEvidenceError(
                            f"tests[{identifier}].resources[{index}].{side} "
                            f"records no '{metric}'."
                        )

    duplicates = sorted({name for name in observed if observed.count(name) > 1})
    if duplicates:
        raise InvalidEvidenceError(
            f"Paired evidence reports workload(s) twice: {', '.join(duplicates)}."
        )
    unknown = sorted(set(observed) - set(definitions))
    if unknown:
        raise InvalidEvidenceError(
            f"Paired evidence reports workload(s) the contract does not "
            f"register: {', '.join(unknown)}."
        )
    missing = sorted(set(definitions) - set(observed))
    if missing:
        raise InvalidEvidenceError(
            f"Paired evidence is missing registered workload(s): "
            f"{', '.join(missing)}."
        )

    # The block count is the spine every parallel structure is measured
    # against. Treating it as optional made every count check below vacuous:
    # a document that simply omitted it qualified.
    block_count = evidence.get("blockCount")
    if isinstance(block_count, bool) or not isinstance(block_count, int):
        raise InvalidEvidenceError(
            f"Paired evidence declares blockCount {block_count!r}, which is "
            "not a count."
        )
    registered = int(policy["blocks"]["completeBlocks"])
    if block_count != registered:
        raise InvalidEvidenceError(
            f"Paired evidence declares {block_count} blocks; the policy "
            f"registers exactly {registered}."
        )

    for test in tests:
        label = f"tests[{test['workloadId']}]"
        for field in (
            "blocks",
            "latencies",
            "calibrations",
            "calibrationPulses",
            "calibrationPulseIndices",
            "resources",
            "terminations",
        ):
            parallel = test.get(field)
            observed_length = len(parallel) if isinstance(parallel, list) else 0
            if observed_length != block_count:
                raise InvalidEvidenceError(
                    f"{label}.{field} covers {observed_length} of "
                    f"{block_count} blocks."
                )

    order = evidence.get("executionOrder")
    if not isinstance(order, dict):
        raise InvalidEvidenceError(
            "Paired evidence records no execution order; the counterbalancing "
            "the comparison rests on is unproven."
        )
    executed = order.get("executedBlockPatterns")
    if not isinstance(executed, list) or len(executed) != block_count:
        observed_length = len(executed) if isinstance(executed, list) else 0
        raise InvalidEvidenceError(
            f"The recorded execution order covers {observed_length} blocks "
            f"while the evidence declares {block_count}."
        )

    # The absolute ceilings read the candidate measurements. A document that
    # carried one block of them satisfied every other check while holding the
    # candidate to a fraction of the run.
    candidate_blocks = evidence.get("candidateWorkloads")
    if not isinstance(candidate_blocks, list):
        raise InvalidEvidenceError(
            "Paired evidence carries no candidate workload measurements."
        )
    # Types before values, everywhere. Reading a field first and checking it
    # afterwards raised a plain TypeError or AttributeError, which leaves the
    # command line as exit 1 -- and the attempt recorder reads exit 1 as a
    # regression. Structurally broken evidence must never look like a verdict
    # about the provider.
    observed_ids: list[int] = []
    for position, entry in enumerate(candidate_blocks):
        if not isinstance(entry, dict):
            raise InvalidEvidenceError(
                f"candidateWorkloads[{position}] is not an object."
            )
        identifier = entry.get("block")
        # `bool` is a subclass of `int` in Python, so `True` would otherwise
        # pass as block 1 and sort alongside the real identifiers.
        if isinstance(identifier, bool) or not isinstance(identifier, int):
            raise InvalidEvidenceError(
                f"candidateWorkloads[{position}] declares block "
                f"{identifier!r}, which is not a block identifier."
            )
        if not 1 <= identifier <= block_count:
            raise InvalidEvidenceError(
                f"candidateWorkloads[{position}] declares block {identifier}, "
                f"outside the {block_count} blocks the evidence records."
            )
        observed_ids.append(identifier)

    if sorted(observed_ids) != list(range(1, block_count + 1)):
        raise InvalidEvidenceError(
            f"Candidate measurements cover blocks {sorted(observed_ids)}; the "
            f"evidence declares {block_count}."
        )

    for entry in candidate_blocks:
        workloads = entry.get("workloads")
        if not isinstance(workloads, list) or not workloads:
            raise InvalidEvidenceError(
                f"Candidate block {entry['block']} carries no workloads."
            )
        measured = []
        for position, workload in enumerate(workloads):
            if not isinstance(workload, dict):
                raise InvalidEvidenceError(
                    f"Candidate block {entry['block']} workload[{position}] "
                    "is not an object."
                )
            identifier = workload.get("id")
            if not isinstance(identifier, str) or not identifier.strip():
                raise InvalidEvidenceError(
                    f"Candidate block {entry['block']} workload[{position}] "
                    "records no usable identifier."
                )
            measured.append(identifier)
            # Exactly the projection, in both directions. A surplus field is a
            # second, unchecked record of the same measurement; a missing one
            # would reach the comparison below as a `None` and leave the
            # command line as exit 1, which the attempt recorder reads as a
            # regression.
            if set(workload) != set(CANDIDATE_PROJECTION_FIELDS):
                surplus = sorted(set(workload) - set(CANDIDATE_PROJECTION_FIELDS))
                missing = sorted(set(CANDIDATE_PROJECTION_FIELDS) - set(workload))
                raise InvalidEvidenceError(
                    f"Candidate block {entry['block']} workload "
                    f"'{identifier}' is not the audit projection: "
                    f"surplus [{', '.join(surplus)}], "
                    f"missing [{', '.join(missing)}]."
                )
            definition = definitions.get(identifier)
            if definition is None:
                # The completeness comparison below would catch this too, but
                # only after the family check has already read the entry.
                continue
            validate_candidate_ceiling_entry(
                workload,
                definition,
                label=f"Candidate block {entry['block']} workload '{identifier}'",
            )
        if sorted(measured) != sorted(definitions):
            raise InvalidEvidenceError(
                f"Candidate block {entry['block']} measured "
                f"{len(measured)} workloads rather than the registered "
                f"{len(definitions)}."
            )

    # `candidateWorkloads` is an audit projection, not a second source of
    # truth: the decisions read `tests`. It is checked against what those
    # measurements produce, so the document cannot carry a summary that
    # disagrees with the numbers it summarizes.
    tests_by_id = {test["workloadId"]: test for test in tests}
    for entry in candidate_blocks:
        for workload in entry["workloads"]:
            test = tests_by_id[workload["id"]]
            measurement = candidate_ceiling_measurement(
                test,
                definitions[workload["id"]],
                block=entry["block"],
            )
            for field in (*LATENCY_CEILING_QUANTILES, *CEILING_RESOURCE_FIELDS):
                projected = workload.get(field)
                measured_value = measurement[field]
                if not close_enough(float(projected), float(measured_value)):
                    raise InvalidEvidenceError(
                        f"Candidate block {entry['block']} workload "
                        f"'{workload['id']}' reports {field}={projected!r} "
                        f"against the {measured_value!r} its measurement "
                        "produces."
                    )

    # The pairing removes the machine only if both sides ran on one. Dropping
    # the record made that claim unfalsifiable.
    environments = evidence.get("environments")
    if not isinstance(environments, dict) or set(environments) != set(SIDES):
        raise InvalidEvidenceError(
            "Paired evidence records no environment for both sides."
        )
    validate_paired_environment(environments["reference"], environments["candidate"])

    # Provenance: which revisions were compared, under which driver, against
    # which contract. Without these the numbers describe nothing in particular.
    for field in ("candidateCommit", "referenceCommit",
                  "benchmarkDriverSourceHash", "contractDigest"):
        value = evidence.get(field)
        if not isinstance(value, str) or not value.strip():
            raise InvalidEvidenceError(
                f"Paired evidence carries no usable '{field}'."
            )
    # Real identifiers, not merely non-empty text. A reference revision that
    # reads `not-a-commit` describes no revision at all, and no later gate
    # re-checks it: the release comparison protects the candidate commit and
    # takes the reference on trust.
    for field in ("commit", "candidateCommit", "referenceCommit"):
        try:
            required_commit(evidence, field, "paired evidence")
        except PerformanceEvidenceError as error:
            raise InvalidEvidenceError(str(error)) from error
    try:
        required_sha256(evidence, "sourceHash", "paired evidence")
    except PerformanceEvidenceError as error:
        raise InvalidEvidenceError(str(error)) from error

    if not _SHA256_SHAPE.fullmatch(evidence["contractDigest"]):
        raise InvalidEvidenceError(
            f"Paired evidence records 'contractDigest' as "
            f"{evidence['contractDigest']!r}, which is not a SHA-256 digest."
        )
    # The driver hash is a Git tree identifier for a clean checkout and a
    # SHA-256 over the working tree otherwise; both shapes are what the runner
    # actually emits.
    if not _DIGEST_SHAPE.fullmatch(evidence["benchmarkDriverSourceHash"]):
        raise InvalidEvidenceError(
            f"Paired evidence records 'benchmarkDriverSourceHash' as "
            f"{evidence['benchmarkDriverSourceHash']!r}, which is neither a "
            "Git tree identifier nor a SHA-256 digest."
        )
    if evidence["contractDigest"] != contract_digest:
        # Requiring the field without binding it let evidence claim it belonged
        # to a contract nobody evaluated it against -- so a budget, a cap, or a
        # workload matrix from somewhere else could carry the verdict.
        raise InvalidEvidenceError(
            f"Paired evidence was measured against contract "
            f"{evidence['contractDigest']}, not the {contract_digest} this "
            "evaluation loaded."
        )
    if evidence["commit"] != evidence["candidateCommit"]:
        raise InvalidEvidenceError(
            f"Paired evidence records commit {evidence['commit']} and "
            f"candidate commit {evidence['candidateCommit']}; the release is "
            "about one revision."
        )

    return definitions


def validate_terminations(
    test: dict[str, Any],
    label: str,
    *,
    definition: dict[str, Any] | None = None,
    profile: dict[str, Any] | None = None,
) -> str:
    """Return the worst termination the runner reported for one workload.

    The runner already decided whether each side converged or stopped at its
    sample cap. Storing that verdict and never reading it is how a run that ran
    out of samples on both sides reached a clean qualification -- and reading
    only its shape is how a state the runner cannot produce reached a verdict.
    """
    blocks = test["blocks"]
    terminations = test.get("terminations")
    if not isinstance(terminations, list) or len(terminations) != len(blocks):
        observed = 0 if not isinstance(terminations, list) else len(terminations)
        raise InvalidEvidenceError(
            f"{label} carries {observed} termination records for "
            f"{len(blocks)} blocks."
        )

    worst = PRECISION_REACHED
    for index, (record, measured) in enumerate(zip(terminations, blocks)):
        if not isinstance(record, dict) or set(record) != set(SIDES):
            raise InvalidEvidenceError(
                f"{label}.terminations[{index}] does not cover both sides."
            )
        for side in SIDES:
            entry = record[side]
            if not isinstance(entry, dict):
                raise InvalidEvidenceError(
                    f"{label}.terminations[{index}].{side} is not an object."
                )
            reason = entry.get("terminationReason")
            if reason not in TERMINATION_REASONS:
                raise InvalidEvidenceError(
                    f"{label}.terminations[{index}].{side} reports termination "
                    f"{reason!r}, which is not a registered reason."
                )
            count = entry.get("sampleCount")
            if count != len(measured[side]):
                raise InvalidEvidenceError(
                    f"{label}.terminations[{index}].{side} reports {count} "
                    f"samples while the block carries {len(measured[side])}."
                )
            duration_reached = entry.get("minimumDurationReached")
            if not isinstance(duration_reached, bool):
                raise InvalidEvidenceError(
                    f"{label}.terminations[{index}].{side} records no "
                    "minimumDurationReached verdict."
                )

            # The two reasons carry opposite obligations, and neither is
            # checkable from the reason alone. A run stops on precision only
            # once it has also met the duration floor; a run may claim the cap
            # only at the population the cap actually permits.
            if reason == PRECISION_REACHED and not duration_reached:
                raise InvalidEvidenceError(
                    f"{label}.terminations[{index}].{side} reports "
                    f"{PRECISION_REACHED!r} without reaching the minimum "
                    "measurement duration, which the runner cannot produce."
                )
            if reason == SAMPLE_CAP_REACHED:
                if definition is not None and profile is not None:
                    cap = workload_sample_cap(profile, definition)
                    if count != cap:
                        raise InvalidEvidenceError(
                            f"{label}.terminations[{index}].{side} reports "
                            f"{SAMPLE_CAP_REACHED!r} at {count} samples; this "
                            f"workload\'s cap is {cap}."
                        )
                worst = SAMPLE_CAP_REACHED

    return worst


def evaluate_paired_comparison(
    evidence: dict[str, Any],
    contract: dict[str, Any],
    *,
    contract_digest: str,
) -> dict[str, Any]:
    """Evaluate a complete paired run and derive its qualification state.

    The result is one state for the whole comparison. A confirmed regression,
    absolute-budget failure, resource regression, or soak failure rejects the
    candidate. Statistical overlap is retained as uncertainty rather than
    triggering a fresh sample chosen after observing the result.
    """
    policy = validate_paired_policy(contract)
    validate_evidence_envelope(evidence, policy)
    blocks_policy = policy["blocks"]
    complete_blocks = int(blocks_policy["completeBlocks"])
    # The profile the blocks measured under owns what counts as a valid
    # population; the evaluator applies the same floor rather than one of its
    # own, so a run cannot be judged against a bar the runner never used.
    block_profile = contract.get("profiles", {}).get(blocks_policy["profile"], {})
    minimum_samples = int(block_profile.get("minimumValidSamples", 2))

    tests = evidence.get("tests")
    if not isinstance(tests, list) or not tests:
        raise InvalidEvidenceError("Paired evidence declares no tests.")
    for index, test in enumerate(tests):
        if not isinstance(test, dict) or "workloadId" not in test:
            raise InvalidEvidenceError(f"tests[{index}].workloadId is required.")
        blocks = test.get("blocks")
        if not isinstance(blocks, list):
            raise InvalidEvidenceError(f"tests[{index}].blocks is required.")
        if len(blocks) != complete_blocks:
            raise InvalidEvidenceError(
                f"tests[{index}] contributed {len(blocks)} complete blocks; the "
                f"policy registers exactly {complete_blocks}."
            )

    # Per-block termination remains part of the audit record. Reaching the
    # registered sample cap is a normal fixed-population stop, not permission to
    # choose another population after observing the statistical result.
    definitions = validate_paired_evidence(
        evidence, contract, policy, contract_digest=contract_digest
    )
    capped = frozenset(
        test["workloadId"]
        for test in tests
        if validate_terminations(
            test,
            f"tests[{test['workloadId']}]",
            definition=definitions[test["workloadId"]],
            profile=block_profile,
        )
        == SAMPLE_CAP_REACHED
    )

    primary_metric = policy["primaryFamily"]["metric"]
    families = [primary_metric]
    families += [family["metric"] for family in policy["secondaryFamilies"]]

    primary_endpoint = evaluate_primary_endpoint(
        tests,
        metric=primary_metric,
        budget=float(policy["practicalBudgets"][primary_metric]),
        policy=policy,
        minimum_samples=minimum_samples,
    )
    target_role = policy["targetRoles"][evidence["target"]]
    primary_endpoint["targetRole"] = target_role

    results: list[dict[str, Any]] = []
    for metric in families:
        results.extend(
            evaluate_observational_family(
                tests,
                metric=metric,
                budget=float(policy["practicalBudgets"][metric]),
                policy=policy,
                minimum_samples=minimum_samples,
            )
        )

    resource_results = evaluate_resource_families(tests, policy)
    ceilings = evaluate_absolute_ceilings(
        evidence["tests"], contract, policy, block_count=evidence["blockCount"]
    )
    if len(ceilings) and len({check["block"] for check in ceilings}) < 1:
        raise InvalidEvidenceError("Candidate ceilings carry no block identity.")

    # Sustained use is a different failure mode from per-operation latency: a
    # leak shows up over thousands of iterations and never inside a block. The
    # policy requires the report, so its absence is invalid evidence rather
    # than a silently skipped check.
    soak_policy = policy["soak"]
    soak_report = evidence.get("soak")
    if soak_report is None:
        raise InvalidEvidenceError(
            "The paired policy requires a candidate soak report; the evidence "
            "carries none."
        )
    # The run has to have followed the plan the policy registers. An order
    # recorded but never compared would document a deviation instead of
    # refusing it.
    validate_execution_order(evidence.get("executionOrder"), policy, complete_blocks)

    soak_scenarios = validate_soak_report(
        soak_report,
        contract,
        run_id=evidence["runId"],
        target=evidence["target"],
        profile=blocks_policy["profile"],
    )

    resource_states = {item["state"] for item in resource_results}
    if "regression" in resource_states or not all(
        check["passed"] for check in ceilings
    ):
        qualification = "regression"
    elif (
        target_role == "required"
        and primary_endpoint["state"] == "insufficient-sensitivity"
    ):
        qualification = "measurement-inconclusive"
    else:
        qualification = "pending-run-wide-adjustment"

    return {
        "schemaVersion": 5,
        "kind": "paired-performance-evaluation",
        # The identity travels from the evidence onto the verdict so a receipt
        # can prove it owns this evaluation. Without it the attempt recorder
        # has nothing to bind and the run produces no selectable result.
        "target": evidence["target"],
        "profile": evidence["profile"],
        "runId": evidence["runId"],
        "commit": evidence["commit"],
        "sourceHash": evidence["sourceHash"],
        "runnerClass": evidence["runnerClass"],
        "contractDigest": evidence["contractDigest"],
        "referenceCommit": evidence["referenceCommit"],
        "qualification": qualification,
        "success": qualification == "pending-run-wide-adjustment",
        "families": families,
        "primaryEndpoint": primary_endpoint,
        "dispersionObservation": {
            "schemaVersion": 1,
            "kind": "paired-dispersion-observation",
            "target": evidence["target"],
            "runId": evidence["runId"],
            "commit": evidence["commit"],
            "sourceHash": evidence["sourceHash"],
            "runnerClass": evidence["runnerClass"],
            "contractDigest": evidence["contractDigest"],
            "referenceCommit": evidence["referenceCommit"],
            "metric": primary_metric,
            "aggregation": policy["primaryFamily"]["aggregation"],
            "realizedLogRatioStandardDeviation": primary_endpoint[
                "logRatioStandardDeviation"
            ],
            "registeredUpperBound": policy["sensitivity"][
                "maximumLogRatioStandardDeviation"
            ],
            "state": (
                "drift"
                if primary_endpoint["state"] == "insufficient-sensitivity"
                else "stable"
            ),
        },
        "results": results,
        "cappedWorkloads": sorted(capped),
        "uncertainResults": sum(
            item["state"] == "observed-overlap" for item in results
        ),
        "sensitivity": {
            "method": policy["sensitivity"]["method"],
            "familyCase": policy["sensitivity"]["familyCase"],
            "minimumPower": policy["sensitivity"]["minimumPower"],
            "maximumLogRatioStandardDeviation": policy["sensitivity"][
                "maximumLogRatioStandardDeviation"
            ],
            "characterization": policy["sensitivity"]["characterization"],
            "minimumDetectableRatios": {
                primary_metric: float(policy["practicalBudgets"][primary_metric])
                * float(
                    policy["sensitivity"]["minimumDetectableBudgetMultiple"]
                )
            },
        },
        "resourceResults": resource_results,
        "absoluteCeilings": ceilings,
        "soakScenarios": soak_scenarios,
        "soakAppliesTo": soak_policy["appliesTo"],
    }


def evaluate_scorecard_qualification(
    evaluations: Sequence[dict[str, Any]],
    contract: dict[str, Any],
    *,
    contract_digest: str,
) -> dict[str, Any]:
    """Apply one run-wide decision to all selected target evaluations.

    Target jobs can validate measurement quality and hard local gates, but no
    target can adjust its p-value without seeing the other required
    targets. This finalizer is therefore the only place that may convert a
    primary latency endpoint into a statistical regression verdict.
    """
    policy = validate_paired_policy(contract)
    expected_targets = set(contract["requiredTargets"])
    by_target: dict[str, dict[str, Any]] = {}
    for evaluation in evaluations:
        if evaluation.get("schemaVersion") != 5:
            raise InvalidEvidenceError(
                "Scorecard target evaluation has an unsupported schema version."
            )
        if evaluation.get("kind") != "paired-performance-evaluation":
            raise InvalidEvidenceError(
                "Scorecard input is not a paired performance evaluation."
            )
        target = evaluation.get("target")
        if target in by_target:
            raise InvalidEvidenceError(
                f"Scorecard carries target '{target}' more than once."
            )
        by_target[target] = evaluation

    observed_targets = set(by_target)
    if observed_targets != expected_targets:
        missing = sorted(expected_targets - observed_targets)
        unexpected = sorted(observed_targets - expected_targets)
        raise InvalidEvidenceError(
            "Scorecard target set is incomplete: "
            f"missing={missing}, unexpected={unexpected}."
        )

    identity_fields = (
        "commit",
        "sourceHash",
        "runnerClass",
        "contractDigest",
        "referenceCommit",
    )
    identities: dict[str, Any] = {}
    for field in identity_fields:
        values = {evaluation.get(field) for evaluation in evaluations}
        if len(values) != 1:
            raise InvalidEvidenceError(
                f"Scorecard target evaluations disagree on {field}."
            )
        identities[field] = next(iter(values))
    if identities["contractDigest"] != contract_digest:
        raise InvalidEvidenceError(
            "Scorecard target evaluations were produced under another contract."
        )

    required: list[tuple[str, dict[str, Any]]] = []
    endpoint_results: list[dict[str, Any]] = []
    for target in sorted(by_target):
        evaluation = by_target[target]
        if evaluation.get("qualification") != "pending-run-wide-adjustment":
            raise InvalidEvidenceError(
                f"Target '{target}' is not eligible for run-wide adjustment."
            )
        endpoint = evaluation.get("primaryEndpoint")
        if not isinstance(endpoint, dict):
            raise InvalidEvidenceError(
                f"Target '{target}' carries no primary endpoint."
            )
        role = policy["targetRoles"][target]
        result = dict(endpoint)
        result["target"] = target
        result["targetRole"] = role
        if role == "required":
            if endpoint.get("state") != "pending-run-wide-adjustment":
                raise MeasurementQualityError(
                    f"Required target '{target}' has insufficient sensitivity."
                )
            required.append((target, result))
        else:
            result["runWideRejected"] = None
            result["state"] = "observational"
        endpoint_results.append(result)

    rejected = holm_rejections(
        [float(result["pValue"]) for _, result in required],
        float(policy["multipleComparison"]["familyWiseErrorRate"]),
    )
    for (_, result), is_rejected in zip(required, rejected):
        result["runWideRejected"] = is_rejected
        budget = float(result["budget"])
        above_budget = float(result["lowerBound"]) > budget and not close_enough(
            float(result["lowerBound"]), budget
        )
        result["state"] = (
            "regression" if is_rejected and above_budget else "qualified"
        )

    qualification = (
        "regression"
        if any(result["state"] == "regression" for _, result in required)
        else "qualified"
    )

    return {
        "schemaVersion": 1,
        "kind": "paired-performance-scorecard-qualification",
        "qualification": qualification,
        "success": qualification == "qualified",
        **identities,
        "multipleComparison": {
            "scope": policy["multipleComparison"]["scope"],
            "procedure": policy["multipleComparison"]["procedure"],
            "familyWiseErrorRate": policy["multipleComparison"][
                "familyWiseErrorRate"
            ],
            "requiredTargetCount": len(required),
        },
        "targets": endpoint_results,
    }


_BLOCK_FILE = re.compile(
    r"^block-(?P<block>\d+)-(?P<side>reference|candidate)\.json$"
)


def _read_side(path: Path) -> dict[str, Any]:
    """Read one side of one block, or fail with the file that was unusable."""
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise InvalidEvidenceError(f"{path.name} is unreadable: {error}") from error


def assemble_evidence(
    blocks_dir: Path,
    *,
    contract: dict[str, Any],
    target: str,
    run_id: str,
    candidate_commit: str,
    reference_commit: str,
    driver_source_hash: str,
    contract_digest: str,
    profile: str,
    source_hash: str,
    runner_class: str,
    execution_order: dict[str, Any] | None = None,
    soak_report: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Fold per-block, per-side measurements into one paired evidence document.

    Each measurement file holds one side of one block. A workload is only
    admitted when every block measured it on both sides: a workload present on
    one side alone has no ratio, and silently dropping the other side would
    turn missing evidence into an apparently clean result.

    Assembly is also where the two sides are proven comparable. The environment
    and the benchmark driver are checked here rather than at evaluation time,
    because evidence that was never comparable should not reach a statistical
    procedure that would happily produce a confident verdict from it.
    """
    measurements: dict[int, dict[str, dict[str, Any]]] = {}
    environments: dict[str, dict[str, Any]] = {}
    identities: dict[str, dict[str, Any]] = {}
    candidate_workloads: dict[int, list[dict[str, Any]]] = {}

    for path in sorted(blocks_dir.iterdir()):
        matched = _BLOCK_FILE.match(path.name)
        if not matched:
            continue
        payload = _read_side(path)
        block = int(matched.group("block"))
        side = matched.group("side")

        environment = payload.get("environment")
        if not isinstance(environment, dict):
            raise InvalidEvidenceError(f"{path.name} records no environment.")
        # Every block is compared against the first one for this side. Keeping
        # only the first would let a runner that changed under the run -- a
        # different engine build, a rescheduled container, a different
        # processor after a restart -- go unrecorded and unrejected.
        established = environments.setdefault(side, environment)
        if established != environment:
            differing = sorted(
                field
                for field in set(established) | set(environment)
                if established.get(field) != environment.get(field)
            )
            raise InvalidEvidenceError(
                f"{path.name} ran under a different environment than the first "
                f"{side} block for field(s): {', '.join(differing)}."
            )

        # Each side records the benchmark driver tree and contract it was built
        # from. Publishing the reference side from its own worktree would
        # change these, which is the failure the comparison must never absorb
        # as a provider result.
        identity_path = path.with_suffix(".identity.json")
        if not identity_path.is_file():
            raise InvalidEvidenceError(
                f"{path.name} has no companion {identity_path.name}; the "
                "benchmark driver identity of this side is unproven."
            )
        identity = _read_side(identity_path)
        established_identity = identities.setdefault(side, identity)
        if established_identity != identity:
            raise InvalidEvidenceError(
                f"{identity_path.name} records a different benchmark driver or "
                f"contract than the first {side} block."
            )

        # Every raw report goes through the canonical workload contract before
        # a single number is taken from it. Checking only the fields this
        # module happens to read let a foreign document, an impossible
        # termination, and a statistic that does not follow from its own
        # samples all reach the comparison.
        try:
            validate_workload_report(
                payload,
                contract,
                run_id=run_id,
                target=target,
                profile=profile,
            )
        except InvalidEvidenceError:
            raise
        except PerformanceEvidenceError as error:
            # The canonical validator speaks the general error domain, which
            # the command line maps to exit 1 and the attempt recorder reads
            # as a regression. A malformed report says nothing about the
            # provider, so it leaves as invalid evidence.
            raise InvalidEvidenceError(f"{path.name}: {error}") from error

        entries = payload.get("workloads")
        if not isinstance(entries, list) or not entries:
            raise InvalidEvidenceError(f"{path.name} carries no workloads.")
        if side == "candidate":
            candidate_workloads[block] = [
                candidate_audit_projection(workload) for workload in entries
            ]
        for workload in entries:
            identifier = workload.get("id")
            samples = workload.get("normalizedSamples")
            if not identifier or not isinstance(samples, list) or not samples:
                raise InvalidEvidenceError(
                    f"{path.name} carries no usable samples for "
                    f"{identifier or 'an unnamed workload'}."
                )
            recorded = workload.get("sampleCount")
            if recorded != len(samples):
                raise InvalidEvidenceError(
                    f"{path.name} reports {recorded} samples for {identifier} "
                    f"but carries {len(samples)}."
                )
            reason = workload.get("terminationReason")
            if reason not in TERMINATION_REASONS:
                raise InvalidEvidenceError(
                    f"{path.name} reports termination {reason!r} for "
                    f"{identifier}, which is not a registered reason."
                )
            if not isinstance(workload.get("minimumDurationReached"), bool):
                raise InvalidEvidenceError(
                    f"{path.name} records no minimumDurationReached verdict "
                    f"for {identifier}."
                )
            latencies = workload.get("samplesNanoseconds")
            if not isinstance(latencies, list) or not latencies:
                raise InvalidEvidenceError(
                    f"{path.name} carries no latency samples for {identifier}."
                )
            calibrations = workload.get("calibrationNanoseconds")
            if not isinstance(calibrations, list) or not calibrations:
                raise InvalidEvidenceError(
                    f"{path.name} carries no calibration samples for "
                    f"{identifier}."
                )
            pulses = workload.get("calibrationPulseNanoseconds")
            pulse_indices = workload.get("calibrationPulseIndices")
            if not isinstance(pulses, list) or not pulses:
                raise InvalidEvidenceError(
                    f"{path.name} carries no calibration pulses for "
                    f"{identifier}."
                )
            if not isinstance(pulse_indices, list) or not pulse_indices:
                raise InvalidEvidenceError(
                    f"{path.name} carries no calibration pulse assignment for "
                    f"{identifier}."
                )
            measurements.setdefault(block, {}).setdefault(identifier, {})[side] = {
                "samples": samples,
                # The normalized samples are ratios against the calibration
                # pulse, so they cannot be held to a budget expressed in
                # nanoseconds. The absolute ceilings read these instead, and
                # they are the same measurement the ratio decision uses rather
                # than a second, independently writable summary.
                "latencySamples": latencies,
                # The divisor that turns one into the other, and where that
                # divisor came from. Carrying only the divisor proved the
                # arithmetic and not its origin: a document could rescale a
                # real regression into a qualification by choosing a divisor,
                # leaving every raw latency untouched.
                "calibrationSamples": calibrations,
                "calibrationPulses": pulses,
                "calibrationPulseIndices": pulse_indices,
                "family": workload.get("family"),
                # The runner's own quality verdict travels with the numbers.
                # Without it the evaluator cannot tell a population that
                # converged from one that stopped at the sample cap.
                "sampleCount": recorded,
                "terminationReason": reason,
                "minimumDurationReached": workload.get("minimumDurationReached"),
                "resources": {
                    metric: workload.get(metric)
                    for metric in (
                        "allocatedBytesPerOperation",
                        "gen2CollectionsPer1000",
                    )
                },
            }

    if not measurements:
        raise InvalidEvidenceError(f"No paired block measurements in {blocks_dir}.")
    for side in SIDES:
        if side not in environments:
            raise InvalidEvidenceError(f"No {side} measurements in {blocks_dir}.")

    validate_paired_environment(environments["reference"], environments["candidate"])
    for side in SIDES:
        for field, expected in (
            ("benchmarkDriverSourceHash", driver_source_hash),
            ("contractDigest", contract_digest),
        ):
            if identities[side].get(field) != expected:
                raise InvalidEvidenceError(
                    f"The {side} side records {field} "
                    f"{identities[side].get(field)!r}, which is not the "
                    f"orchestrated {expected!r}."
                )
    validate_paired_benchmark_driver(identities["reference"], identities["candidate"])

    block_ids = sorted(measurements)

    # Completeness is decided over the union of every block rather than over
    # the first one. Reading the first block alone would let a workload that
    # only ever ran in later blocks pass unnoticed, which is the same silent
    # gap the both-sides rule exists to close.
    observed: set[str] = set()
    for block in block_ids:
        observed |= set(measurements[block])

    tests: list[dict[str, Any]] = []
    for identifier in sorted(observed):
        blocks: list[dict[str, list[float]]] = []
        latencies: list[dict[str, list[float]]] = []
        calibrations: list[dict[str, list[float]]] = []
        pulses: list[dict[str, list[float]]] = []
        pulse_indices: list[dict[str, list[int]]] = []
        resources: list[dict[str, dict[str, Any]]] = []
        terminations: list[dict[str, dict[str, Any]]] = []
        for block in block_ids:
            sides = measurements[block].get(identifier, {})
            if set(sides) != set(SIDES):
                raise InvalidEvidenceError(
                    f"Workload '{identifier}' is missing a side in block {block}."
                )
            blocks.append({side: sides[side]["samples"] for side in SIDES})
            latencies.append(
                {side: sides[side]["latencySamples"] for side in SIDES}
            )
            calibrations.append(
                {side: sides[side]["calibrationSamples"] for side in SIDES}
            )
            pulses.append({side: sides[side]["calibrationPulses"] for side in SIDES})
            pulse_indices.append(
                {side: sides[side]["calibrationPulseIndices"] for side in SIDES}
            )
            resources.append({side: sides[side]["resources"] for side in SIDES})
            terminations.append(
                {
                    side: {
                        field: sides[side][field]
                        for field in (
                            "sampleCount",
                            "terminationReason",
                            "minimumDurationReached",
                        )
                    }
                    for side in SIDES
                }
            )
        tests.append(
            {
                "workloadId": identifier,
                "family": measurements[block_ids[0]][identifier]["candidate"]["family"],
                "blocks": blocks,
                "latencies": latencies,
                "calibrations": calibrations,
                "calibrationPulses": pulses,
                "calibrationPulseIndices": pulse_indices,
                "resources": resources,
                "terminations": terminations,
            }
        )

    return {
        "schemaVersion": 2,
        "kind": "paired-performance-evidence",
        "runId": run_id,
        "target": target,
        # The attempt machinery binds a receipt to the evidence it claims. That
        # binding needs the same identity a historical evaluation carries, so a
        # paired run travels the selection path instead of needing a second
        # one. `commit` is the candidate: the release is about that revision,
        # and the reference is recorded separately.
        "profile": profile,
        "commit": candidate_commit,
        "sourceHash": source_hash,
        "runnerClass": runner_class,
        "candidateCommit": candidate_commit,
        "referenceCommit": reference_commit,
        "benchmarkDriverSourceHash": driver_source_hash,
        "contractDigest": contract_digest,
        "blockCount": len(block_ids),
        "executionOrder": execution_order,
        "environments": environments,
        # Every block, not the last one: a catastrophe ceiling that only saw
        # the final block would accept a candidate that blew its budget early
        # and recovered.
        "candidateWorkloads": [
            {"block": block, "workloads": candidate_workloads[block]}
            for block in block_ids
        ],
        "soak": soak_report,
        "tests": tests,
    }
