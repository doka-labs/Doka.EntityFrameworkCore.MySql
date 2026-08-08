#!/usr/bin/env python3
"""Historical baseline matching and multi-run tail confirmation."""

import argparse
import copy
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Sequence

if __package__:
    from .contract import (
        LATENCY_CONFIRMATION_RUNS,
        LATENCY_METRICS,
        P99_CONFIRMATION_RUNS,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
        load_json,
        non_negative_integer,
        require_identity,
        required_positive_integer,
        required_sha256,
        required_string,
        sha256,
        validate_contract,
    )
    from .host import validate_host_preflight
    from .environment import (
        validate_environment_compatibility,
        validate_host_workload_binding,
    )
    from .reports import (
        validate_absolute_budgets,
        validate_workload_report,
    )
    from .statistics import (
        calibration_adjustment_factor,
        historical_latency_check,
        percentile,
        standard_error,
        validate_confirmed_latency_check,
    )
else:
    from contract import (
        LATENCY_CONFIRMATION_RUNS,
        LATENCY_METRICS,
        P99_CONFIRMATION_RUNS,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
        load_json,
        non_negative_integer,
        require_identity,
        required_positive_integer,
        required_sha256,
        required_string,
        sha256,
        validate_contract,
    )
    from host import validate_host_preflight
    from environment import (
        validate_environment_compatibility,
        validate_host_workload_binding,
    )
    from reports import (
        validate_absolute_budgets,
        validate_workload_report,
    )
    from statistics import (
        calibration_adjustment_factor,
        historical_latency_check,
        percentile,
        standard_error,
        validate_confirmed_latency_check,
    )

def load_matching_baseline(
    baseline_path: Path,
    contract: dict[str, Any],
    *,
    target: str,
    profile: str,
    runner_class: str,
) -> tuple[dict[str, Any], dict[str, Any]]:
    """Load the unique accepted baseline for target, profile, and runner class."""
    baseline = load_json(baseline_path)
    if baseline.get("schemaVersion") != 3:
        raise PerformanceEvidenceError("Baseline schemaVersion must be 3.")
    if baseline.get("baselineState") != "accepted":
        raise PerformanceEvidenceError("Baseline state must be accepted.")
    if baseline.get("contractVersion") != contract["contractVersion"]:
        raise PerformanceEvidenceError("Baseline contractVersion does not match the active contract.")

    entries = baseline.get("baselines")
    if not isinstance(entries, list):
        raise PerformanceEvidenceError("Baseline baselines must be an array.")
    matches = [
        entry
        for entry in entries
        if isinstance(entry, dict)
        and entry.get("target") == target
        and entry.get("profile") == profile
        and entry.get("runnerClass") == runner_class
    ]
    if len(matches) != 1:
        raise PerformanceEvidenceError(
            f"Expected one accepted baseline for target '{target}', profile '{profile}', "
            f"runner '{runner_class}', found {len(matches)}."
        )

    return baseline, matches[0]


def historical_p99_confirmation_candidates(
    current: Sequence[dict[str, Any]],
    baseline_entry: dict[str, Any],
    contract: dict[str, Any],
) -> list[dict[str, Any]]:
    """Return the legacy flat view of p99 confirmation candidates."""
    baseline_workloads = baseline_entry.get("workloads")
    if not isinstance(baseline_workloads, list):
        raise PerformanceEvidenceError("Accepted baseline entry has no workloads.")
    baseline_by_id = {
        workload.get("id"): workload
        for workload in baseline_workloads
        if isinstance(workload, dict) and isinstance(workload.get("id"), str)
    }
    candidates: list[dict[str, Any]] = []
    for workload in current:
        workload_id = workload["id"]
        baseline_workload = baseline_by_id.get(workload_id)
        if not isinstance(baseline_workload, dict):
            raise PerformanceEvidenceError(
                f"Accepted baseline has no workload '{workload_id}'."
            )
        observed = finite_number(
            workload.get("normalizedP99"),
            f"current.{workload_id}.normalizedP99",
            minimum=0,
        )
        baseline = finite_number(
            baseline_workload.get("normalizedP99"),
            f"baseline.{workload_id}.normalizedP99",
            minimum=0,
        )
        ratio = finite_number(
            contract["historicalBudgets"].get("normalizedP99Ratio"),
            "historicalBudgets.normalizedP99Ratio",
            minimum=1,
        )
        adjustment_factor = calibration_adjustment_factor(
            workload_id,
            workload,
            baseline_workload,
        )
        actual = observed * adjustment_factor
        maximum = baseline * ratio
        if actual <= maximum:
            continue
        candidates.append(
            {
                "workloadId": workload_id,
                "baseline": baseline,
                "baselineCalibrationMedianNanoseconds": finite_number(
                    baseline_workload.get("calibrationMedianNanoseconds"),
                    f"baseline.{workload_id}.calibrationMedianNanoseconds",
                    minimum=0.000001,
                ),
                "observed": observed,
                "actual": actual,
                "maximum": maximum,
                "calibrationAdjustmentFactor": adjustment_factor,
                "confirmationRuns": P99_CONFIRMATION_RUNS,
            }
        )

    return candidates


def historical_latency_confirmation_candidates(
    current: Sequence[dict[str, Any]],
    baseline_entry: dict[str, Any],
    contract: dict[str, Any],
) -> list[dict[str, Any]]:
    """Identify latency regressions that require independent confirmation."""
    baseline_workloads = baseline_entry.get("workloads")
    if not isinstance(baseline_workloads, list):
        raise PerformanceEvidenceError("Accepted baseline entry has no workloads.")

    baseline_by_id = {
        workload.get("id"): workload
        for workload in baseline_workloads
        if isinstance(workload, dict) and isinstance(workload.get("id"), str)
    }
    candidates: list[dict[str, Any]] = []

    for workload in current:
        workload_id = workload["id"]
        baseline_workload = baseline_by_id.get(workload_id)
        if not isinstance(baseline_workload, dict):
            raise PerformanceEvidenceError(
                f"Accepted baseline has no workload '{workload_id}'."
            )

        baseline_calibration = finite_number(
            baseline_workload.get("calibrationMedianNanoseconds"),
            f"baseline.{workload_id}.calibrationMedianNanoseconds",
            minimum=0.000001,
        )
        metrics = []
        for metric in LATENCY_METRICS:
            check = historical_latency_check(
                workload_id,
                metric,
                workload,
                baseline_workload,
                contract,
            )
            if check["actual"] > check["maximum"]:
                metrics.append(
                    {
                        key: check[key]
                        for key in (
                            "metric",
                            "baseline",
                            "observed",
                            "actual",
                            "maximum",
                            "calibrationAdjustmentFactor",
                        )
                    }
                )
        if metrics:
            candidates.append(
                {
                    "workloadId": workload_id,
                    "baselineCalibrationMedianNanoseconds": baseline_calibration,
                    "confirmationRuns": LATENCY_CONFIRMATION_RUNS,
                    "metrics": metrics,
                }
            )

    return sorted(candidates, key=lambda candidate: candidate["workloadId"])


def plan_tail_confirmation(args: argparse.Namespace) -> dict[str, Any]:
    """Plan bounded reruns for normalized latency outside historical budgets."""
    contract = load_json(Path(args.contract))
    validate_contract(contract)
    report = load_json(Path(args.workloads))
    normalized = validate_workload_report(
        report,
        contract,
        run_id=args.run_id,
        target=args.target,
        profile=args.profile,
    )
    validate_absolute_budgets(normalized, contract)

    candidates: list[dict[str, Any]] = []
    baseline_version: str | None = None
    profile_contract = contract["profiles"][args.profile]
    if profile_contract["baselineRequired"]:
        baseline, matching_entry = load_matching_baseline(
            Path(args.baseline),
            contract,
            target=args.target,
            profile=args.profile,
            runner_class=report["runnerClass"],
        )
        baseline_version = required_string(baseline, "baselineVersion", "baseline")
        baseline_environment = matching_entry.get("environment")
        if not isinstance(baseline_environment, dict):
            raise PerformanceEvidenceError(
                "Accepted baseline entry has no environment evidence."
            )
        validate_environment_compatibility(
            report["environment"],
            baseline_environment,
        )
        candidates = historical_latency_confirmation_candidates(
            normalized,
            matching_entry,
            contract,
        )

    return {
        "schemaVersion": 2,
        "kind": "performance-tail-confirmation-plan",
        "contractVersion": contract["contractVersion"],
        "runId": args.run_id,
        "target": args.target,
        "profile": args.profile,
        "commit": report["commit"],
        "sourceHash": report["sourceHash"],
        "runnerClass": report["runnerClass"],
        "baselineVersion": baseline_version,
        "workloads": candidates,
    }


def merge_workload_tail_samples(
    original: dict[str, Any],
    confirmations: Sequence[dict[str, Any]],
) -> dict[str, Any]:
    """Merge independently measured samples and recompute every derived metric."""
    entries = [original, *confirmations]
    operations_per_sample = required_positive_integer(
        original,
        "operationsPerSample",
        f"workload.{original.get('id')}",
    )
    samples: list[float] = []
    calibration_samples: list[float] = []
    calibration_pulses: list[float] = []
    calibration_pulse_indices: list[int] = []
    normalized_samples: list[float] = []
    measured_operations = 0

    for entry in entries:
        if entry.get("id") != original.get("id"):
            raise PerformanceEvidenceError("Tail confirmation workload IDs disagree.")
        if entry.get("operationsPerSample") != operations_per_sample:
            raise PerformanceEvidenceError(
                f"Tail confirmation '{original.get('id')}' operationsPerSample disagrees."
            )
        entry_samples = entry.get("samplesNanoseconds")
        if not isinstance(entry_samples, list) or not entry_samples:
            raise PerformanceEvidenceError(
                f"Tail confirmation '{original.get('id')}' has no samples."
            )
        samples.extend(
            finite_number(
                sample,
                f"tailConfirmation.{original.get('id')}.sample",
                minimum=0.000001,
            )
            for sample in entry_samples
        )
        entry_calibration = entry.get("calibrationNanoseconds")
        entry_calibration_pulses = entry.get("calibrationPulseNanoseconds")
        entry_calibration_pulse_indices = entry.get("calibrationPulseIndices")
        entry_normalized = entry.get("normalizedSamples")
        if (
            not isinstance(entry_calibration, list)
            or len(entry_calibration) != len(entry_samples)
            or not isinstance(entry_calibration_pulses, list)
            or not entry_calibration_pulses
            or not isinstance(entry_calibration_pulse_indices, list)
            or len(entry_calibration_pulse_indices) != len(entry_samples)
            or not isinstance(entry_normalized, list)
            or len(entry_normalized) != len(entry_samples)
        ):
            raise PerformanceEvidenceError(
                f"Tail confirmation '{original.get('id')}' calibration evidence disagrees."
            )
        pulse_offset = len(calibration_pulses)
        calibration_pulses.extend(
            finite_number(
                sample,
                f"tailConfirmation.{original.get('id')}.calibrationPulse",
                minimum=0.000001,
            )
            for sample in entry_calibration_pulses
        )
        calibration_pulse_indices.extend(
            non_negative_integer(
                value,
                f"tailConfirmation.{original.get('id')}.calibrationPulseIndex",
            )
            + pulse_offset
            for value in entry_calibration_pulse_indices
        )
        calibration_samples.extend(
            finite_number(
                sample,
                f"tailConfirmation.{original.get('id')}.calibration",
                minimum=0.000001,
            )
            for sample in entry_calibration
        )
        normalized_samples.extend(
            finite_number(
                sample,
                f"tailConfirmation.{original.get('id')}.normalized",
                minimum=0.000001,
            )
            for sample in entry_normalized
        )
        measured_operations += len(entry_samples) * operations_per_sample

    sorted_samples = sorted(samples)
    merged = copy.deepcopy(original)
    merged["sampleCount"] = len(samples)
    merged["checksum"] = sum(int(entry.get("checksum", 0)) for entry in entries)
    merged["medianNanoseconds"] = percentile(sorted_samples, 0.5)
    merged["p95Nanoseconds"] = percentile(sorted_samples, 0.95)
    merged["p99Nanoseconds"] = percentile(sorted_samples, 0.99)
    merged["standardErrorNanoseconds"] = standard_error(samples)
    merged["samplesNanoseconds"] = samples
    sorted_calibration_pulses = sorted(calibration_pulses)
    sorted_normalized = sorted(normalized_samples)
    merged["calibrationMedianNanoseconds"] = percentile(
        sorted_calibration_pulses,
        0.5,
    )
    merged["calibrationStandardErrorNanoseconds"] = standard_error(
        calibration_pulses
    )
    merged["normalizedMedian"] = percentile(sorted_normalized, 0.5)
    merged["normalizedP95"] = percentile(sorted_normalized, 0.95)
    merged["normalizedP99"] = percentile(sorted_normalized, 0.99)
    merged["calibrationNanoseconds"] = calibration_samples
    merged["calibrationPulseNanoseconds"] = calibration_pulses
    merged["calibrationPulseIndices"] = calibration_pulse_indices
    merged["normalizedSamples"] = normalized_samples

    for metric in (
        "allocatedBytesPerOperation",
        "gen0CollectionsPer1000",
        "gen1CollectionsPer1000",
        "gen2CollectionsPer1000",
    ):
        weighted_total = 0.0
        for entry in entries:
            entry_samples = entry["samplesNanoseconds"]
            entry_operations = len(entry_samples) * operations_per_sample
            weighted_total += finite_number(
                entry.get(metric),
                f"tailConfirmation.{original.get('id')}.{metric}",
                minimum=0,
            ) * entry_operations
        merged[metric] = weighted_total / measured_operations

    merged["retainedBytes"] = max(
        finite_number(
            entry.get("retainedBytes"),
            f"tailConfirmation.{original.get('id')}.retainedBytes",
            minimum=0,
        )
        for entry in entries
    )
    return merged


def merge_tail_confirmations(args: argparse.Namespace) -> dict[str, Any]:
    """Validate bounded reruns and merge them into the canonical workload report."""
    contract = load_json(Path(args.contract))
    validate_contract(contract)
    original_report = load_json(Path(args.workloads))
    original_normalized = validate_workload_report(
        original_report,
        contract,
        run_id=args.run_id,
        target=args.target,
        profile=args.profile,
    )
    original_by_id = {
        workload["id"]: workload
        for workload in original_report["workloads"]
    }
    normalized_by_id = {
        workload["id"]: workload
        for workload in original_normalized
    }

    plan_path = Path(args.plan)
    plan = load_json(plan_path)
    if (
        plan.get("schemaVersion") != 2
        or plan.get("kind") != "performance-tail-confirmation-plan"
    ):
        raise PerformanceEvidenceError("Tail confirmation plan schema or kind is invalid.")
    require_identity(
        plan,
        label="tailConfirmationPlan",
        run_id=args.run_id,
        target=args.target,
        profile=args.profile,
        contract_version=contract["contractVersion"],
    )
    for key in ("commit", "sourceHash", "runnerClass"):
        if plan.get(key) != original_report.get(key):
            raise PerformanceEvidenceError(
                f"Tail confirmation plan and workload report disagree on '{key}'."
            )

    planned_workloads = plan.get("workloads")
    if not isinstance(planned_workloads, list) or not planned_workloads:
        raise PerformanceEvidenceError("Tail confirmation plan has no workloads.")
    planned_by_id: dict[str, dict[str, Any]] = {}
    expected_confirmation_count = 0
    for index, candidate in enumerate(planned_workloads):
        if not isinstance(candidate, dict):
            raise PerformanceEvidenceError(
                f"tailConfirmationPlan.workloads[{index}] must be an object."
            )
        workload_id = required_string(
            candidate,
            "workloadId",
            f"tailConfirmationPlan.workloads[{index}]",
        )
        if workload_id in planned_by_id or workload_id not in original_by_id:
            raise PerformanceEvidenceError(
                f"Tail confirmation plan contains invalid workload '{workload_id}'."
            )
        confirmation_runs = required_positive_integer(
            candidate,
            "confirmationRuns",
            f"tailConfirmationPlan.{workload_id}",
        )
        if confirmation_runs != LATENCY_CONFIRMATION_RUNS:
            raise PerformanceEvidenceError(
                f"Tail confirmation plan requires {confirmation_runs} runs for "
                f"'{workload_id}', expected {LATENCY_CONFIRMATION_RUNS}."
            )
        finite_number(
            candidate.get("baselineCalibrationMedianNanoseconds"),
            f"{workload_id}.baselineCalibrationMedianNanoseconds",
            minimum=0.000001,
        )
        planned_metrics = candidate.get("metrics")
        if not isinstance(planned_metrics, list) or not planned_metrics:
            raise PerformanceEvidenceError(
                f"Tail confirmation plan has no metrics for '{workload_id}'."
            )
        seen_metrics: set[str] = set()
        for metric_index, planned_metric in enumerate(planned_metrics):
            if not isinstance(planned_metric, dict):
                raise PerformanceEvidenceError(
                    f"tailConfirmationPlan.{workload_id}.metrics[{metric_index}] "
                    "must be an object."
                )
            metric = required_string(
                planned_metric,
                "metric",
                f"tailConfirmationPlan.{workload_id}.metrics[{metric_index}]",
            )
            if metric not in LATENCY_METRICS or metric in seen_metrics:
                raise PerformanceEvidenceError(
                    f"Tail confirmation plan contains invalid metric '{metric}' "
                    f"for '{workload_id}'."
                )
            seen_metrics.add(metric)
            observed = finite_number(
                planned_metric.get("observed"),
                f"{workload_id}.{metric}.observed",
                minimum=0,
            )
            if not close_enough(observed, normalized_by_id[workload_id][metric]):
                raise PerformanceEvidenceError(
                    f"Tail confirmation plan observed value for '{workload_id}' "
                    f"{metric} drifted."
                )
            adjustment_factor = finite_number(
                planned_metric.get("calibrationAdjustmentFactor"),
                f"{workload_id}.{metric}.calibrationAdjustmentFactor",
                minimum=0.000001,
            )
            if adjustment_factor > 1:
                raise PerformanceEvidenceError(
                    f"Tail confirmation plan adjustment for '{workload_id}' "
                    f"{metric} is invalid."
                )
            actual = finite_number(
                planned_metric.get("actual"),
                f"{workload_id}.{metric}.actual",
                minimum=0,
            )
            if not close_enough(actual, observed * adjustment_factor):
                raise PerformanceEvidenceError(
                    f"Tail confirmation plan actual value for '{workload_id}' "
                    f"{metric} drifted."
                )
            baseline = finite_number(
                planned_metric.get("baseline"),
                f"{workload_id}.{metric}.baseline",
                minimum=0,
            )
            maximum = finite_number(
                planned_metric.get("maximum"),
                f"{workload_id}.{metric}.maximum",
                minimum=0,
            )
            ratio_key, _ = LATENCY_METRICS[metric]
            ratio = finite_number(
                contract["historicalBudgets"].get(ratio_key),
                f"historicalBudgets.{ratio_key}",
                minimum=1,
            )
            if not close_enough(maximum, baseline * ratio) or actual <= maximum:
                raise PerformanceEvidenceError(
                    f"Tail confirmation plan threshold for '{workload_id}' "
                    f"{metric} is invalid."
                )
        planned_by_id[workload_id] = candidate
        expected_confirmation_count += confirmation_runs

    confirmation_paths = [Path(path) for path in args.confirmation]
    host_paths = [Path(path) for path in args.confirmation_host]
    if (
        len(confirmation_paths) != expected_confirmation_count
        or len(host_paths) != expected_confirmation_count
    ):
        raise PerformanceEvidenceError(
            "Tail confirmation report and host-preflight counts do not match the plan."
        )

    confirmations_by_id: dict[str, list[dict[str, Any]]] = {
        workload_id: []
        for workload_id in planned_by_id
    }
    confirmation_artifacts: list[dict[str, Any]] = []
    for confirmation_path, host_path in zip(
        confirmation_paths,
        host_paths,
        strict=True,
    ):
        report = load_json(confirmation_path)
        require_identity(
            report,
            label="tailConfirmation",
            run_id=args.run_id,
            target=args.target,
            profile=args.profile,
            contract_version=contract["contractVersion"],
        )
        if (
            report.get("schemaVersion") != 3
            or report.get("kind") != "performance-workload-diagnostic"
        ):
            raise PerformanceEvidenceError(
                f"Tail confirmation '{confirmation_path}' schema or kind is invalid."
            )
        for key in ("commit", "sourceHash", "runnerClass"):
            if report.get(key) != original_report.get(key):
                raise PerformanceEvidenceError(
                    f"Tail confirmation '{confirmation_path}' disagrees on '{key}'."
                )

        host = validate_host_preflight(
            load_json(host_path),
            contract,
            maximum_age_hours=float(contract["evidenceMaximumAgeHours"]),
        )
        environment = report.get("environment")
        if not isinstance(environment, dict):
            raise PerformanceEvidenceError(
                f"Tail confirmation '{confirmation_path}' has no environment."
            )
        validate_host_workload_binding(host, environment)

        entries = report.get("workloads")
        if not isinstance(entries, list) or len(entries) != 1:
            raise PerformanceEvidenceError(
                f"Tail confirmation '{confirmation_path}' must contain one workload."
            )
        workload = entries[0]
        if not isinstance(workload, dict):
            raise PerformanceEvidenceError(
                f"Tail confirmation '{confirmation_path}' workload is invalid."
            )
        workload_id = required_string(workload, "id", "tailConfirmation.workload")
        if workload_id not in planned_by_id:
            raise PerformanceEvidenceError(
                f"Tail confirmation contains unplanned workload '{workload_id}'."
            )

        synthetic_report = copy.deepcopy(report)
        synthetic_report["kind"] = "performance-workloads"
        synthetic_report["workloads"] = [
            workload if entry_id == workload_id else copy.deepcopy(entry)
            for entry_id, entry in original_by_id.items()
        ]
        validate_workload_report(
            synthetic_report,
            contract,
            run_id=args.run_id,
            target=args.target,
            profile=args.profile,
        )
        confirmations_by_id[workload_id].append(workload)
        confirmation_artifacts.append(
            {
                "workloadId": workload_id,
                "reportSha256": sha256(confirmation_path),
                "hostPreflightSha256": sha256(host_path),
            }
        )

    merged_report = copy.deepcopy(original_report)
    merged_by_id = {
        workload["id"]: workload
        for workload in merged_report["workloads"]
    }
    confirmation_results: list[dict[str, Any]] = []
    for workload_id, candidate in planned_by_id.items():
        confirmations = confirmations_by_id[workload_id]
        if len(confirmations) != candidate["confirmationRuns"]:
            raise PerformanceEvidenceError(
                f"Tail confirmation '{workload_id}' does not satisfy the planned run count."
            )
        # The original population selected this workload for confirmation. It
        # must not also influence the verdict, or selection bias would make an
        # ordinary high tail more likely to fail its own confirmation.
        merged = merge_workload_tail_samples(
            confirmations[0],
            confirmations[1:],
        )
        metric_results: list[dict[str, Any]] = []
        for planned_metric in candidate["metrics"]:
            metric = planned_metric["metric"]
            check = historical_latency_check(
                workload_id,
                metric,
                merged,
                {
                    metric: planned_metric["baseline"],
                    "calibrationMedianNanoseconds": candidate[
                        "baselineCalibrationMedianNanoseconds"
                    ],
                },
                contract,
            )
            if not close_enough(check["maximum"], planned_metric["maximum"]):
                raise PerformanceEvidenceError(
                    f"Tail confirmation plan maximum for '{workload_id}' "
                    f"{metric} drifted."
                )
            metric_results.append(
                {
                    **check,
                    "originalObserved": normalized_by_id[workload_id][metric],
                }
            )
            if not check["passed"]:
                if metric == "normalizedP99":
                    raise PerformanceEvidenceError(
                        f"Confirmed historical p99 regression for '{workload_id}': "
                        f"{check['exceedanceCount']} of {check['sampleCount']} "
                        f"samples exceeded {check['maximum']}; p-value "
                        f"{check['pValue']}."
                    )
                raise PerformanceEvidenceError(
                    f"Confirmed historical latency regression for '{workload_id}' "
                    f"{metric}: {check['actual']} > {check['maximum']}."
                )
        confirmation_results.append(
            {
                "workloadId": workload_id,
                "confirmationRuns": len(confirmations),
                "originalSampleCount": original_by_id[workload_id]["sampleCount"],
                "confirmationSampleCount": merged["sampleCount"],
                "confirmationCalibrationMedianNanoseconds": merged[
                    "calibrationMedianNanoseconds"
                ],
                "normalizedSamples": merged["normalizedSamples"],
                "metrics": metric_results,
            }
        )

    merged_report["generatedUtc"] = (
        datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    )
    merged_report["workloads"] = [
        merged_by_id[workload["id"]]
        for workload in original_report["workloads"]
    ]
    merged_report["tailConfirmations"] = {
        "planSha256": sha256(plan_path),
        "artifacts": confirmation_artifacts,
        "results": confirmation_results,
    }
    validate_workload_report(
        merged_report,
        contract,
        run_id=args.run_id,
        target=args.target,
        profile=args.profile,
    )
    return merged_report


def validate_historical_budgets(
    current: Sequence[dict[str, Any]],
    baseline_entry: dict[str, Any],
    contract: dict[str, Any],
    tail_confirmations: dict[str, Any] | None = None,
) -> list[dict[str, Any]]:
    """Compare current metrics with the accepted matching historical record."""
    baseline_workloads = baseline_entry.get("workloads")
    if not isinstance(baseline_workloads, list):
        raise PerformanceEvidenceError("Accepted baseline entry has no workloads.")
    baseline_by_id = {
        workload.get("id"): workload
        for workload in baseline_workloads
        if isinstance(workload, dict) and isinstance(workload.get("id"), str)
    }
    current_by_id = {workload["id"]: workload for workload in current}
    if set(baseline_by_id) != set(current_by_id):
        missing = sorted(set(current_by_id) - set(baseline_by_id))
        stale = sorted(set(baseline_by_id) - set(current_by_id))
        raise PerformanceEvidenceError(
            f"Historical baseline matrix drift. Missing: [{', '.join(missing)}]. "
            f"Stale: [{', '.join(stale)}]."
        )

    policy = contract["historicalBudgets"]
    checks: list[dict[str, Any]] = []

    for workload_id in sorted(current_by_id):
        current_workload = current_by_id[workload_id]
        baseline_workload = baseline_by_id[workload_id]
        metric = "allocatedBytesPerOperation"
        observed_value = finite_number(
            current_workload.get(metric),
            f"current.{workload_id}.{metric}",
            minimum=0,
        )
        baseline_value = finite_number(
            baseline_workload.get(metric),
            f"baseline.{workload_id}.{metric}",
            minimum=0,
        )
        ratio = finite_number(
            policy.get("allocatedBytesRatio"),
            "historicalBudgets.allocatedBytesRatio",
            minimum=1,
        )
        maximum = baseline_value * ratio
        passed = observed_value <= maximum
        checks.append(
            {
                "workloadId": workload_id,
                "metric": metric,
                "baseline": baseline_value,
                "observed": observed_value,
                "actual": observed_value,
                "maximum": maximum,
                "calibrationAdjustmentFactor": 1.0,
                "passed": passed,
            }
        )
        if not passed:
            raise PerformanceEvidenceError(
                f"Historical budget failed for '{workload_id}' {metric}: "
                f"{observed_value} > {maximum} from baseline {baseline_value}."
            )

    confirmation_candidates = historical_latency_confirmation_candidates(
        current,
        baseline_entry,
        contract,
    )
    candidate_metrics_by_id = {
        candidate["workloadId"]: {
            metric["metric"]
            for metric in candidate["metrics"]
        }
        for candidate in confirmation_candidates
    }
    candidate_ids = set(candidate_metrics_by_id)
    confirmation_by_id: dict[str, dict[str, Any]] = {}
    if candidate_ids:
        if not isinstance(tail_confirmations, dict):
            raise PerformanceEvidenceError(
                "Historical latency candidates require independent confirmations."
            )
        results = tail_confirmations.get("results")
        artifacts = tail_confirmations.get("artifacts")
        required_sha256(tail_confirmations, "planSha256", "tailConfirmations")
        if not isinstance(results, list) or not isinstance(artifacts, list):
            raise PerformanceEvidenceError("Tail confirmation evidence is incomplete.")
        for index, result in enumerate(results):
            if not isinstance(result, dict):
                raise PerformanceEvidenceError(
                    f"tailConfirmations.results[{index}] must be an object."
                )
            workload_id = required_string(
                result,
                "workloadId",
                f"tailConfirmations.results[{index}]",
            )
            if workload_id in confirmation_by_id:
                raise PerformanceEvidenceError(
                    f"Tail confirmation contains duplicate '{workload_id}'."
                )
            confirmation_by_id[workload_id] = result
        if set(confirmation_by_id) != candidate_ids:
            raise PerformanceEvidenceError(
                "Tail confirmation result matrix does not match current latency "
                "candidates."
            )
        for workload_id, result in confirmation_by_id.items():
            metric_results = result.get("metrics")
            if not isinstance(metric_results, list):
                raise PerformanceEvidenceError(
                    f"Tail confirmation result for '{workload_id}' has no metrics."
                )
            result_metrics = {
                required_string(
                    metric_result,
                    "metric",
                    f"tailConfirmations.{workload_id}.metrics[{index}]",
                )
                for index, metric_result in enumerate(metric_results)
                if isinstance(metric_result, dict)
            }
            if (
                len(result_metrics) != len(metric_results)
                or result_metrics != candidate_metrics_by_id[workload_id]
            ):
                raise PerformanceEvidenceError(
                    f"Tail confirmation metric matrix for '{workload_id}' does not "
                    "match the current candidates."
                )
        artifact_counts = {
            workload_id: 0
            for workload_id in candidate_ids
        }
        for index, artifact in enumerate(artifacts):
            if not isinstance(artifact, dict):
                raise PerformanceEvidenceError(
                    f"tailConfirmations.artifacts[{index}] must be an object."
                )
            workload_id = required_string(
                artifact,
                "workloadId",
                f"tailConfirmations.artifacts[{index}]",
            )
            if workload_id not in artifact_counts:
                raise PerformanceEvidenceError(
                    f"Tail confirmation contains unplanned artifact '{workload_id}'."
                )
            required_sha256(
                artifact,
                "reportSha256",
                f"tailConfirmations.artifacts[{index}]",
            )
            required_sha256(
                artifact,
                "hostPreflightSha256",
                f"tailConfirmations.artifacts[{index}]",
            )
            artifact_counts[workload_id] += 1
        if any(
            count != LATENCY_CONFIRMATION_RUNS
            for count in artifact_counts.values()
        ):
            raise PerformanceEvidenceError(
                "Tail confirmation artifact counts do not match the required runs."
            )
    elif tail_confirmations is not None:
        raise PerformanceEvidenceError(
            "Tail confirmation evidence exists without a current latency candidate."
        )

    for workload_id in sorted(current_by_id):
        current_workload = current_by_id[workload_id]
        baseline_workload = baseline_by_id[workload_id]
        for metric in LATENCY_METRICS:
            if metric in candidate_metrics_by_id.get(workload_id, set()):
                check = validate_confirmed_latency_check(
                    workload_id,
                    metric,
                    current_workload,
                    baseline_workload,
                    confirmation_by_id[workload_id],
                    contract,
                )
            else:
                check = {
                    **historical_latency_check(
                        workload_id,
                        metric,
                        current_workload,
                        baseline_workload,
                        contract,
                    ),
                    "confirmationRequired": False,
                }
            checks.append(check)
            if check["passed"]:
                continue
            if metric == "normalizedP99":
                raise PerformanceEvidenceError(
                    f"Historical budget failed for '{workload_id}' normalizedP99: "
                    f"{check['exceedanceCount']} of {check['sampleCount']} samples "
                    f"exceeded {check['maximum']}; p-value {check['pValue']} is "
                    f"below {check['significanceLevel']}."
                )
            raise PerformanceEvidenceError(
                f"Historical budget failed for '{workload_id}' {metric}: "
                f"{check['actual']} > {check['maximum']} from baseline "
                f"{check['baseline']}."
            )

    return checks
