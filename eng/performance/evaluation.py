#!/usr/bin/env python3
"""Evaluate one performance-evidence run against its active contracts."""

import argparse
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Sequence

if __package__:
    from .confirmation import (
        load_matching_baseline,
        validate_historical_budgets,
    )
    from .contract import (
        PerformanceEvidenceError,
        finite_number,
        load_json,
        require_identity,
        required_string,
        sha256,
        validate_contract,
    )
    from .environment import (
        validate_bdn_workload_environment,
        validate_environment_compatibility,
        validate_host_workload_binding,
    )
    from .host import validate_host_preflight
    from .reports import (
        public_workload_metrics,
        validate_absolute_budgets,
        validate_workload_report,
    )
    from .soak import validate_soak_report
else:
    from confirmation import (
        load_matching_baseline,
        validate_historical_budgets,
    )
    from contract import (
        PerformanceEvidenceError,
        finite_number,
        load_json,
        require_identity,
        required_string,
        sha256,
        validate_contract,
    )
    from environment import (
        validate_bdn_workload_environment,
        validate_environment_compatibility,
        validate_host_workload_binding,
    )
    from host import validate_host_preflight
    from reports import (
        public_workload_metrics,
        validate_absolute_budgets,
        validate_workload_report,
    )
    from soak import validate_soak_report


def collect_gc_diagnostics(
    current: Sequence[dict[str, Any]],
    baseline_entry: dict[str, Any] | None,
    contract: dict[str, Any],
) -> list[dict[str, Any]]:
    """Compare GC policy outcomes without treating them as provider regressions."""
    diagnostics: list[dict[str, Any]] = []

    for workload in current:
        maximum = finite_number(
            contract["familyBudgets"][workload["family"]].get(
                "gen2CollectionsPer1000"
            ),
            f"familyBudgets.{workload['family']}.gen2CollectionsPer1000",
            minimum=0,
        )
        observed = finite_number(
            workload.get("gen2CollectionsPer1000"),
            f"current.{workload['id']}.gen2CollectionsPer1000",
            minimum=0,
        )
        diagnostics.append(
            {
                "workloadId": workload["id"],
                "metric": "gen2CollectionsPer1000",
                "referenceKind": "absolute",
                "observed": observed,
                "referenceMaximum": maximum,
                "withinReferenceRange": observed <= maximum,
            }
        )

    if baseline_entry is None:
        return diagnostics

    baseline_workloads = baseline_entry.get("workloads")
    if not isinstance(baseline_workloads, list):
        raise PerformanceEvidenceError("Accepted baseline entry has no workloads.")
    baseline_by_id = {
        workload.get("id"): workload
        for workload in baseline_workloads
        if isinstance(workload, dict) and isinstance(workload.get("id"), str)
    }
    policy = contract["historicalBudgets"]
    allowance = finite_number(
        policy.get("genCollectionAllowancePer1000"),
        "historicalBudgets.genCollectionAllowancePer1000",
        minimum=0,
    )
    metric_ratios = {
        "gen0CollectionsPer1000": "gen0Ratio",
        "gen1CollectionsPer1000": "gen1Ratio",
        "gen2CollectionsPer1000": "gen2Ratio",
    }

    for workload in current:
        workload_id = workload["id"]
        baseline_workload = baseline_by_id.get(workload_id)
        if not isinstance(baseline_workload, dict):
            raise PerformanceEvidenceError(
                f"Accepted baseline has no workload '{workload_id}'."
            )
        for metric, ratio_key in metric_ratios.items():
            observed = finite_number(
                workload.get(metric),
                f"current.{workload_id}.{metric}",
                minimum=0,
            )
            baseline_value = finite_number(
                baseline_workload.get(metric),
                f"baseline.{workload_id}.{metric}",
                minimum=0,
            )
            ratio = finite_number(
                policy.get(ratio_key),
                f"historicalBudgets.{ratio_key}",
                minimum=1,
            )
            maximum = max(
                baseline_value * ratio,
                baseline_value + allowance,
            )
            diagnostics.append(
                {
                    "workloadId": workload_id,
                    "metric": metric,
                    "referenceKind": "historical",
                    "baseline": baseline_value,
                    "observed": observed,
                    "referenceMaximum": maximum,
                    "withinReferenceRange": observed <= maximum,
                }
            )

    return diagnostics

def evaluate(
    args: argparse.Namespace,
) -> dict[str, Any]:
    """Evaluate one target's complete current-run evidence."""
    contract_path = Path(args.contract)
    workload_path = Path(args.workloads)
    bdn_path = Path(args.bdn)
    host_path = Path(args.host)
    soak_path = Path(args.soak) if args.soak else None
    baseline_path = Path(args.baseline)
    contract = load_json(contract_path)
    validate_contract(contract)

    host_preflight = validate_host_preflight(
        load_json(host_path),
        contract,
        maximum_age_hours=float(contract["evidenceMaximumAgeHours"]),
    )

    workload_report = load_json(workload_path)
    normalized_workloads = validate_workload_report(
        workload_report,
        contract,
        run_id=args.run_id,
        target=args.target,
        profile=args.profile,
    )
    validate_host_workload_binding(host_preflight, workload_report["environment"])
    absolute_checks = validate_absolute_budgets(normalized_workloads, contract)

    bdn = load_json(bdn_path)
    require_identity(
        bdn,
        label="bdnEvidence",
        run_id=args.run_id,
        target=args.target,
        profile=args.profile,
        contract_version=contract["contractVersion"],
    )
    if bdn.get("success") is not True:
        raise PerformanceEvidenceError("BenchmarkDotNet control evidence is not successful.")
    bdn_host_environment = bdn.get("hostEnvironment")
    if not isinstance(bdn_host_environment, dict):
        raise PerformanceEvidenceError("BenchmarkDotNet evidence has no host environment.")
    validate_bdn_workload_environment(
        bdn_host_environment,
        workload_report["environment"],
    )

    profile_contract = contract["profiles"][args.profile]
    normalized_soak: list[dict[str, Any]] = []
    soak_report = (
        load_json(soak_path)
        if soak_path is not None and soak_path.is_file()
        else None
    )
    if profile_contract["soakRequired"]:
        if soak_report is None:
            raise PerformanceEvidenceError(f"Profile '{args.profile}' requires a soak report.")
    if soak_report is not None:
        for key in ("commit", "sourceHash", "runnerClass"):
            if soak_report.get(key) != workload_report.get(key):
                raise PerformanceEvidenceError(
                    f"Soak and workload evidence disagree on '{key}'."
                )
        normalized_soak = validate_soak_report(
            soak_report,
            contract,
            run_id=args.run_id,
            target=args.target,
            profile=args.profile,
        )

    historical_checks: list[dict[str, Any]] = []
    baseline_entry: dict[str, Any] | None = None
    baseline_version: str | None = None
    if args.mode == "compare" and profile_contract["baselineRequired"]:
        baseline, matching_entry = load_matching_baseline(
            baseline_path,
            contract,
            target=args.target,
            profile=args.profile,
            runner_class=workload_report["runnerClass"],
        )
        baseline_version = required_string(baseline, "baselineVersion", "baseline")
        baseline_environment = matching_entry.get("environment")
        if not isinstance(baseline_environment, dict):
            raise PerformanceEvidenceError(
                "Accepted baseline entry has no environment evidence."
            )
        validate_environment_compatibility(
            workload_report["environment"],
            baseline_environment,
        )
        historical_checks = validate_historical_budgets(
            normalized_workloads,
            matching_entry,
            contract,
            workload_report.get("tailConfirmations"),
        )
        baseline_entry = matching_entry

    gc_diagnostics = collect_gc_diagnostics(
        normalized_workloads,
        baseline_entry,
        contract,
    )

    artifact_hashes = {
        "contract": sha256(contract_path),
        "hostPreflight": sha256(host_path),
        "workloads": sha256(workload_path),
        "benchmarkDotNet": sha256(bdn_path),
    }
    if soak_path is not None and soak_path.is_file():
        artifact_hashes["soak"] = sha256(soak_path)

    return {
        "schemaVersion": 3,
        "kind": "performance-evaluation",
        "contractVersion": contract["contractVersion"],
        "runId": args.run_id,
        "target": args.target,
        "profile": args.profile,
        "mode": args.mode,
        "success": True,
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "commit": workload_report["commit"],
        "sourceHash": workload_report["sourceHash"],
        "runnerClass": workload_report["runnerClass"],
        "environment": workload_report["environment"],
        "hostPreflight": host_preflight,
        "baselineVersion": baseline_version,
        "artifactHashes": artifact_hashes,
        "rawReports": bdn.get("rawReports"),
        "benchmarkDotNetHostEnvironment": bdn_host_environment,
        "benchmarkDotNetControls": bdn.get("controls"),
        "absoluteChecks": absolute_checks,
        "historicalChecks": historical_checks,
        "gcDiagnostics": gc_diagnostics,
        "tailConfirmations": workload_report.get("tailConfirmations"),
        "workloads": public_workload_metrics(normalized_workloads),
        "soakScenarios": normalized_soak,
    }
