#!/usr/bin/env python3
"""Validation for BenchmarkDotNet reports and current-run controls."""

import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Sequence

if __package__:
    from .contract import (
        PerformanceEvidenceError,
        finite_number,
        load_json,
        required_positive_integer,
        required_string,
        sha256,
    )
else:
    from contract import (
        PerformanceEvidenceError,
        finite_number,
        load_json,
        required_positive_integer,
        required_string,
        sha256,
    )


def validate_bdn_reports(
    contract: dict[str, Any],
    reports_directory: Path,
    *,
    run_id: str,
    target: str,
    profile: str,
) -> dict[str, Any]:
    """Validate all current-run BenchmarkDotNet reports and same-run controls."""
    resolved_reports_directory = reports_directory.resolve()
    if (
        resolved_reports_directory.name != run_id
        or resolved_reports_directory.parent.name != "reports"
    ):
        raise PerformanceEvidenceError(
            f"Report directory '{reports_directory}' is not scoped to current run '{run_id}'."
        )

    report_paths = sorted(reports_directory.glob("results/*-report-full.json"))
    if not report_paths:
        raise PerformanceEvidenceError(
            f"No BenchmarkDotNet full JSON reports exist under '{reports_directory}'."
        )

    benchmark_entries: list[dict[str, Any]] = []
    host_fingerprints: set[str] = set()
    raw_reports: list[dict[str, str]] = []
    minimum_samples = int(contract["profiles"][profile]["minimumBenchmarkDotNetSamples"])
    host_environment: dict[str, Any] | None = None

    for report_path in report_paths:
        report = load_json(report_path)
        host = report.get("HostEnvironmentInfo")
        if not isinstance(host, dict):
            raise PerformanceEvidenceError(f"BDN report '{report_path}' has no host environment.")
        for key in (
            "BenchmarkDotNetVersion",
            "OsVersion",
            "ProcessorName",
            "RuntimeVersion",
            "Architecture",
            "Configuration",
        ):
            required_string(host, key, f"BDN report '{report_path}'.HostEnvironmentInfo")
        required_positive_integer(
            host,
            "LogicalCoreCount",
            f"BDN report '{report_path}'.HostEnvironmentInfo",
        )

        fingerprint = json.dumps(host, sort_keys=True, separators=(",", ":"))
        host_fingerprints.add(hashlib.sha256(fingerprint.encode("utf-8")).hexdigest())
        host_environment = host

        entries = report.get("Benchmarks")
        if not isinstance(entries, list) or not entries:
            raise PerformanceEvidenceError(f"BDN report '{report_path}' contains no benchmarks.")

        for entry in entries:
            if not isinstance(entry, dict):
                raise PerformanceEvidenceError(f"BDN report '{report_path}' has a non-object benchmark.")
            validate_bdn_entry(entry, report_path, minimum_samples)
            benchmark_entries.append(entry)

        raw_reports.append(
            {
                "path": report_path.relative_to(reports_directory).as_posix(),
                "sha256": sha256(report_path),
            }
        )

    if len(host_fingerprints) != 1:
        raise PerformanceEvidenceError("Current-run BDN reports contain different host environments.")

    controls = [
        evaluate_bdn_control(control, benchmark_entries)
        for control in contract["benchmarkDotNetControls"]
    ]

    return {
        "schemaVersion": 2,
        "kind": "benchmarkdotnet-controls",
        "contractVersion": contract["contractVersion"],
        "runId": run_id,
        "target": target,
        "profile": profile,
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "success": True,
        "hostEnvironment": host_environment,
        "rawReports": raw_reports,
        "controls": controls,
    }


def validate_bdn_entry(
    entry: dict[str, Any],
    report_path: Path,
    minimum_samples: int,
) -> None:
    """Reject failed, incomplete, non-finite, or statistically empty BDN entries."""
    type_name = required_string(entry, "Type", f"BDN report '{report_path}' benchmark")
    method = required_string(entry, "Method", f"BDN report '{report_path}' benchmark")
    label = f"{type_name}.{method}"
    stats = entry.get("Statistics")
    memory = entry.get("Memory")
    if not isinstance(stats, dict):
        raise PerformanceEvidenceError(f"BDN benchmark '{label}' has no statistics.")
    if not isinstance(memory, dict):
        raise PerformanceEvidenceError(f"BDN benchmark '{label}' has no memory evidence.")

    original_values = stats.get("OriginalValues")
    if not isinstance(original_values, list) or len(original_values) < minimum_samples:
        raise PerformanceEvidenceError(
            f"BDN benchmark '{label}' has fewer than {minimum_samples} valid samples."
        )
    for index, value in enumerate(original_values):
        finite_number(value, f"{label}.Statistics.OriginalValues[{index}]", minimum=0.000001)

    for key in ("N", "Mean", "Median", "StandardError"):
        finite_number(stats.get(key), f"{label}.Statistics.{key}", minimum=0)
    percentiles = stats.get("Percentiles")
    if not isinstance(percentiles, dict):
        raise PerformanceEvidenceError(f"BDN benchmark '{label}' has no percentiles.")
    finite_number(percentiles.get("P95"), f"{label}.Statistics.Percentiles.P95", minimum=0)

    for key in (
        "Gen0Collections",
        "Gen1Collections",
        "Gen2Collections",
        "TotalOperations",
        "BytesAllocatedPerOperation",
    ):
        minimum = 1 if key == "TotalOperations" else 0
        finite_number(memory.get(key), f"{label}.Memory.{key}", minimum=minimum)


def evaluate_bdn_control(
    control: dict[str, Any],
    entries: Sequence[dict[str, Any]],
) -> dict[str, Any]:
    """Evaluate one relative or absolute current-run BDN control."""
    control_id = required_string(control, "id", "benchmarkDotNetControl")
    type_name = required_string(control, "type", f"benchmarkDotNetControl.{control_id}")
    method = required_string(control, "method", f"benchmarkDotNetControl.{control_id}")
    metric = required_string(control, "metric", f"benchmarkDotNetControl.{control_id}")
    maximum = finite_number(
        control.get("maximum"),
        f"benchmarkDotNetControl.{control_id}.maximum",
        minimum=0,
    )
    method_entries = [
        entry
        for entry in entries
        if entry.get("Type") == type_name and entry.get("Method") == method
    ]
    if len(method_entries) != 1:
        raise PerformanceEvidenceError(
            f"BDN control '{control_id}' requires exactly one {type_name}.{method}, "
            f"found {len(method_entries)}."
        )

    gated = method_entries[0]
    if metric == "allocatedBytes":
        actual = finite_number(
            gated["Memory"].get("BytesAllocatedPerOperation"),
            f"{control_id}.allocatedBytes",
            minimum=0,
        )
    else:
        baseline_method = required_string(
            control,
            "baselineMethod",
            f"benchmarkDotNetControl.{control_id}",
        )
        baseline_entries = [
            entry
            for entry in entries
            if entry.get("Type") == type_name and entry.get("Method") == baseline_method
        ]
        if len(baseline_entries) != 1:
            raise PerformanceEvidenceError(
                f"BDN control '{control_id}' requires exactly one "
                f"{type_name}.{baseline_method}, found {len(baseline_entries)}."
            )
        baseline = baseline_entries[0]
        if metric == "meanRatio":
            numerator = finite_number(gated["Statistics"].get("Mean"), f"{control_id}.mean", minimum=0)
            denominator = finite_number(
                baseline["Statistics"].get("Mean"),
                f"{control_id}.baselineMean",
                minimum=0.000001,
            )
        elif metric == "allocationRatio":
            numerator = finite_number(
                gated["Memory"].get("BytesAllocatedPerOperation"),
                f"{control_id}.allocatedBytes",
                minimum=0,
            )
            denominator = finite_number(
                baseline["Memory"].get("BytesAllocatedPerOperation"),
                f"{control_id}.baselineAllocatedBytes",
                minimum=0.000001,
            )
        else:
            raise PerformanceEvidenceError(f"BDN control '{control_id}' has unknown metric '{metric}'.")
        actual = numerator / denominator

    if actual > maximum:
        raise PerformanceEvidenceError(
            f"BDN control '{control_id}' failed: {actual:.6f} > {maximum:.6f}."
        )

    return {
        "id": control_id,
        "metric": metric,
        "actual": actual,
        "maximum": maximum,
        "passed": True,
    }
