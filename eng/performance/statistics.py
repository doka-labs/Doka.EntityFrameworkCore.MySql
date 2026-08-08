#!/usr/bin/env python3
"""Statistical evaluation and confirmation rules for performance evidence."""

import math
import statistics
from typing import Any, Sequence

if __package__:
    from .contract import (
        LATENCY_CONFIRMATION_RUNS,
        LATENCY_METRICS,
        P99_CONFIRMATION_RUNS,
        P99_EXPECTED_EXCEEDANCE_PROBABILITY,
        P99_SIGNIFICANCE_LEVEL,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
        required_positive_integer,
    )
else:
    from contract import (
        LATENCY_CONFIRMATION_RUNS,
        LATENCY_METRICS,
        P99_CONFIRMATION_RUNS,
        P99_EXPECTED_EXCEEDANCE_PROBABILITY,
        P99_SIGNIFICANCE_LEVEL,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
        required_positive_integer,
    )

def percentile(sorted_values: Sequence[float], percentile_value: float) -> float:
    """Calculate the same linear percentile interpolation used by the C# runner."""
    if not sorted_values:
        raise PerformanceEvidenceError("A percentile requires at least one sample.")

    position = (len(sorted_values) - 1) * percentile_value
    lower_index = math.floor(position)
    upper_index = math.ceil(position)
    if lower_index == upper_index:
        return sorted_values[lower_index]

    fraction = position - lower_index
    return sorted_values[lower_index] + (
        (sorted_values[upper_index] - sorted_values[lower_index]) * fraction
    )


def standard_error(values: Sequence[float]) -> float:
    """Calculate sample standard error for verification of persisted summaries."""
    if len(values) <= 1:
        return 0.0
    return statistics.stdev(values) / math.sqrt(len(values))


def binomial_survival_probability(
    sample_count: int,
    exceedance_count: int,
    expected_probability: float,
) -> float:
    """Return the exact probability of observing at least this many exceedances."""
    if sample_count <= 0:
        raise PerformanceEvidenceError("A binomial tail requires at least one sample.")
    if exceedance_count < 0 or exceedance_count > sample_count:
        raise PerformanceEvidenceError("The binomial exceedance count is invalid.")
    if expected_probability <= 0 or expected_probability >= 1:
        raise PerformanceEvidenceError("The binomial probability must be between zero and one.")
    if exceedance_count == 0:
        return 1.0

    log_term = (
        math.lgamma(sample_count + 1)
        - math.lgamma(exceedance_count + 1)
        - math.lgamma(sample_count - exceedance_count + 1)
        + exceedance_count * math.log(expected_probability)
        + (sample_count - exceedance_count) * math.log1p(-expected_probability)
    )
    term = math.exp(log_term)
    probability = term

    for observed_count in range(exceedance_count, sample_count):
        term *= (
            (sample_count - observed_count)
            / (observed_count + 1)
            * expected_probability
            / (1 - expected_probability)
        )
        probability += term

    return min(1.0, probability)


def calibration_adjustment_factor(
    workload_id: str,
    current_workload: dict[str, Any],
    baseline_workload: dict[str, Any],
) -> float:
    """Discount slower controls without amplifying a faster control into a regression."""
    current_calibration = finite_number(
        current_workload.get("calibrationMedianNanoseconds"),
        f"current.{workload_id}.calibrationMedianNanoseconds",
        minimum=0.000001,
    )
    baseline_calibration = finite_number(
        baseline_workload.get("calibrationMedianNanoseconds"),
        f"baseline.{workload_id}.calibrationMedianNanoseconds",
        minimum=0.000001,
    )

    return min(current_calibration / baseline_calibration, 1.0)


def historical_p99_check(
    workload_id: str,
    workload: dict[str, Any],
    baseline_workload: dict[str, Any],
    contract: dict[str, Any],
) -> dict[str, Any]:
    """Test whether normalized tail exceedances establish a p99 regression."""
    policy = contract["historicalBudgets"]
    ratio = finite_number(
        policy.get("normalizedP99Ratio"),
        "historicalBudgets.normalizedP99Ratio",
        minimum=1,
    )
    baseline_value = finite_number(
        baseline_workload.get("normalizedP99"),
        f"baseline.{workload_id}.normalizedP99",
        minimum=0,
    )
    maximum = baseline_value * ratio
    adjustment_factor = calibration_adjustment_factor(
        workload_id,
        workload,
        baseline_workload,
    )
    samples_payload = workload.get("_normalizedSamples")
    if samples_payload is None:
        samples_payload = workload.get("normalizedSamples")
    if not isinstance(samples_payload, list) or not samples_payload:
        raise PerformanceEvidenceError(
            f"Current workload '{workload_id}' has no normalized p99 samples."
        )
    samples = [
        finite_number(
            sample,
            f"current.{workload_id}.normalizedSamples[{index}]",
            minimum=0.000001,
        )
        for index, sample in enumerate(samples_payload)
    ]
    observed = finite_number(
        workload.get("normalizedP99"),
        f"current.{workload_id}.normalizedP99",
        minimum=0,
    )
    recomputed_observed = percentile(sorted(samples), 0.99)
    if not close_enough(observed, recomputed_observed):
        raise PerformanceEvidenceError(
            f"Current workload '{workload_id}' normalized p99 does not match its samples."
        )

    adjusted_samples = [
        sample * adjustment_factor
        for sample in samples
    ]
    actual = percentile(sorted(adjusted_samples), 0.99)
    exceedance_count = sum(sample > maximum for sample in adjusted_samples)
    p_value = binomial_survival_probability(
        len(samples),
        exceedance_count,
        P99_EXPECTED_EXCEEDANCE_PROBABILITY,
    )
    passed = p_value >= P99_SIGNIFICANCE_LEVEL

    return {
        "workloadId": workload_id,
        "metric": "normalizedP99",
        "baseline": baseline_value,
        "observed": observed,
        "actual": actual,
        "maximum": maximum,
        "calibrationAdjustmentFactor": adjustment_factor,
        "sampleCount": len(samples),
        "exceedanceCount": exceedance_count,
        "exceedanceRate": exceedance_count / len(samples),
        "expectedExceedanceProbability": P99_EXPECTED_EXCEEDANCE_PROBABILITY,
        "pValue": p_value,
        "significanceLevel": P99_SIGNIFICANCE_LEVEL,
        "passed": passed,
    }


def historical_latency_check(
    workload_id: str,
    metric: str,
    workload: dict[str, Any],
    baseline_workload: dict[str, Any],
    contract: dict[str, Any],
) -> dict[str, Any]:
    """Recompute one normalized latency metric from its bound samples."""
    if metric == "normalizedP99":
        return historical_p99_check(
            workload_id,
            workload,
            baseline_workload,
            contract,
        )
    if metric not in LATENCY_METRICS:
        raise PerformanceEvidenceError(
            f"Unsupported historical latency metric '{metric}'."
        )

    ratio_key, quantile = LATENCY_METRICS[metric]
    ratio = finite_number(
        contract["historicalBudgets"].get(ratio_key),
        f"historicalBudgets.{ratio_key}",
        minimum=1,
    )
    baseline_value = finite_number(
        baseline_workload.get(metric),
        f"baseline.{workload_id}.{metric}",
        minimum=0,
    )
    adjustment_factor = calibration_adjustment_factor(
        workload_id,
        workload,
        baseline_workload,
    )
    samples_payload = workload.get("_normalizedSamples")
    if samples_payload is None:
        samples_payload = workload.get("normalizedSamples")
    if not isinstance(samples_payload, list) or not samples_payload:
        raise PerformanceEvidenceError(
            f"Current workload '{workload_id}' has no normalized latency samples."
        )
    samples = [
        finite_number(
            sample,
            f"current.{workload_id}.normalizedSamples[{index}]",
            minimum=0.000001,
        )
        for index, sample in enumerate(samples_payload)
    ]
    observed = finite_number(
        workload.get(metric),
        f"current.{workload_id}.{metric}",
        minimum=0,
    )
    recomputed_observed = percentile(sorted(samples), quantile)
    if not close_enough(observed, recomputed_observed):
        raise PerformanceEvidenceError(
            f"Current workload '{workload_id}' {metric} does not match its samples."
        )

    actual = percentile(
        sorted(sample * adjustment_factor for sample in samples),
        quantile,
    )
    maximum = baseline_value * ratio

    return {
        "workloadId": workload_id,
        "metric": metric,
        "baseline": baseline_value,
        "observed": observed,
        "actual": actual,
        "maximum": maximum,
        "calibrationAdjustmentFactor": adjustment_factor,
        "sampleCount": len(samples),
        "passed": actual <= maximum,
    }


def validate_confirmed_latency_check(
    workload_id: str,
    metric: str,
    current_workload: dict[str, Any],
    baseline_workload: dict[str, Any],
    confirmation: dict[str, Any],
    contract: dict[str, Any],
) -> dict[str, Any]:
    """Recompute one independent latency confirmation and bind its trigger."""
    confirmation_runs = required_positive_integer(
        confirmation,
        "confirmationRuns",
        f"tailConfirmation.{workload_id}",
    )
    if confirmation_runs != LATENCY_CONFIRMATION_RUNS:
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' has the wrong run count."
        )
    original_sample_count = required_positive_integer(
        confirmation,
        "originalSampleCount",
        f"tailConfirmation.{workload_id}",
    )
    if original_sample_count != current_workload.get("sampleCount"):
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' sample trigger drifted."
        )

    results = confirmation.get("metrics")
    if not isinstance(results, list):
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' has no metric results."
        )
    matches = [
        result
        for result in results
        if isinstance(result, dict) and result.get("metric") == metric
    ]
    if len(matches) != 1:
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' has an invalid '{metric}' result."
        )
    result = matches[0]
    trigger = finite_number(
        current_workload.get(metric),
        f"current.{workload_id}.{metric}",
        minimum=0,
    )
    original_observed = finite_number(
        result.get("originalObserved"),
        f"tailConfirmation.{workload_id}.{metric}.originalObserved",
        minimum=0,
    )
    if not close_enough(original_observed, trigger):
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' {metric} trigger drifted."
        )

    check = historical_latency_check(
        workload_id,
        metric,
        {
            metric: result.get("observed"),
            "normalizedSamples": confirmation.get("normalizedSamples"),
            "calibrationMedianNanoseconds": confirmation.get(
                "confirmationCalibrationMedianNanoseconds"
            ),
        },
        baseline_workload,
        contract,
    )
    if confirmation.get("confirmationSampleCount") != check["sampleCount"]:
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' sample count drifted."
        )
    for key in (
        "baseline",
        "observed",
        "actual",
        "maximum",
        "calibrationAdjustmentFactor",
    ):
        actual = finite_number(
            result.get(key),
            f"tailConfirmation.{workload_id}.{metric}.{key}",
            minimum=0,
        )
        if not close_enough(actual, check[key]):
            raise PerformanceEvidenceError(
                f"Tail confirmation '{workload_id}' {metric} {key} drifted."
            )
    if result.get("passed") is not check["passed"]:
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' {metric} verdict drifted."
        )
    if metric == "normalizedP99":
        if result.get("exceedanceCount") != check["exceedanceCount"]:
            raise PerformanceEvidenceError(
                f"Tail confirmation '{workload_id}' p99 exceedance count drifted."
            )
        for key in (
            "exceedanceRate",
            "expectedExceedanceProbability",
            "pValue",
            "significanceLevel",
        ):
            actual = finite_number(
                result.get(key),
                f"tailConfirmation.{workload_id}.{metric}.{key}",
                minimum=0,
            )
            if not close_enough(actual, check[key]):
                raise PerformanceEvidenceError(
                    f"Tail confirmation '{workload_id}' p99 {key} drifted."
                )

    return {
        **check,
        "triggerActual": trigger,
        "confirmationRequired": True,
        "confirmationRuns": confirmation_runs,
    }


def validate_confirmed_p99_check(
    workload_id: str,
    current_workload: dict[str, Any],
    baseline_workload: dict[str, Any],
    confirmation: dict[str, Any],
    contract: dict[str, Any],
) -> dict[str, Any]:
    """Recompute an independent p99 confirmation and bind it to its trigger."""
    confirmation_runs = required_positive_integer(
        confirmation,
        "confirmationRuns",
        f"tailConfirmation.{workload_id}",
    )
    if confirmation_runs != P99_CONFIRMATION_RUNS:
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' has the wrong run count."
        )
    original_sample_count = required_positive_integer(
        confirmation,
        "originalSampleCount",
        f"tailConfirmation.{workload_id}",
    )
    if original_sample_count != current_workload.get("sampleCount"):
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' sample trigger drifted."
        )
    original_p99 = finite_number(
        confirmation.get("originalNormalizedP99"),
        f"tailConfirmation.{workload_id}.originalNormalizedP99",
        minimum=0,
    )
    current_p99 = finite_number(
        current_workload.get("normalizedP99"),
        f"current.{workload_id}.normalizedP99",
        minimum=0,
    )
    if not close_enough(original_p99, current_p99):
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' p99 trigger drifted."
        )

    confirmation_p99 = finite_number(
        confirmation.get("confirmationNormalizedP99"),
        f"tailConfirmation.{workload_id}.confirmationNormalizedP99",
        minimum=0,
    )
    check = historical_p99_check(
        workload_id,
        {
            "normalizedP99": confirmation_p99,
            "normalizedSamples": confirmation.get("normalizedSamples"),
            "calibrationMedianNanoseconds": confirmation.get(
                "confirmationCalibrationMedianNanoseconds"
            ),
        },
        baseline_workload,
        contract,
    )
    expected_values = {
        "confirmationSampleCount": check["sampleCount"],
        "exceedanceCount": check["exceedanceCount"],
    }
    for key, expected in expected_values.items():
        if confirmation.get(key) != expected:
            raise PerformanceEvidenceError(
                f"Tail confirmation '{workload_id}' {key} drifted."
            )
    for key, expected in (
        ("maximumNormalizedP99", check["maximum"]),
        (
            "calibrationAdjustmentFactor",
            check["calibrationAdjustmentFactor"],
        ),
        ("exceedanceRate", check["exceedanceRate"]),
        (
            "expectedExceedanceProbability",
            check["expectedExceedanceProbability"],
        ),
        ("pValue", check["pValue"]),
        ("significanceLevel", check["significanceLevel"]),
    ):
        actual = finite_number(
            confirmation.get(key),
            f"tailConfirmation.{workload_id}.{key}",
            minimum=0,
        )
        if not close_enough(actual, expected):
            raise PerformanceEvidenceError(
                f"Tail confirmation '{workload_id}' {key} drifted."
            )
    if confirmation.get("passed") is not check["passed"]:
        raise PerformanceEvidenceError(
            f"Tail confirmation '{workload_id}' verdict drifted."
        )

    return {
        **check,
        "triggerActual": current_p99,
        "confirmationRequired": True,
        "confirmationRuns": confirmation_runs,
    }
