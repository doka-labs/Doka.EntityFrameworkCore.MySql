#!/usr/bin/env python3
"""Validation for sustained-use performance scenarios."""

from typing import Any

if __package__:
    from .contract import (
        SOAK_SCENARIO_IDS,
        PerformanceEvidenceError,
        finite_number,
        require_identity,
        required_commit,
        required_current_timestamp,
        required_sha256,
        required_string,
    )
else:
    from contract import (
        SOAK_SCENARIO_IDS,
        PerformanceEvidenceError,
        finite_number,
        require_identity,
        required_commit,
        required_current_timestamp,
        required_sha256,
        required_string,
    )


def validate_soak_report(
    report: dict[str, Any],
    contract: dict[str, Any],
    *,
    run_id: str,
    target: str,
    profile: str,
) -> list[dict[str, Any]]:
    """Validate sustained-use scenario completeness and pass verdicts."""
    require_identity(
        report,
        label="soakReport",
        run_id=run_id,
        target=target,
        profile=profile,
        contract_version=contract["contractVersion"],
    )
    if report.get("schemaVersion") != 2 or report.get("kind") != "performance-soak":
        raise PerformanceEvidenceError("Soak report schema or kind is invalid.")
    required_commit(report, "commit", "soakReport")
    required_sha256(report, "sourceHash", "soakReport")
    required_string(report, "runnerClass", "soakReport")
    required_current_timestamp(
        report,
        "generatedUtc",
        "soakReport",
        float(contract["evidenceMaximumAgeHours"]),
    )

    scenarios = report.get("scenarios")
    if not isinstance(scenarios, list):
        raise PerformanceEvidenceError("soakReport.scenarios must be an array.")

    actual: dict[str, dict[str, Any]] = {}
    for index, scenario in enumerate(scenarios):
        if not isinstance(scenario, dict):
            raise PerformanceEvidenceError(f"soakReport.scenarios[{index}] must be an object.")
        scenario_id = required_string(scenario, "id", f"soakReport.scenarios[{index}]")
        if scenario_id in actual:
            raise PerformanceEvidenceError(f"Soak report contains duplicate '{scenario_id}'.")
        actual[scenario_id] = scenario

    missing = sorted(SOAK_SCENARIO_IDS - set(actual))
    unknown = sorted(set(actual) - SOAK_SCENARIO_IDS)
    if missing or unknown:
        raise PerformanceEvidenceError(
            f"Soak matrix drift. Missing: [{', '.join(missing)}]. "
            f"Unknown: [{', '.join(unknown)}]."
        )

    failures: list[str] = []
    normalized: list[dict[str, Any]] = []
    for scenario_id in sorted(actual):
        scenario = actual[scenario_id]
        metrics = scenario.get("metrics")
        budgets = scenario.get("budgets")
        if not isinstance(metrics, dict) or not metrics:
            raise PerformanceEvidenceError(f"Soak scenario '{scenario_id}' has no metrics.")
        if not isinstance(budgets, dict) or not budgets:
            raise PerformanceEvidenceError(f"Soak scenario '{scenario_id}' has no budgets.")

        normalized_metrics = {
            key: finite_number(value, f"{scenario_id}.metrics.{key}")
            for key, value in sorted(metrics.items())
        }
        normalized_budgets = {
            key: finite_number(value, f"{scenario_id}.budgets.{key}")
            for key, value in sorted(budgets.items())
        }
        success = validate_soak_scenario(
            scenario_id,
            normalized_metrics,
            normalized_budgets,
            contract,
        )
        if scenario.get("success") is not success:
            raise PerformanceEvidenceError(
                f"Soak scenario '{scenario_id}' reports success="
                f"{scenario.get('success')}, recomputed value is {success}."
            )
        if not success:
            failures.append(f"{scenario_id}: {scenario.get('error') or 'failed without an error message'}")
        normalized.append(
            {
                "id": scenario_id,
                "success": success,
                "metrics": normalized_metrics,
                "budgets": normalized_budgets,
            }
        )

    if report.get("success") is not True or failures:
        raise PerformanceEvidenceError("Soak gate failed: " + "; ".join(failures))

    return normalized


def validate_soak_scenario(
    scenario_id: str,
    metrics: dict[str, float],
    budgets: dict[str, float],
    contract: dict[str, Any],
) -> bool:
    """Recompute a soak verdict from raw metrics and the checked-in budget."""
    contract_budgets = contract["soakBudgets"]
    expected_budget_keys = {
        "soak.hilo-cache-bound": {"maximumCacheEntries"},
        "soak.pooled-buffer-return": {"maximumOutstandingBuffers"},
        "soak.connection-cleanup": {"maximumConnectionDelta"},
        "soak.migration-lock-cleanup": {"maximumHeldLocks"},
        "soak.working-set-stabilization": {
            "maximumWorkingSetGrowthBytes",
            "maximumManagedHeapGrowthBytes",
        },
        "soak.concurrent-throughput-retention": {
            "minimumThroughputRetentionRatio",
        },
    }
    if scenario_id not in expected_budget_keys:
        raise PerformanceEvidenceError(f"Unknown soak scenario '{scenario_id}'.")
    require_exact_keys(
        budgets,
        expected_budget_keys[scenario_id],
        scenario_id,
        "budgets",
    )

    if scenario_id == "soak.hilo-cache-bound":
        require_exact_keys(metrics, {"cacheEntries"}, scenario_id, "metrics")
        maximum = soak_budget(
            budgets,
            "maximumCacheEntries",
            contract_budgets,
            "hiloCacheMaximumEntries",
            scenario_id,
        )
        return metrics["cacheEntries"] <= maximum

    if scenario_id == "soak.pooled-buffer-return":
        require_exact_keys(
            metrics,
            {"rentCount", "returnCount", "outstandingBuffers"},
            scenario_id,
            "metrics",
        )
        maximum = soak_budget(
            budgets,
            "maximumOutstandingBuffers",
            contract_budgets,
            "pooledBufferMaximumOutstanding",
            scenario_id,
        )
        return (
            metrics["outstandingBuffers"] <= maximum
            and metrics["rentCount"] == metrics["returnCount"]
        )

    if scenario_id == "soak.connection-cleanup":
        require_exact_keys(
            metrics,
            {"threadsConnectedBefore", "threadsConnectedAfter", "connectionDelta"},
            scenario_id,
            "metrics",
        )
        maximum = soak_budget(
            budgets,
            "maximumConnectionDelta",
            contract_budgets,
            "connectionMaximumDelta",
            scenario_id,
        )
        return metrics["connectionDelta"] <= maximum

    if scenario_id == "soak.migration-lock-cleanup":
        require_exact_keys(metrics, {"heldLocks"}, scenario_id, "metrics")
        maximum = soak_budget(
            budgets,
            "maximumHeldLocks",
            contract_budgets,
            "migrationLockMaximumHeld",
            scenario_id,
        )
        return metrics["heldLocks"] <= maximum

    if scenario_id == "soak.working-set-stabilization":
        require_exact_keys(
            metrics,
            {
                "workingSetFirstBytes",
                "workingSetLastBytes",
                "workingSetGrowthBytes",
                "managedHeapFirstBytes",
                "managedHeapLastBytes",
                "managedHeapGrowthBytes",
            },
            scenario_id,
            "metrics",
        )
        maximum_working_set = soak_budget(
            budgets,
            "maximumWorkingSetGrowthBytes",
            contract_budgets,
            "workingSetMaximumGrowthBytes",
            scenario_id,
        )
        maximum_managed_heap = soak_budget(
            budgets,
            "maximumManagedHeapGrowthBytes",
            contract_budgets,
            "managedHeapMaximumGrowthBytes",
            scenario_id,
        )
        return (
            metrics["workingSetGrowthBytes"] <= maximum_working_set
            and metrics["managedHeapGrowthBytes"] <= maximum_managed_heap
        )

    if scenario_id == "soak.concurrent-throughput-retention":
        require_exact_keys(
            metrics,
            {
                "initialOperationsPerSecond",
                "finalOperationsPerSecond",
                "throughputRetentionRatio",
            },
            scenario_id,
            "metrics",
        )
        minimum = soak_budget(
            budgets,
            "minimumThroughputRetentionRatio",
            contract_budgets,
            "minimumThroughputRetentionRatio",
            scenario_id,
        )
        return metrics["throughputRetentionRatio"] >= minimum

    raise PerformanceEvidenceError(f"Unhandled soak scenario '{scenario_id}'.")


def require_exact_keys(
    payload: dict[str, float],
    expected: set[str],
    scenario_id: str,
    field: str,
) -> None:
    """Reject missing or stale metrics and budget fields."""
    actual = set(payload)
    if actual != expected:
        missing = sorted(expected - actual)
        unknown = sorted(actual - expected)
        raise PerformanceEvidenceError(
            f"Soak scenario '{scenario_id}' {field} drift. "
            f"Missing: [{', '.join(missing)}]. Unknown: [{', '.join(unknown)}]."
        )


def soak_budget(
    reported_budgets: dict[str, float],
    report_key: str,
    contract_budgets: dict[str, Any],
    contract_key: str,
    scenario_id: str,
) -> float:
    """Require one report budget to equal its checked-in contract value."""
    expected = finite_number(
        contract_budgets.get(contract_key),
        f"soakBudgets.{contract_key}",
    )
    actual = reported_budgets[report_key]
    if actual != expected:
        raise PerformanceEvidenceError(
            f"Soak scenario '{scenario_id}' reports budget {report_key}={actual}, "
            f"expected {expected}."
        )

    return expected
