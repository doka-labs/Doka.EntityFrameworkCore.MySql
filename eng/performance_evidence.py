#!/usr/bin/env python3
"""Validate, compare, and seed performance and memory evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import statistics
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Sequence

BASELINE_PATH = Path("benchmarks/baselines/doka-benchmark-baseline.json")
SOAK_SCENARIO_IDS = {
    "soak.hilo-cache-bound",
    "soak.pooled-buffer-return",
    "soak.connection-cleanup",
    "soak.migration-lock-cleanup",
    "soak.working-set-stabilization",
    "soak.concurrent-throughput-retention",
}
COMPARABLE_ENVIRONMENT_FIELDS = (
    "frameworkDescription",
    "osDescription",
    "osArchitecture",
    "processArchitecture",
    "processor",
    "processorCount",
    "engineFamily",
    "serverVersion",
    "serverImage",
)


class PerformanceEvidenceError(RuntimeError):
    """Raised when performance evidence violates the versioned contract."""


def load_json(path: Path) -> dict[str, Any]:
    """Load one JSON object and reject missing, malformed, or non-object input."""
    try:
        with path.open(encoding="utf-8") as stream:
            payload = json.load(stream)
    except (OSError, json.JSONDecodeError) as error:
        raise PerformanceEvidenceError(f"Unable to read JSON '{path}': {error}") from error

    if not isinstance(payload, dict):
        raise PerformanceEvidenceError(f"JSON '{path}' must contain an object.")

    return payload


def write_json(path: Path, payload: dict[str, Any]) -> None:
    """Write canonical reviewable JSON with a trailing newline."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, indent=2, sort_keys=True, allow_nan=False) + "\n",
        encoding="utf-8",
    )


def sha256(path: Path) -> str:
    """Return the SHA-256 digest of one evidence file."""
    digest = hashlib.sha256()

    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)

    return digest.hexdigest()


def repository_source_hash(repository: Path) -> str:
    """Hash HEAD plus the measured working tree while excluding the baseline output."""
    repository = repository.resolve()

    def git(*arguments: str) -> bytes:
        try:
            result = subprocess.run(
                ["git", "-C", str(repository), *arguments],
                check=True,
                capture_output=True,
            )
        except (OSError, subprocess.CalledProcessError) as error:
            detail = (
                error.stderr.decode("utf-8", errors="replace").strip()
                if isinstance(error, subprocess.CalledProcessError)
                else str(error)
            )
            raise PerformanceEvidenceError(
                f"Unable to inspect benchmark source identity in '{repository}': {detail}"
            ) from error

        return result.stdout

    head = git("rev-parse", "--verify", "HEAD").strip()
    tracked_patch = git(
        "diff",
        "--binary",
        "HEAD",
        "--",
        ".",
        f":(exclude){BASELINE_PATH.as_posix()}",
    )
    untracked_payload = git("ls-files", "--others", "--exclude-standard", "-z")
    untracked_paths = sorted(
        Path(raw_path.decode("utf-8"))
        for raw_path in untracked_payload.split(b"\0")
        if raw_path
        and Path(raw_path.decode("utf-8")) != BASELINE_PATH
    )

    digest = hashlib.sha256()
    digest.update(b"doka-performance-source-v1\0")
    digest.update(head)
    digest.update(b"\0tracked-patch\0")
    digest.update(tracked_patch)

    for relative_path in untracked_paths:
        absolute_path = (repository / relative_path).resolve()
        try:
            absolute_path.relative_to(repository)
        except ValueError as error:
            raise PerformanceEvidenceError(
                f"Untracked source path '{relative_path}' escapes the repository."
            ) from error
        if not absolute_path.is_file():
            continue

        digest.update(b"\0untracked-file\0")
        digest.update(relative_path.as_posix().encode("utf-8"))
        digest.update(b"\0")
        with absolute_path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)

    return digest.hexdigest()


def finite_number(value: Any, label: str, *, minimum: float | None = None) -> float:
    """Return a finite number or raise a contract error with its field label."""
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise PerformanceEvidenceError(f"{label} must be a number.")

    result = float(value)
    if not math.isfinite(result):
        raise PerformanceEvidenceError(f"{label} must be finite.")
    if minimum is not None and result < minimum:
        raise PerformanceEvidenceError(f"{label} must be >= {minimum}.")

    return result


def required_string(payload: dict[str, Any], key: str, label: str) -> str:
    """Read a non-empty string field."""
    value = payload.get(key)
    if not isinstance(value, str) or not value.strip():
        raise PerformanceEvidenceError(f"{label}.{key} must be a non-empty string.")

    return value


def required_positive_integer(payload: dict[str, Any], key: str, label: str) -> int:
    """Read a strictly positive integer field."""
    value = payload.get(key)
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise PerformanceEvidenceError(f"{label}.{key} must be a positive integer.")

    return value


def required_sha256(payload: dict[str, Any], key: str, label: str) -> str:
    """Read a lower-case SHA-256 digest field."""
    value = required_string(payload, key, label)
    if len(value) != 64 or any(character not in "0123456789abcdef" for character in value):
        raise PerformanceEvidenceError(f"{label}.{key} must be a lower-case SHA-256 digest.")

    return value


def required_commit(payload: dict[str, Any], key: str, label: str) -> str:
    """Read a full lower-case Git object identifier."""
    value = required_string(payload, key, label)
    if (
        len(value) not in (40, 64)
        or any(character not in "0123456789abcdef" for character in value)
    ):
        raise PerformanceEvidenceError(
            f"{label}.{key} must be a full lower-case Git object identifier."
        )

    return value


def required_current_timestamp(
    payload: dict[str, Any],
    key: str,
    label: str,
    maximum_age_hours: float | None,
) -> datetime:
    """Read a UTC timestamp and reject future or stale evidence."""
    value = required_string(payload, key, label)
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise PerformanceEvidenceError(
            f"{label}.{key} must be an ISO-8601 timestamp."
        ) from error
    if parsed.tzinfo is None:
        raise PerformanceEvidenceError(f"{label}.{key} must include a timezone.")

    current = datetime.now(timezone.utc)
    normalized = parsed.astimezone(timezone.utc)
    if normalized > current + timedelta(minutes=5):
        raise PerformanceEvidenceError(f"{label}.{key} is in the future.")
    if (
        maximum_age_hours is not None
        and normalized < current - timedelta(hours=maximum_age_hours)
    ):
        raise PerformanceEvidenceError(
            f"{label}.{key} is older than {maximum_age_hours:g} hours."
        )

    return normalized


def require_identity(
    payload: dict[str, Any],
    *,
    label: str,
    run_id: str,
    target: str,
    profile: str,
    contract_version: str,
) -> None:
    """Assert that one artifact belongs to the exact current execution."""
    expected = {
        "runId": run_id,
        "target": target,
        "profile": profile,
        "contractVersion": contract_version,
    }

    for key, expected_value in expected.items():
        actual = payload.get(key)
        if actual != expected_value:
            raise PerformanceEvidenceError(
                f"{label}.{key} is '{actual}', expected current-run value '{expected_value}'."
            )


def validate_contract(contract: dict[str, Any]) -> None:
    """Validate uniqueness, references, and required dimension coverage."""
    if contract.get("schemaVersion") != 2:
        raise PerformanceEvidenceError("Performance contract schemaVersion must be 2.")

    required_string(contract, "contractVersion", "contract")
    finite_number(
        contract.get("evidenceMaximumAgeHours"),
        "contract.evidenceMaximumAgeHours",
        minimum=1,
    )

    targets = contract.get("requiredTargets")
    profiles = contract.get("profiles")
    families = contract.get("familyBudgets")
    workloads = contract.get("workloads")
    requirements = contract.get("coverageRequirements")
    historical_budgets = contract.get("historicalBudgets")
    benchmark_controls = contract.get("benchmarkDotNetControls")
    soak_budgets = contract.get("soakBudgets")

    if not isinstance(targets, dict) or not targets:
        raise PerformanceEvidenceError("Performance contract must define requiredTargets.")
    if not isinstance(profiles, dict) or not profiles:
        raise PerformanceEvidenceError("Performance contract must define profiles.")
    if not isinstance(families, dict) or not families:
        raise PerformanceEvidenceError("Performance contract must define familyBudgets.")
    if not isinstance(workloads, list) or not workloads:
        raise PerformanceEvidenceError("Performance contract must define workloads.")
    if not isinstance(requirements, dict) or not requirements:
        raise PerformanceEvidenceError("Performance contract must define coverageRequirements.")
    if not isinstance(historical_budgets, dict):
        raise PerformanceEvidenceError("Performance contract must define historicalBudgets.")
    if not isinstance(benchmark_controls, list) or not benchmark_controls:
        raise PerformanceEvidenceError("Performance contract must define benchmarkDotNetControls.")
    if not isinstance(soak_budgets, dict):
        raise PerformanceEvidenceError("Performance contract must define soakBudgets.")

    for target_id, target_contract in targets.items():
        if not isinstance(target_contract, dict):
            raise PerformanceEvidenceError(
                f"requiredTargets.{target_id} must be an object."
            )
        for key in ("engineFamily", "serverVersion", "serverImage"):
            required_string(target_contract, key, f"requiredTargets.{target_id}")

    required_profile_fields = (
        "warmupSamples",
        "measurementSamples",
        "expensiveMeasurementSamples",
        "minimumValidSamples",
        "minimumBenchmarkDotNetSamples",
        "soakIterations",
        "soakConcurrency",
    )
    for profile_name, profile_contract in profiles.items():
        if not isinstance(profile_contract, dict):
            raise PerformanceEvidenceError(
                f"profiles.{profile_name} must be an object."
            )
        for key in required_profile_fields:
            value = finite_number(
                profile_contract.get(key),
                f"profiles.{profile_name}.{key}",
                minimum=1,
            )
            if not value.is_integer():
                raise PerformanceEvidenceError(
                    f"profiles.{profile_name}.{key} must be an integer."
                )
        finite_number(
            profile_contract.get("maximumRelativeStandardError"),
            f"profiles.{profile_name}.maximumRelativeStandardError",
            minimum=0,
        )
        for key in ("baselineRequired", "soakRequired"):
            if not isinstance(profile_contract.get(key), bool):
                raise PerformanceEvidenceError(
                    f"profiles.{profile_name}.{key} must be a boolean."
                )
        if profile_contract["minimumValidSamples"] > min(
            profile_contract["measurementSamples"],
            profile_contract["expensiveMeasurementSamples"],
        ):
            raise PerformanceEvidenceError(
                f"profiles.{profile_name}.minimumValidSamples exceeds a sample count."
            )

    family_budget_fields = (
        "medianNanoseconds",
        "p95Nanoseconds",
        "p99Nanoseconds",
        "allocatedBytes",
        "retainedBytes",
        "gen2CollectionsPer1000",
    )
    for family_name, family_budget in families.items():
        if not isinstance(family_budget, dict):
            raise PerformanceEvidenceError(
                f"familyBudgets.{family_name} must be an object."
            )
        for key in family_budget_fields:
            finite_number(
                family_budget.get(key),
                f"familyBudgets.{family_name}.{key}",
                minimum=0,
            )
        if not (
            family_budget["medianNanoseconds"]
            <= family_budget["p95Nanoseconds"]
            <= family_budget["p99Nanoseconds"]
        ):
            raise PerformanceEvidenceError(
                f"familyBudgets.{family_name} latency ceilings are not monotonic."
            )

    ratio_fields = (
        "medianRatio",
        "p95Ratio",
        "p99Ratio",
        "allocatedBytesRatio",
        "retainedBytesRatio",
        "gen0Ratio",
        "gen1Ratio",
        "gen2Ratio",
    )
    for key in ratio_fields:
        finite_number(
            historical_budgets.get(key),
            f"historicalBudgets.{key}",
            minimum=1,
        )
    for key in ("retainedBytesAllowance", "genCollectionAllowancePer1000"):
        finite_number(
            historical_budgets.get(key),
            f"historicalBudgets.{key}",
            minimum=0,
        )

    workload_ids: list[str] = []
    for index, workload in enumerate(workloads):
        if not isinstance(workload, dict):
            raise PerformanceEvidenceError(f"contract.workloads[{index}] must be an object.")
        workload_id = required_string(workload, "id", f"contract.workloads[{index}]")
        family = required_string(workload, "family", f"contract.workloads[{index}]")
        if family not in families:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' references unknown budget family '{family}'."
            )
        if workload.get("cost", "standard") not in ("standard", "expensive"):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' has an unknown cost class."
            )
        if "smoke" in workload and not isinstance(workload["smoke"], bool):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' smoke must be a boolean."
            )
        operations_per_sample = finite_number(
            workload.get("operationsPerSample", 1),
            f"Workload '{workload_id}'.operationsPerSample",
            minimum=1,
        )
        if not operations_per_sample.is_integer():
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' operationsPerSample must be an integer."
            )
        workload_ids.append(workload_id)

    duplicates = sorted(
        workload_id
        for workload_id in set(workload_ids)
        if workload_ids.count(workload_id) > 1
    )
    if duplicates:
        raise PerformanceEvidenceError(
            f"Performance contract contains duplicate workload IDs: {', '.join(duplicates)}."
        )

    for dimension, tokens in requirements.items():
        if not isinstance(tokens, list) or not tokens:
            raise PerformanceEvidenceError(
                f"coverageRequirements.{dimension} must contain at least one token."
            )
        for token in tokens:
            if not isinstance(token, str) or not token:
                raise PerformanceEvidenceError(
                    f"coverageRequirements.{dimension} contains an invalid token."
                )
            if not any(token in workload_id for workload_id in workload_ids):
                raise PerformanceEvidenceError(
                    f"Coverage token '{token}' in dimension '{dimension}' has no workload consumer."
                )

    control_ids: set[str] = set()
    for index, control in enumerate(benchmark_controls):
        if not isinstance(control, dict):
            raise PerformanceEvidenceError(
                f"benchmarkDotNetControls[{index}] must be an object."
            )
        label = f"benchmarkDotNetControls[{index}]"
        control_id = required_string(control, "id", label)
        if control_id in control_ids:
            raise PerformanceEvidenceError(
                f"Performance contract contains duplicate control '{control_id}'."
            )
        control_ids.add(control_id)
        required_string(control, "type", label)
        required_string(control, "method", label)
        metric = required_string(control, "metric", label)
        if metric not in ("meanRatio", "allocationRatio", "allocatedBytes"):
            raise PerformanceEvidenceError(
                f"BenchmarkDotNet control '{control_id}' has unknown metric '{metric}'."
            )
        if metric != "allocatedBytes":
            required_string(control, "baselineMethod", label)
        finite_number(control.get("maximum"), f"{label}.maximum", minimum=0)

    for key in (
        "hiloCacheMaximumEntries",
        "pooledBufferMaximumOutstanding",
        "connectionMaximumDelta",
        "migrationLockMaximumHeld",
        "workingSetMaximumGrowthBytes",
        "managedHeapMaximumGrowthBytes",
        "minimumThroughputRetentionRatio",
    ):
        finite_number(soak_budgets.get(key), f"soakBudgets.{key}", minimum=0)


def applicable_workloads(contract: dict[str, Any], profile: str) -> list[dict[str, Any]]:
    """Return the exact workload set required for one profile."""
    profiles = contract["profiles"]
    if profile not in profiles:
        raise PerformanceEvidenceError(f"Unknown performance profile '{profile}'.")

    workloads = contract["workloads"]
    if profile == "smoke":
        workloads = [workload for workload in workloads if workload.get("smoke") is True]

    return sorted(workloads, key=lambda workload: workload["id"])


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


def close_enough(actual: float, expected: float) -> bool:
    """Compare derived floating-point evidence with a tight serialization tolerance."""
    return math.isclose(actual, expected, rel_tol=1e-9, abs_tol=1e-6)


def validate_workload_report(
    report: dict[str, Any],
    contract: dict[str, Any],
    *,
    run_id: str,
    target: str,
    profile: str,
) -> list[dict[str, Any]]:
    """Validate matrix completeness and recompute every persisted statistic."""
    contract_version = contract["contractVersion"]
    require_identity(
        report,
        label="workloadReport",
        run_id=run_id,
        target=target,
        profile=profile,
        contract_version=contract_version,
    )
    if report.get("schemaVersion") != 2 or report.get("kind") != "performance-workloads":
        raise PerformanceEvidenceError("Workload report schema or kind is invalid.")

    required_commit(report, "commit", "workloadReport")
    required_sha256(report, "sourceHash", "workloadReport")
    required_string(report, "runnerClass", "workloadReport")
    required_current_timestamp(
        report,
        "generatedUtc",
        "workloadReport",
        float(contract["evidenceMaximumAgeHours"]),
    )
    finite_number(report.get("stopwatchFrequency"), "workloadReport.stopwatchFrequency", minimum=1)

    environment = report.get("environment")
    if not isinstance(environment, dict):
        raise PerformanceEvidenceError("workloadReport.environment must be an object.")
    for key in (
        "frameworkDescription",
        "osDescription",
        "osArchitecture",
        "processArchitecture",
        "processor",
        "engineFamily",
        "serverVersion",
        "serverImage",
    ):
        required_string(environment, key, "workloadReport.environment")
    required_positive_integer(
        environment,
        "processorCount",
        "workloadReport.environment",
    )
    target_contract = contract["requiredTargets"][target]
    expected_family = target_contract["engineFamily"]
    if environment["engineFamily"] != expected_family:
        raise PerformanceEvidenceError(
            f"workloadReport.environment.engineFamily is "
            f"'{environment['engineFamily']}', expected '{expected_family}'."
        )
    expected_version_parts = target_contract["serverVersion"].split(".")
    expected_version_prefix = ".".join(expected_version_parts[:2]) + "."
    observed_version = environment["serverVersion"]
    if not observed_version.startswith(expected_version_prefix):
        raise PerformanceEvidenceError(
            f"workloadReport.environment.serverVersion is '{observed_version}', "
            f"expected prefix '{expected_version_prefix}'."
        )
    observed_mariadb = "mariadb" in observed_version.lower()
    if observed_mariadb != (expected_family == "MariaDB"):
        raise PerformanceEvidenceError(
            "workloadReport.environment.serverVersion does not identify the "
            f"expected engine family '{expected_family}'."
        )

    expected_image = target_contract["serverImage"]
    if environment["serverImage"] != expected_image:
        raise PerformanceEvidenceError(
            f"workloadReport.environment.serverImage is "
            f"'{environment['serverImage']}', expected '{expected_image}'."
        )

    entries = report.get("workloads")
    if not isinstance(entries, list):
        raise PerformanceEvidenceError("workloadReport.workloads must be an array.")

    expected_definitions = applicable_workloads(contract, profile)
    expected_by_id = {
        definition["id"]: definition
        for definition in expected_definitions
    }
    actual_by_id: dict[str, dict[str, Any]] = {}

    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise PerformanceEvidenceError(f"workloadReport.workloads[{index}] must be an object.")
        workload_id = required_string(entry, "id", f"workloadReport.workloads[{index}]")
        if workload_id in actual_by_id:
            raise PerformanceEvidenceError(f"Workload report contains duplicate '{workload_id}'.")
        actual_by_id[workload_id] = entry

    missing = sorted(set(expected_by_id) - set(actual_by_id))
    unknown = sorted(set(actual_by_id) - set(expected_by_id))
    if missing or unknown:
        raise PerformanceEvidenceError(
            f"Workload matrix drift. Missing: [{', '.join(missing)}]. "
            f"Unknown: [{', '.join(unknown)}]."
        )

    profile_contract = contract["profiles"][profile]
    normalized: list[dict[str, Any]] = []

    for workload_id in sorted(actual_by_id):
        entry = actual_by_id[workload_id]
        definition = expected_by_id[workload_id]
        if entry.get("family") != definition["family"]:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' reports family '{entry.get('family')}', "
                f"expected '{definition['family']}'."
            )

        warmup_samples = required_positive_integer(
            entry,
            "warmupSamples",
            workload_id,
        )
        expected_warmup_samples = int(profile_contract["warmupSamples"])
        if warmup_samples != expected_warmup_samples:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' has {warmup_samples} warmups, "
                f"expected {expected_warmup_samples}."
            )

        sample_count = required_positive_integer(entry, "sampleCount", workload_id)
        sample_count_field = (
            "expensiveMeasurementSamples"
            if definition.get("cost", "standard") == "expensive"
            else "measurementSamples"
        )
        expected_sample_count = int(profile_contract[sample_count_field])
        if sample_count != expected_sample_count:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' has {sample_count} samples, "
                f"expected {expected_sample_count}."
            )

        operations_per_sample = required_positive_integer(
            entry,
            "operationsPerSample",
            workload_id,
        )
        expected_operations_per_sample = int(
            definition.get("operationsPerSample", 1)
        )
        if operations_per_sample != expected_operations_per_sample:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' reports operationsPerSample="
                f"{operations_per_sample}, expected {expected_operations_per_sample}."
            )

        samples_payload = entry.get("samplesNanoseconds")
        if not isinstance(samples_payload, list) or len(samples_payload) != sample_count:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' sample array does not match sampleCount."
            )
        samples = [
            finite_number(sample, f"{workload_id}.samplesNanoseconds[{index}]", minimum=0.000001)
            for index, sample in enumerate(samples_payload)
        ]
        sorted_samples = sorted(samples)
        derived = {
            "medianNanoseconds": percentile(sorted_samples, 0.5),
            "p95Nanoseconds": percentile(sorted_samples, 0.95),
            "p99Nanoseconds": percentile(sorted_samples, 0.99),
            "standardErrorNanoseconds": standard_error(samples),
        }
        for key, expected_value in derived.items():
            actual_value = finite_number(entry.get(key), f"{workload_id}.{key}", minimum=0)
            if not close_enough(actual_value, expected_value):
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' reports {key}={actual_value}, "
                    f"recomputed value is {expected_value}."
                )

        relative_standard_error = (
            derived["standardErrorNanoseconds"] / derived["medianNanoseconds"]
        )
        maximum_relative_standard_error = finite_number(
            profile_contract.get("maximumRelativeStandardError"),
            f"profiles.{profile}.maximumRelativeStandardError",
            minimum=0,
        )
        if relative_standard_error > maximum_relative_standard_error:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' has relative standard error "
                f"{relative_standard_error:.6f}, maximum is "
                f"{maximum_relative_standard_error:.6f}."
            )

        metrics: dict[str, float] = {}
        for key in (
            "allocatedBytesPerOperation",
            "retainedBytes",
            "gen0CollectionsPer1000",
            "gen1CollectionsPer1000",
            "gen2CollectionsPer1000",
        ):
            metrics[key] = finite_number(entry.get(key), f"{workload_id}.{key}", minimum=0)

        normalized.append(
            {
                "id": workload_id,
                "family": definition["family"],
                "sampleCount": sample_count,
                "operationsPerSample": operations_per_sample,
                "relativeStandardError": relative_standard_error,
                **derived,
                **metrics,
            }
        )

    return normalized


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


def validate_absolute_budgets(
    workloads: Sequence[dict[str, Any]],
    contract: dict[str, Any],
) -> list[dict[str, Any]]:
    """Evaluate every workload against its family's absolute ceilings."""
    checks: list[dict[str, Any]] = []
    metric_map = {
        "medianNanoseconds": "medianNanoseconds",
        "p95Nanoseconds": "p95Nanoseconds",
        "p99Nanoseconds": "p99Nanoseconds",
        "allocatedBytes": "allocatedBytesPerOperation",
        "retainedBytes": "retainedBytes",
        "gen2CollectionsPer1000": "gen2CollectionsPer1000",
    }

    for workload in workloads:
        family_budget = contract["familyBudgets"][workload["family"]]
        for budget_name, metric_name in metric_map.items():
            maximum = finite_number(
                family_budget.get(budget_name),
                f"familyBudgets.{workload['family']}.{budget_name}",
                minimum=0,
            )
            actual = finite_number(
                workload.get(metric_name),
                f"{workload['id']}.{metric_name}",
                minimum=0,
            )
            passed = actual <= maximum
            checks.append(
                {
                    "workloadId": workload["id"],
                    "metric": metric_name,
                    "actual": actual,
                    "maximum": maximum,
                    "passed": passed,
                }
            )
            if not passed:
                raise PerformanceEvidenceError(
                    f"Absolute budget failed for '{workload['id']}' {metric_name}: "
                    f"{actual} > {maximum}."
                )

    return checks


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
    if baseline.get("schemaVersion") != 2:
        raise PerformanceEvidenceError("Baseline schemaVersion must be 2.")
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


def validate_historical_budgets(
    current: Sequence[dict[str, Any]],
    baseline_entry: dict[str, Any],
    contract: dict[str, Any],
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
    metrics = {
        "medianNanoseconds": ("medianRatio", 0.0),
        "p95Nanoseconds": ("p95Ratio", 0.0),
        "p99Nanoseconds": ("p99Ratio", 0.0),
        "allocatedBytesPerOperation": ("allocatedBytesRatio", 0.0),
        "retainedBytes": ("retainedBytesRatio", float(policy["retainedBytesAllowance"])),
        "gen0CollectionsPer1000": ("gen0Ratio", float(policy["genCollectionAllowancePer1000"])),
        "gen1CollectionsPer1000": ("gen1Ratio", float(policy["genCollectionAllowancePer1000"])),
        "gen2CollectionsPer1000": ("gen2Ratio", float(policy["genCollectionAllowancePer1000"])),
    }
    checks: list[dict[str, Any]] = []

    for workload_id in sorted(current_by_id):
        current_workload = current_by_id[workload_id]
        baseline_workload = baseline_by_id[workload_id]
        for metric, (ratio_key, allowance) in metrics.items():
            current_value = finite_number(
                current_workload.get(metric),
                f"current.{workload_id}.{metric}",
                minimum=0,
            )
            baseline_value = finite_number(
                baseline_workload.get(metric),
                f"baseline.{workload_id}.{metric}",
                minimum=0,
            )
            ratio = finite_number(policy.get(ratio_key), f"historicalBudgets.{ratio_key}", minimum=1)
            maximum = max(baseline_value * ratio, baseline_value + allowance)
            passed = current_value <= maximum
            checks.append(
                {
                    "workloadId": workload_id,
                    "metric": metric,
                    "baseline": baseline_value,
                    "actual": current_value,
                    "maximum": maximum,
                    "passed": passed,
                }
            )
            if not passed:
                raise PerformanceEvidenceError(
                    f"Historical budget failed for '{workload_id}' {metric}: "
                    f"{current_value} > {maximum} from baseline {baseline_value}."
                )

    return checks


def validate_environment_compatibility(
    current: dict[str, Any],
    baseline: dict[str, Any],
) -> None:
    """Reject historical comparisons across different execution environments."""
    mismatches = [
        field
        for field in COMPARABLE_ENVIRONMENT_FIELDS
        if current.get(field) != baseline.get(field)
    ]
    if mismatches:
        raise PerformanceEvidenceError(
            "Historical baseline environment drift for field(s): "
            f"{', '.join(mismatches)}."
        )


def evaluate(
    args: argparse.Namespace,
) -> dict[str, Any]:
    """Evaluate one target's complete current-run evidence."""
    contract_path = Path(args.contract)
    workload_path = Path(args.workloads)
    bdn_path = Path(args.bdn)
    soak_path = Path(args.soak) if args.soak else None
    baseline_path = Path(args.baseline)
    contract = load_json(contract_path)
    validate_contract(contract)

    workload_report = load_json(workload_path)
    normalized_workloads = validate_workload_report(
        workload_report,
        contract,
        run_id=args.run_id,
        target=args.target,
        profile=args.profile,
    )
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
        )

    artifact_hashes = {
        "contract": sha256(contract_path),
        "workloads": sha256(workload_path),
        "benchmarkDotNet": sha256(bdn_path),
    }
    if soak_path is not None and soak_path.is_file():
        artifact_hashes["soak"] = sha256(soak_path)

    return {
        "schemaVersion": 2,
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
        "baselineVersion": baseline_version,
        "artifactHashes": artifact_hashes,
        "rawReports": bdn.get("rawReports"),
        "benchmarkDotNetControls": bdn.get("controls"),
        "absoluteChecks": absolute_checks,
        "historicalChecks": historical_checks,
        "workloads": normalized_workloads,
        "soakScenarios": normalized_soak,
    }


def validate_seed_evaluation(
    evaluation: dict[str, Any],
    contract: dict[str, Any],
    contract_path: Path,
    *,
    maximum_age_hours: float | None = None,
) -> None:
    """Reject a seed record that is incomplete or cannot reproduce its verdicts."""
    if evaluation.get("schemaVersion") != 2 or evaluation.get("kind") != "performance-evaluation":
        raise PerformanceEvidenceError("Seed input is not a performance evaluation.")
    if evaluation.get("success") is not True or evaluation.get("mode") != "seed":
        raise PerformanceEvidenceError("Seed input must be a successful seed-mode evaluation.")
    if evaluation.get("contractVersion") != contract["contractVersion"]:
        raise PerformanceEvidenceError("Seed input contractVersion does not match.")

    target = required_string(evaluation, "target", "seedEvaluation")
    profile = required_string(evaluation, "profile", "seedEvaluation")
    required_string(evaluation, "runId", "seedEvaluation")
    required_string(evaluation, "runnerClass", "seedEvaluation")
    required_commit(evaluation, "commit", "seedEvaluation")
    required_sha256(evaluation, "sourceHash", "seedEvaluation")
    required_current_timestamp(
        evaluation,
        "generatedUtc",
        "seedEvaluation",
        maximum_age_hours,
    )
    if target not in contract["requiredTargets"]:
        raise PerformanceEvidenceError(f"Seed input has unknown target '{target}'.")
    if (
        profile not in contract["profiles"]
        or contract["profiles"][profile]["baselineRequired"] is not True
    ):
        raise PerformanceEvidenceError(
            f"Seed input profile '{profile}' is not baseline-qualified."
        )

    environment = evaluation.get("environment")
    if not isinstance(environment, dict):
        raise PerformanceEvidenceError("Seed input has no environment evidence.")
    for key in (
        "frameworkDescription",
        "osDescription",
        "osArchitecture",
        "processArchitecture",
        "processor",
        "engineFamily",
        "serverVersion",
        "serverImage",
    ):
        required_string(environment, key, "seedEvaluation.environment")
    required_positive_integer(
        environment,
        "processorCount",
        "seedEvaluation.environment",
    )
    if environment["serverImage"] != contract["requiredTargets"][target]["serverImage"]:
        raise PerformanceEvidenceError(
            f"Seed input target '{target}' has the wrong server image."
        )

    workloads = evaluation.get("workloads")
    if not isinstance(workloads, list):
        raise PerformanceEvidenceError("Seed input has no normalized workloads.")
    validate_normalized_workloads(workloads, contract, profile, "seedEvaluation")
    validate_absolute_budgets(workloads, contract)

    controls = evaluation.get("benchmarkDotNetControls")
    if not isinstance(controls, list):
        raise PerformanceEvidenceError("Seed input has no BenchmarkDotNet controls.")
    expected_controls = {
        control["id"]: control
        for control in contract["benchmarkDotNetControls"]
    }
    actual_controls = {
        control.get("id"): control
        for control in controls
        if isinstance(control, dict) and isinstance(control.get("id"), str)
    }
    if set(actual_controls) != set(expected_controls):
        raise PerformanceEvidenceError("Seed BenchmarkDotNet control matrix is incomplete.")
    for control_id, expected in expected_controls.items():
        actual = actual_controls[control_id]
        actual_value = finite_number(
            actual.get("actual"),
            f"seedEvaluation.benchmarkDotNetControls.{control_id}.actual",
            minimum=0,
        )
        maximum = finite_number(
            actual.get("maximum"),
            f"seedEvaluation.benchmarkDotNetControls.{control_id}.maximum",
            minimum=0,
        )
        if (
            actual.get("metric") != expected["metric"]
            or maximum != float(expected["maximum"])
            or actual.get("passed") is not True
            or actual_value > maximum
        ):
            raise PerformanceEvidenceError(
                f"Seed BenchmarkDotNet control '{control_id}' is not reproducibly passing."
            )

    soak_scenarios = evaluation.get("soakScenarios")
    if not isinstance(soak_scenarios, list):
        raise PerformanceEvidenceError("Seed input has no soak scenarios.")
    actual_soak_ids = {
        scenario.get("id")
        for scenario in soak_scenarios
        if isinstance(scenario, dict)
    }
    if actual_soak_ids != SOAK_SCENARIO_IDS:
        raise PerformanceEvidenceError("Seed soak scenario matrix is incomplete.")
    for scenario in soak_scenarios:
        if not isinstance(scenario, dict):
            raise PerformanceEvidenceError("Seed soak evidence contains a non-object scenario.")
        scenario_id = required_string(scenario, "id", "seedEvaluation.soakScenario")
        metrics = scenario.get("metrics")
        budgets = scenario.get("budgets")
        if not isinstance(metrics, dict) or not isinstance(budgets, dict):
            raise PerformanceEvidenceError(
                f"Seed soak scenario '{scenario_id}' has invalid evidence."
            )
        normalized_metrics = {
            key: finite_number(value, f"{scenario_id}.metrics.{key}")
            for key, value in metrics.items()
        }
        normalized_budgets = {
            key: finite_number(value, f"{scenario_id}.budgets.{key}")
            for key, value in budgets.items()
        }
        if (
            scenario.get("success") is not True
            or not validate_soak_scenario(
                scenario_id,
                normalized_metrics,
                normalized_budgets,
                contract,
            )
        ):
            raise PerformanceEvidenceError(
                f"Seed soak scenario '{scenario_id}' is not passing."
            )

    artifact_hashes = evaluation.get("artifactHashes")
    if not isinstance(artifact_hashes, dict):
        raise PerformanceEvidenceError("Seed input has no artifact hashes.")
    expected_hash_keys = {"contract", "workloads", "benchmarkDotNet", "soak"}
    if set(artifact_hashes) != expected_hash_keys:
        raise PerformanceEvidenceError("Seed artifact hash matrix is incomplete.")
    for key in expected_hash_keys:
        required_sha256(artifact_hashes, key, "seedEvaluation.artifactHashes")
    if artifact_hashes["contract"] != sha256(contract_path):
        raise PerformanceEvidenceError("Seed input was evaluated against different contract bytes.")

    raw_reports = evaluation.get("rawReports")
    if not isinstance(raw_reports, list) or not raw_reports:
        raise PerformanceEvidenceError("Seed input has no raw BenchmarkDotNet report hashes.")
    raw_paths: set[str] = set()
    for index, report in enumerate(raw_reports):
        if not isinstance(report, dict):
            raise PerformanceEvidenceError(
                f"seedEvaluation.rawReports[{index}] must be an object."
            )
        path = required_string(report, "path", f"seedEvaluation.rawReports[{index}]")
        if Path(path).is_absolute() or ".." in Path(path).parts or path in raw_paths:
            raise PerformanceEvidenceError(
                f"Seed raw report path '{path}' is not unique and relative."
            )
        raw_paths.add(path)
        required_sha256(report, "sha256", f"seedEvaluation.rawReports[{index}]")

    if evaluation.get("historicalChecks") not in ([], None):
        raise PerformanceEvidenceError("Seed input unexpectedly contains historical checks.")


def validate_normalized_workloads(
    workloads: Sequence[dict[str, Any]],
    contract: dict[str, Any],
    profile: str,
    label: str,
) -> None:
    """Validate the normalized metric surface retained by evaluations and baselines."""
    expected = {
        definition["id"]: definition
        for definition in applicable_workloads(contract, profile)
    }
    actual: dict[str, dict[str, Any]] = {}
    for index, workload in enumerate(workloads):
        if not isinstance(workload, dict):
            raise PerformanceEvidenceError(f"{label}.workloads[{index}] must be an object.")
        workload_id = required_string(workload, "id", f"{label}.workloads[{index}]")
        if workload_id in actual:
            raise PerformanceEvidenceError(
                f"{label} contains duplicate workload '{workload_id}'."
            )
        actual[workload_id] = workload
    if set(actual) != set(expected):
        raise PerformanceEvidenceError(f"{label} workload matrix is incomplete.")

    maximum_relative_error = float(
        contract["profiles"][profile]["maximumRelativeStandardError"]
    )
    for workload_id, workload in actual.items():
        if workload.get("family") != expected[workload_id]["family"]:
            raise PerformanceEvidenceError(
                f"{label} workload '{workload_id}' has the wrong family."
            )
        sample_count = required_positive_integer(
            workload,
            "sampleCount",
            f"{label}.{workload_id}",
        )
        sample_count_field = (
            "expensiveMeasurementSamples"
            if expected[workload_id].get("cost", "standard") == "expensive"
            else "measurementSamples"
        )
        expected_sample_count = int(
            contract["profiles"][profile][sample_count_field]
        )
        if sample_count != expected_sample_count:
            raise PerformanceEvidenceError(
                f"{label} workload '{workload_id}' has the wrong sampleCount."
            )
        median = finite_number(
            workload.get("medianNanoseconds"),
            f"{label}.{workload_id}.medianNanoseconds",
            minimum=0.000001,
        )
        standard_error_value = finite_number(
            workload.get("standardErrorNanoseconds"),
            f"{label}.{workload_id}.standardErrorNanoseconds",
            minimum=0,
        )
        relative_error = finite_number(
            workload.get("relativeStandardError"),
            f"{label}.{workload_id}.relativeStandardError",
            minimum=0,
        )
        if (
            not close_enough(relative_error, standard_error_value / median)
            or relative_error > maximum_relative_error
        ):
            raise PerformanceEvidenceError(
                f"{label} workload '{workload_id}' has invalid statistical error."
            )
        expected_operations_per_sample = int(
            expected[workload_id].get("operationsPerSample", 1)
        )
        actual_operations_per_sample = required_positive_integer(
            workload,
            "operationsPerSample",
            f"{label}.{workload_id}",
        )
        if actual_operations_per_sample != expected_operations_per_sample:
            raise PerformanceEvidenceError(
                f"{label} workload '{workload_id}' has the wrong operationsPerSample."
            )
        for key in (
            "p95Nanoseconds",
            "p99Nanoseconds",
            "allocatedBytesPerOperation",
            "retainedBytes",
            "gen0CollectionsPer1000",
            "gen1CollectionsPer1000",
            "gen2CollectionsPer1000",
        ):
            finite_number(workload.get(key), f"{label}.{workload_id}.{key}", minimum=0)


def seed_baseline(args: argparse.Namespace) -> dict[str, Any]:
    """Create an explicit accepted baseline from successful seed evaluations."""
    contract = load_json(Path(args.contract))
    validate_contract(contract)
    evaluations = [load_json(Path(path)) for path in args.evidence]
    required_targets = set(contract["requiredTargets"])
    observed_targets: set[str] = set()
    baseline_entries: list[dict[str, Any]] = []

    for evidence_path, evaluation in zip(args.evidence, evaluations, strict=True):
        validate_seed_evaluation(
            evaluation,
            contract,
            Path(args.contract),
            maximum_age_hours=float(contract["evidenceMaximumAgeHours"]),
        )

        target = required_string(evaluation, "target", "seedEvaluation")
        if target in observed_targets:
            raise PerformanceEvidenceError(f"Seed input contains duplicate target '{target}'.")
        observed_targets.add(target)
        baseline_entries.append(
            {
                "target": target,
                "profile": required_string(evaluation, "profile", "seedEvaluation"),
                "runnerClass": required_string(evaluation, "runnerClass", "seedEvaluation"),
                "commit": required_string(evaluation, "commit", "seedEvaluation"),
                "sourceHash": required_sha256(
                    evaluation,
                    "sourceHash",
                    "seedEvaluation",
                ),
                "runId": required_string(evaluation, "runId", "seedEvaluation"),
                "generatedUtc": required_string(
                    evaluation,
                    "generatedUtc",
                    "seedEvaluation",
                ),
                "environment": evaluation.get("environment"),
                "sourceEvidenceSha256": sha256(Path(evidence_path)),
                "artifactHashes": evaluation.get("artifactHashes"),
                "rawReports": evaluation.get("rawReports"),
                "benchmarkDotNetControls": evaluation.get("benchmarkDotNetControls"),
                "soakScenarios": evaluation.get("soakScenarios"),
                "workloads": evaluation.get("workloads"),
            }
        )

    if observed_targets != required_targets:
        missing = sorted(required_targets - observed_targets)
        unknown = sorted(observed_targets - required_targets)
        raise PerformanceEvidenceError(
            f"Baseline seed target drift. Missing: [{', '.join(missing)}]. "
            f"Unknown: [{', '.join(unknown)}]."
        )

    retained_entries: list[dict[str, Any]] = []
    merge_existing = getattr(args, "merge_existing", None)
    if merge_existing:
        validate_baseline_file(
            argparse.Namespace(
                contract=args.contract,
                baseline=merge_existing,
            )
        )
        existing = load_json(Path(merge_existing))
        existing_entries = existing.get("baselines")
        if not isinstance(existing_entries, list):
            raise PerformanceEvidenceError("The existing baseline has no baselines array.")
        replacement_keys = {
            (entry["target"], entry["profile"], entry["runnerClass"])
            for entry in baseline_entries
        }
        retained_entries = [
            entry
            for entry in existing_entries
            if isinstance(entry, dict)
            and (
                entry.get("target"),
                entry.get("profile"),
                entry.get("runnerClass"),
            )
            not in replacement_keys
        ]

    combined_entries = retained_entries + baseline_entries

    return {
        "schemaVersion": 2,
        "baselineVersion": args.version,
        "baselineState": "accepted",
        "contractVersion": contract["contractVersion"],
        "acceptedUtc": args.accepted_utc
        or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "baselines": sorted(
            combined_entries,
            key=lambda entry: (
                entry["profile"],
                entry["runnerClass"],
                entry["target"],
            ),
        ),
    }


def validate_baseline_file(args: argparse.Namespace) -> dict[str, Any]:
    """Validate that every required target has one structurally complete baseline."""
    contract = load_json(Path(args.contract))
    validate_contract(contract)
    baseline = load_json(Path(args.baseline))
    if baseline.get("schemaVersion") != 2 or baseline.get("baselineState") != "accepted":
        raise PerformanceEvidenceError("Baseline must use schemaVersion 2 and state accepted.")
    if baseline.get("contractVersion") != contract["contractVersion"]:
        raise PerformanceEvidenceError("Baseline contractVersion does not match.")

    entries = baseline.get("baselines")
    if not isinstance(entries, list):
        raise PerformanceEvidenceError("Baseline baselines must be an array.")
    observed_keys: set[tuple[str, str, str]] = set()
    target_groups: dict[tuple[str, str], set[str]] = {}
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise PerformanceEvidenceError(f"baseline.baselines[{index}] must be an object.")
        target = required_string(entry, "target", f"baseline.baselines[{index}]")
        profile = required_string(entry, "profile", f"baseline.baselines[{index}]")
        runner_class = required_string(
            entry,
            "runnerClass",
            f"baseline.baselines[{index}]",
        )
        required_string(entry, "commit", f"baseline.baselines[{index}]")
        required_sha256(entry, "sourceHash", f"baseline.baselines[{index}]")
        required_sha256(
            entry,
            "sourceEvidenceSha256",
            f"baseline.baselines[{index}]",
        )
        validate_seed_evaluation(
            {
                **entry,
                "schemaVersion": 2,
                "kind": "performance-evaluation",
                "contractVersion": contract["contractVersion"],
                "mode": "seed",
                "success": True,
                "historicalChecks": [],
            },
            contract,
            Path(args.contract),
        )
        key = (target, profile, runner_class)
        if key in observed_keys:
            raise PerformanceEvidenceError(
                f"Baseline contains duplicate target/profile/runner tuple {key}."
            )
        observed_keys.add(key)
        target_groups.setdefault((profile, runner_class), set()).add(target)

    required_targets = set(contract["requiredTargets"])
    if not target_groups:
        raise PerformanceEvidenceError("Baseline contains no profile and runner groups.")
    for group, observed_targets in target_groups.items():
        if observed_targets != required_targets:
            raise PerformanceEvidenceError(
                f"Baseline target matrix for profile/runner {group} does not match "
                "requiredTargets."
            )

    return {
        "schemaVersion": 2,
        "kind": "baseline-validation",
        "baselineVersion": required_string(baseline, "baselineVersion", "baseline"),
        "success": True,
        "targetCount": len(observed_keys),
        "runnerGroupCount": len(target_groups),
    }


def add_common_identity_arguments(parser: argparse.ArgumentParser) -> None:
    """Add current-run identity arguments shared by validation commands."""
    parser.add_argument("--contract", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--profile", required=True)
    parser.add_argument("--output", required=True)


def build_parser() -> argparse.ArgumentParser:
    """Build the command-line surface."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    bdn_parser = subparsers.add_parser("validate-bdn")
    add_common_identity_arguments(bdn_parser)
    bdn_parser.add_argument("--reports", required=True)

    evaluate_parser = subparsers.add_parser("evaluate")
    add_common_identity_arguments(evaluate_parser)
    evaluate_parser.add_argument("--workloads", required=True)
    evaluate_parser.add_argument("--soak")
    evaluate_parser.add_argument("--bdn", required=True)
    evaluate_parser.add_argument("--baseline", required=True)
    evaluate_parser.add_argument("--mode", choices=("compare", "seed"), required=True)

    seed_parser = subparsers.add_parser("seed")
    seed_parser.add_argument("--contract", required=True)
    seed_parser.add_argument("--baseline", required=True)
    seed_parser.add_argument("--version", required=True)
    seed_parser.add_argument("--accepted-utc")
    seed_parser.add_argument("--evidence", action="append", required=True)
    seed_parser.add_argument("--merge-existing")

    baseline_parser = subparsers.add_parser("validate-baseline")
    baseline_parser.add_argument("--contract", required=True)
    baseline_parser.add_argument("--baseline", required=True)
    baseline_parser.add_argument("--output", required=True)

    source_hash_parser = subparsers.add_parser("source-hash")
    source_hash_parser.add_argument("--repo", required=True)

    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Run one deterministic evidence command."""
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        if args.command == "source-hash":
            print(repository_source_hash(Path(args.repo)))
            return 0
        if args.command == "validate-bdn":
            contract = load_json(Path(args.contract))
            validate_contract(contract)
            payload = validate_bdn_reports(
                contract,
                Path(args.reports),
                run_id=args.run_id,
                target=args.target,
                profile=args.profile,
            )
        elif args.command == "evaluate":
            payload = evaluate(args)
        elif args.command == "seed":
            payload = seed_baseline(args)
            write_json(Path(args.baseline), payload)
            print(
                f"Accepted performance baseline '{payload['baselineVersion']}' "
                f"with {len(payload['baselines'])} target records."
            )
            return 0
        elif args.command == "validate-baseline":
            payload = validate_baseline_file(args)
        else:
            parser.error(f"Unknown command '{args.command}'.")

        write_json(Path(args.output), payload)
        print(f"Performance evidence command '{args.command}' passed: {args.output}")
        return 0
    except PerformanceEvidenceError as error:
        print(f"Performance evidence failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
