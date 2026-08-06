#!/usr/bin/env python3
"""Validate, compare, and seed performance and memory evidence."""

from __future__ import annotations

import argparse
import copy
import ctypes
import hashlib
import json
import math
import os
import platform
import statistics
import subprocess
import sys
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Callable, NamedTuple, Sequence

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
P99_CONFIRMATION_RUNS = 2
P99_EXPECTED_EXCEEDANCE_PROBABILITY = 0.01
P99_SIGNIFICANCE_LEVEL = 0.01
HOST_ADMISSION_METRIC = "interval-host-cpu-utilization"


class HostCpuCounterSnapshot(NamedTuple):
    """Represent cumulative host CPU counters used by one interval sample."""

    source: str
    counters: tuple[int, ...]
    busy_indices: tuple[int, ...]
    counter_modulus: int | None


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
        """Run one read-only Git probe against the measured repository."""
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


def non_negative_integer(value: Any, label: str) -> int:
    """Read an integer index that may point at the first array element."""
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise PerformanceEvidenceError(f"{label} must be a non-negative integer.")

    return value


def expected_warmup_sample_count(
    profile_contract: dict[str, Any],
    workload_definition: dict[str, Any],
) -> int:
    """Derive the contract-owned warmup batch count for one workload."""
    profile_samples = int(profile_contract["warmupSamples"])
    minimum_operations = workload_definition.get("minimumWarmupOperations")

    if minimum_operations is None:
        return profile_samples

    operations_per_sample = int(workload_definition.get("operationsPerSample", 1))
    operation_bound_samples = (
        int(minimum_operations) + operations_per_sample - 1
    ) // operations_per_sample

    return max(profile_samples, operation_bound_samples)


def expected_measurement_sample_count(
    profile_contract: dict[str, Any],
    workload_definition: dict[str, Any],
) -> int:
    """Derive the contract-owned measurement population for one workload."""
    profile_field = (
        "expensiveMeasurementSamples"
        if workload_definition.get("cost", "standard") == "expensive"
        else "measurementSamples"
    )

    return int(
        workload_definition.get(
            "measurementSamples",
            profile_contract[profile_field],
        )
    )


def expected_workload_timeout_seconds(
    timeout_policies: dict[str, Any],
    profile_contract: dict[str, Any],
    workload_definition: dict[str, Any],
) -> int:
    """Resolve a named hang deadline without weakening stricter profiles."""
    policy_name = workload_definition.get("timeoutPolicy")
    policy_timeout = 0
    if policy_name is not None:
        policy = timeout_policies.get(policy_name)
        if not isinstance(policy, dict):
            raise PerformanceEvidenceError(
                f"Workload '{workload_definition.get('id')}' references unknown "
                f"timeout policy '{policy_name}'."
            )
        policy_timeout = int(policy["minimumWorkloadTimeoutSeconds"])

    return max(
        int(profile_contract["maximumWorkloadDurationSeconds"]),
        policy_timeout,
    )


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
    if contract.get("schemaVersion") != 4:
        raise PerformanceEvidenceError("Performance contract schemaVersion must be 4.")

    required_string(contract, "contractVersion", "contract")
    finite_number(
        contract.get("evidenceMaximumAgeHours"),
        "contract.evidenceMaximumAgeHours",
        minimum=1,
    )

    targets = contract.get("requiredTargets")
    profiles = contract.get("profiles")
    timeout_policies = contract.get("timeoutPolicies")
    families = contract.get("familyBudgets")
    workloads = contract.get("workloads")
    requirements = contract.get("coverageRequirements")
    host_preconditions = contract.get("hostPreconditions")
    calibration = contract.get("calibration")
    historical_budgets = contract.get("historicalBudgets")
    benchmark_controls = contract.get("benchmarkDotNetControls")
    soak_budgets = contract.get("soakBudgets")

    if not isinstance(targets, dict) or not targets:
        raise PerformanceEvidenceError("Performance contract must define requiredTargets.")
    if not isinstance(profiles, dict) or not profiles:
        raise PerformanceEvidenceError("Performance contract must define profiles.")
    if not isinstance(timeout_policies, dict) or not timeout_policies:
        raise PerformanceEvidenceError(
            "Performance contract must define timeoutPolicies."
        )
    if not isinstance(families, dict) or not families:
        raise PerformanceEvidenceError("Performance contract must define familyBudgets.")
    if not isinstance(workloads, list) or not workloads:
        raise PerformanceEvidenceError("Performance contract must define workloads.")
    if not isinstance(requirements, dict) or not requirements:
        raise PerformanceEvidenceError("Performance contract must define coverageRequirements.")
    if not isinstance(host_preconditions, dict):
        raise PerformanceEvidenceError("Performance contract must define hostPreconditions.")
    if not isinstance(calibration, dict):
        raise PerformanceEvidenceError("Performance contract must define calibration.")
    if not isinstance(historical_budgets, dict):
        raise PerformanceEvidenceError("Performance contract must define historicalBudgets.")
    if not isinstance(benchmark_controls, list) or not benchmark_controls:
        raise PerformanceEvidenceError("Performance contract must define benchmarkDotNetControls.")
    if not isinstance(soak_budgets, dict):
        raise PerformanceEvidenceError("Performance contract must define soakBudgets.")

    if host_preconditions.get("admissionMetric") != HOST_ADMISSION_METRIC:
        raise PerformanceEvidenceError(
            "Host admission metric must use interval host CPU utilization."
        )
    maximum_cpu = finite_number(
        host_preconditions.get("maximumCpuUtilization"),
        "hostPreconditions.maximumCpuUtilization",
        minimum=0,
    )
    if maximum_cpu <= 0 or maximum_cpu > 1:
        raise PerformanceEvidenceError(
            "Host CPU utilization must be greater than 0 and at most 1."
        )
    sample_interval = required_positive_integer(
        host_preconditions,
        "sampleIntervalMilliseconds",
        "hostPreconditions",
    )
    required_passes = required_positive_integer(
        host_preconditions,
        "requiredConsecutivePassingSamples",
        "hostPreconditions",
    )
    maximum_attempts = required_positive_integer(
        host_preconditions,
        "maximumSampleAttempts",
        "hostPreconditions",
    )
    if sample_interval < 100 or sample_interval > 10_000:
        raise PerformanceEvidenceError(
            "Host CPU sample interval must be between 100 and 10000 milliseconds."
        )
    if required_passes > maximum_attempts or maximum_attempts > 10:
        raise PerformanceEvidenceError(
            "Host CPU admission requires no more passes than attempts and at most 10 attempts."
        )

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
        "calibrationSamplesPerPulse",
        "calibrationIntervalSamples",
        "maximumWorkloadMatrixDurationSeconds",
        "maximumTotalDurationSeconds",
        "maximumWorkloadDurationSeconds",
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
        minimum_measurement_duration = finite_number(
            profile_contract.get("minimumMeasurementDurationMilliseconds"),
            f"profiles.{profile_name}.minimumMeasurementDurationMilliseconds",
            minimum=0,
        )
        if not minimum_measurement_duration.is_integer():
            raise PerformanceEvidenceError(
                f"profiles.{profile_name}.minimumMeasurementDurationMilliseconds "
                "must be an integer."
            )
        finite_number(
            profile_contract.get("maximumRelativeStandardError"),
            f"profiles.{profile_name}.maximumRelativeStandardError",
            minimum=0,
        )

    for policy_name, timeout_policy in timeout_policies.items():
        if not isinstance(policy_name, str) or not policy_name:
            raise PerformanceEvidenceError("Timeout policy names must be non-empty strings.")
        if not isinstance(timeout_policy, dict):
            raise PerformanceEvidenceError(
                f"timeoutPolicies.{policy_name} must be an object."
            )
        policy_timeout = finite_number(
            timeout_policy.get("minimumWorkloadTimeoutSeconds"),
            f"timeoutPolicies.{policy_name}.minimumWorkloadTimeoutSeconds",
            minimum=1,
        )
        if not policy_timeout.is_integer():
            raise PerformanceEvidenceError(
                f"timeoutPolicies.{policy_name}.minimumWorkloadTimeoutSeconds "
                "must be an integer."
            )
        finite_number(
            profile_contract.get("maximumCalibrationRelativeStandardError"),
            f"profiles.{profile_name}.maximumCalibrationRelativeStandardError",
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
        if profile_contract["baselineRequired"] and min(
            profile_contract["measurementSamples"],
            profile_contract["expensiveMeasurementSamples"],
        ) < 100:
            raise PerformanceEvidenceError(
                f"profiles.{profile_name} requires at least 100 observations "
                "for historical p99 evidence."
            )

    family_budget_fields = (
        "medianNanoseconds",
        "p95Nanoseconds",
        "p99Nanoseconds",
        "allocatedBytes",
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
        "normalizedMedianRatio",
        "normalizedP95Ratio",
        "normalizedP99Ratio",
        "allocatedBytesRatio",
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
    for key in ("genCollectionAllowancePer1000",):
        finite_number(
            historical_budgets.get(key),
            f"historicalBudgets.{key}",
            minimum=0,
        )

    calibration_families: list[str] = []
    for key in ("cpuFamilies", "databaseFamilies"):
        values = calibration.get(key)
        if not isinstance(values, list) or not values:
            raise PerformanceEvidenceError(f"calibration.{key} must be a non-empty array.")
        for value in values:
            if not isinstance(value, str) or value not in families:
                raise PerformanceEvidenceError(
                    f"calibration.{key} contains unknown family '{value}'."
                )
            calibration_families.append(value)

    if len(calibration_families) != len(set(calibration_families)):
        raise PerformanceEvidenceError("Calibration families must be disjoint.")
    if set(calibration_families) != set(families):
        raise PerformanceEvidenceError(
            "Calibration must classify every performance family exactly once."
        )

    workload_ids: list[str] = []
    consumed_timeout_policies: set[str] = set()
    for index, workload in enumerate(workloads):
        if not isinstance(workload, dict):
            raise PerformanceEvidenceError(f"contract.workloads[{index}] must be an object.")
        workload_id = required_string(workload, "id", f"contract.workloads[{index}]")
        family = required_string(workload, "family", f"contract.workloads[{index}]")
        if family not in families:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' references unknown budget family '{family}'."
            )
        workload_cost = workload.get("cost", "standard")
        if workload_cost not in ("standard", "expensive"):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' has an unknown cost class."
            )
        timeout_policy_name = workload.get("timeoutPolicy")
        if workload_cost == "expensive" and not isinstance(timeout_policy_name, str):
            raise PerformanceEvidenceError(
                f"Expensive workload '{workload_id}' must reference a timeoutPolicy."
            )
        if timeout_policy_name is not None:
            if workload_cost != "expensive":
                raise PerformanceEvidenceError(
                    f"Standard workload '{workload_id}' must not reference a timeoutPolicy."
                )
            if timeout_policy_name not in timeout_policies:
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' references unknown timeout policy "
                    f"'{timeout_policy_name}'."
                )
            consumed_timeout_policies.add(timeout_policy_name)
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
        minimum_warmup_operations = workload.get("minimumWarmupOperations")
        if minimum_warmup_operations is not None:
            minimum_warmup_operations = finite_number(
                minimum_warmup_operations,
                f"Workload '{workload_id}'.minimumWarmupOperations",
                minimum=1,
            )
            if not minimum_warmup_operations.is_integer():
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' minimumWarmupOperations must be an integer."
                )
        measurement_samples = workload.get("measurementSamples")
        if measurement_samples is not None:
            measurement_samples = finite_number(
                measurement_samples,
                f"Workload '{workload_id}'.measurementSamples",
                minimum=1,
            )
            if not measurement_samples.is_integer():
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' measurementSamples must be an integer."
                )
        if timeout_policy_name is not None:
            for profile_name, profile_contract in profiles.items():
                if profile_name == "smoke" and workload.get("smoke") is not True:
                    continue
                if expected_workload_timeout_seconds(
                    timeout_policies,
                    profile_contract,
                    workload,
                ) > profile_contract["maximumWorkloadMatrixDurationSeconds"]:
                    raise PerformanceEvidenceError(
                        f"Workload '{workload_id}' timeout exceeds the "
                        f"'{profile_name}' matrix deadline."
                    )
        workload_ids.append(workload_id)

    unused_timeout_policies = sorted(
        set(timeout_policies) - consumed_timeout_policies
    )
    if unused_timeout_policies:
        raise PerformanceEvidenceError(
            "Performance contract contains unused timeout policies: "
            + ", ".join(unused_timeout_policies)
            + "."
        )

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


def resolve_processor_identity() -> str:
    """Return a stable processor model without adding a platform dependency."""
    override = os.environ.get("DOKA_BENCHMARK_PROCESSOR")
    if override and override.strip():
        return override.strip()

    if sys.platform == "darwin":
        try:
            result = subprocess.run(
                ["sysctl", "-n", "machdep.cpu.brand_string"],
                check=True,
                capture_output=True,
                text=True,
            )
            if result.stdout.strip():
                return result.stdout.strip()
        except (OSError, subprocess.CalledProcessError):
            pass

    if sys.platform.startswith("linux"):
        try:
            for line in Path("/proc/cpuinfo").read_text(encoding="utf-8").splitlines():
                key, separator, value = line.partition(":")
                if separator and key.strip() in ("model name", "Hardware", "Processor"):
                    if value.strip():
                        return value.strip()
        except OSError:
            pass

    return platform.processor().strip() or platform.machine().strip() or "unknown"


def parse_linux_cpu_counters(output: str) -> HostCpuCounterSnapshot:
    """Parse the aggregate CPU counters from Linux /proc/stat."""
    aggregate = next(
        (line for line in output.splitlines() if line.startswith("cpu ")),
        None,
    )
    if aggregate is None:
        raise PerformanceEvidenceError("Linux host CPU counters are missing.")

    fields = aggregate.split()
    if len(fields) < 9:
        raise PerformanceEvidenceError("Linux host CPU counters are incomplete.")
    try:
        counters = tuple(int(value) for value in fields[1:9])
    except ValueError as error:
        raise PerformanceEvidenceError("Linux host CPU counters are invalid.") from error
    if any(value < 0 for value in counters):
        raise PerformanceEvidenceError("Linux host CPU counters are invalid.")

    # Guest time is already included in user and nice time. Counting only the
    # first eight documented fields therefore avoids double-counting capacity.
    return HostCpuCounterSnapshot(
        source="linux-proc-stat",
        counters=counters,
        busy_indices=(0, 1, 2, 5, 6, 7),
        counter_modulus=None,
    )


def capture_linux_cpu_counters() -> HostCpuCounterSnapshot:
    """Read the cumulative aggregate CPU counters exposed by Linux."""
    try:
        output = Path("/proc/stat").read_text(encoding="utf-8")
    except OSError as error:
        raise PerformanceEvidenceError(
            "Unable to capture Linux host CPU counters."
        ) from error

    return parse_linux_cpu_counters(output)


def capture_macos_cpu_counters() -> HostCpuCounterSnapshot:
    """Read Darwin host CPU ticks through the public Mach host API."""

    class HostCpuLoadInfo(ctypes.Structure):
        _fields_ = [("cpu_ticks", ctypes.c_uint32 * 4)]

    try:
        library = ctypes.CDLL(None)
        mach_host_self = library.mach_host_self
        mach_host_self.restype = ctypes.c_uint32
        mach_port_deallocate = library.mach_port_deallocate
        mach_port_deallocate.argtypes = (ctypes.c_uint32, ctypes.c_uint32)
        mach_port_deallocate.restype = ctypes.c_int
        host_statistics64 = library.host_statistics64
        host_statistics64.argtypes = (
            ctypes.c_uint32,
            ctypes.c_int,
            ctypes.POINTER(ctypes.c_int),
            ctypes.POINTER(ctypes.c_uint32),
        )
        host_statistics64.restype = ctypes.c_int
        information = HostCpuLoadInfo()
        count = ctypes.c_uint32(4)
        task_self = ctypes.c_uint32.in_dll(library, "mach_task_self_").value
        host_port = mach_host_self()
        if host_port == 0:
            raise PerformanceEvidenceError("macOS returned an invalid host port.")
        try:
            result = host_statistics64(
                host_port,
                3,
                ctypes.cast(ctypes.byref(information), ctypes.POINTER(ctypes.c_int)),
                ctypes.byref(count),
            )
        finally:
            deallocation_result = mach_port_deallocate(task_self, host_port)
    except (AttributeError, OSError, ValueError) as error:
        raise PerformanceEvidenceError(
            "Unable to capture macOS host CPU counters."
        ) from error

    if deallocation_result != 0:
        raise PerformanceEvidenceError("Unable to release the macOS host port.")
    if result != 0 or count.value < 4:
        raise PerformanceEvidenceError("macOS host CPU counters are incomplete.")

    return HostCpuCounterSnapshot(
        source="macos-host-statistics64",
        counters=tuple(information.cpu_ticks),
        busy_indices=(0, 1, 3),
        counter_modulus=2**32,
    )


def capture_host_cpu_counters() -> HostCpuCounterSnapshot:
    """Capture one platform-native cumulative host CPU snapshot."""
    if sys.platform.startswith("linux"):
        return capture_linux_cpu_counters()
    if sys.platform == "darwin":
        return capture_macos_cpu_counters()

    raise PerformanceEvidenceError(
        f"Host CPU interval sampling is unsupported on '{sys.platform}'."
    )


def calculate_host_cpu_utilization(
    before: HostCpuCounterSnapshot,
    after: HostCpuCounterSnapshot,
) -> float:
    """Calculate busy capacity from two compatible cumulative snapshots."""
    if (
        before.source != after.source
        or before.busy_indices != after.busy_indices
        or before.counter_modulus != after.counter_modulus
        or len(before.counters) != len(after.counters)
    ):
        raise PerformanceEvidenceError("Host CPU counter snapshots are incompatible.")

    deltas: list[int] = []
    for earlier, later in zip(before.counters, after.counters, strict=True):
        if later >= earlier:
            deltas.append(later - earlier)
            continue
        if before.counter_modulus is None:
            raise PerformanceEvidenceError("Host CPU counters moved backwards.")
        deltas.append(later + before.counter_modulus - earlier)

    total_delta = sum(deltas)
    if total_delta <= 0:
        raise PerformanceEvidenceError("Host CPU counters did not advance.")
    busy_delta = sum(deltas[index] for index in before.busy_indices)

    return busy_delta / total_delta


def sample_host_cpu_utilization(
    interval_seconds: float,
    *,
    counter_reader: Callable[[], HostCpuCounterSnapshot] = capture_host_cpu_counters,
    sleeper: Callable[[float], None] = time.sleep,
) -> tuple[str, float]:
    """Measure current host CPU utilization across one bounded interval."""
    if not math.isfinite(interval_seconds) or interval_seconds <= 0:
        raise PerformanceEvidenceError("Host CPU sample interval must be positive.")

    before = counter_reader()
    sleeper(interval_seconds)
    after = counter_reader()

    return before.source, calculate_host_cpu_utilization(before, after)


def capture_host_preflight(
    contract: dict[str, Any],
    *,
    sample_provider: Callable[[float], tuple[str, float]] = sample_host_cpu_utilization,
) -> dict[str, Any]:
    """Admit only a host with sustained current CPU headroom."""
    validate_contract(contract)
    processor_count = os.cpu_count()
    if processor_count is None or processor_count <= 0:
        raise PerformanceEvidenceError("The benchmark host exposes no processor count.")

    try:
        load_average_1m, load_average_5m, load_average_15m = os.getloadavg()
    except (AttributeError, OSError) as error:
        raise PerformanceEvidenceError(
            "The benchmark host does not expose Unix load averages."
        ) from error

    preconditions = contract["hostPreconditions"]
    maximum_cpu_utilization = finite_number(
        preconditions["maximumCpuUtilization"],
        "hostPreconditions.maximumCpuUtilization",
        minimum=0,
    )
    sample_interval_milliseconds = required_positive_integer(
        preconditions,
        "sampleIntervalMilliseconds",
        "hostPreconditions",
    )
    required_passes = required_positive_integer(
        preconditions,
        "requiredConsecutivePassingSamples",
        "hostPreconditions",
    )
    maximum_attempts = required_positive_integer(
        preconditions,
        "maximumSampleAttempts",
        "hostPreconditions",
    )
    ratio = load_average_1m / processor_count
    samples: list[dict[str, Any]] = []
    consecutive_passing_samples = 0
    sampling_source: str | None = None

    for sequence in range(1, maximum_attempts + 1):
        source, cpu_utilization = sample_provider(
            sample_interval_milliseconds / 1000.0
        )
        if sampling_source is None:
            sampling_source = source
        elif source != sampling_source:
            raise PerformanceEvidenceError(
                "Host CPU sampling source changed during admission."
            )
        if not math.isfinite(cpu_utilization) or not 0 <= cpu_utilization <= 1:
            raise PerformanceEvidenceError("Host CPU utilization sample is invalid.")

        within_limit = cpu_utilization <= maximum_cpu_utilization
        samples.append(
            {
                "sequence": sequence,
                "cpuUtilization": cpu_utilization,
                "withinLimit": within_limit,
            }
        )
        consecutive_passing_samples = (
            consecutive_passing_samples + 1
            if within_limit
            else 0
        )
        if consecutive_passing_samples == required_passes:
            break

    success = consecutive_passing_samples == required_passes
    observed_maximum_cpu_utilization = max(
        sample["cpuUtilization"]
        for sample in samples
    )
    admitted_cpu_utilization = (
        max(
            sample["cpuUtilization"]
            for sample in samples[-required_passes:]
        )
        if success
        else None
    )

    return {
        "schemaVersion": 4,
        "kind": "performance-host-preflight",
        "contractVersion": contract["contractVersion"],
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "processor": resolve_processor_identity(),
        "processorCount": processor_count,
        "loadAverage1Minute": load_average_1m,
        "loadAverage5Minutes": load_average_5m,
        "loadAverage15Minutes": load_average_15m,
        "loadAverage1MinutePerProcessor": ratio,
        "admissionMetric": HOST_ADMISSION_METRIC,
        "samplingSource": sampling_source,
        "sampleIntervalMilliseconds": sample_interval_milliseconds,
        "requiredConsecutivePassingSamples": required_passes,
        "maximumSampleAttempts": maximum_attempts,
        "samples": samples,
        "admittedCpuUtilization": admitted_cpu_utilization,
        "observedMaximumCpuUtilization": observed_maximum_cpu_utilization,
        "maximumCpuUtilization": maximum_cpu_utilization,
        "success": success,
    }


def validate_host_preflight(
    report: dict[str, Any],
    contract: dict[str, Any],
    *,
    maximum_age_hours: float | None,
) -> dict[str, Any]:
    """Reject stale, overloaded, or contract-drifting host preflight evidence."""
    if (
        report.get("schemaVersion") != 4
        or report.get("kind") != "performance-host-preflight"
    ):
        raise PerformanceEvidenceError("Host preflight schema or kind is invalid.")
    if report.get("contractVersion") != contract["contractVersion"]:
        raise PerformanceEvidenceError("Host preflight contractVersion does not match.")

    required_current_timestamp(
        report,
        "generatedUtc",
        "hostPreflight",
        maximum_age_hours,
    )
    required_string(report, "processor", "hostPreflight")
    processor_count = required_positive_integer(report, "processorCount", "hostPreflight")
    load_average_1m = finite_number(
        report.get("loadAverage1Minute"),
        "hostPreflight.loadAverage1Minute",
        minimum=0,
    )
    for key in ("loadAverage5Minutes", "loadAverage15Minutes"):
        finite_number(report.get(key), f"hostPreflight.{key}", minimum=0)

    actual_ratio = finite_number(
        report.get("loadAverage1MinutePerProcessor"),
        "hostPreflight.loadAverage1MinutePerProcessor",
        minimum=0,
    )
    expected_ratio = load_average_1m / processor_count
    if not close_enough(actual_ratio, expected_ratio):
        raise PerformanceEvidenceError(
            "Host preflight load ratio does not match load and processor count."
        )

    if report.get("admissionMetric") != HOST_ADMISSION_METRIC:
        raise PerformanceEvidenceError("Host preflight admission metric is invalid.")
    sampling_source = required_string(report, "samplingSource", "hostPreflight")
    if sampling_source not in ("linux-proc-stat", "macos-host-statistics64"):
        raise PerformanceEvidenceError("Host preflight sampling source is invalid.")

    preconditions = contract["hostPreconditions"]
    expected_interval = preconditions["sampleIntervalMilliseconds"]
    expected_required_passes = preconditions["requiredConsecutivePassingSamples"]
    expected_maximum_attempts = preconditions["maximumSampleAttempts"]
    if report.get("sampleIntervalMilliseconds") != expected_interval:
        raise PerformanceEvidenceError("Host preflight sample interval is invalid.")
    if report.get("requiredConsecutivePassingSamples") != expected_required_passes:
        raise PerformanceEvidenceError(
            "Host preflight consecutive passing sample count is invalid."
        )
    if report.get("maximumSampleAttempts") != expected_maximum_attempts:
        raise PerformanceEvidenceError("Host preflight maximum sample attempts is invalid.")

    samples = report.get("samples")
    if (
        not isinstance(samples, list)
        or not samples
        or len(samples) > expected_maximum_attempts
    ):
        raise PerformanceEvidenceError("Host preflight samples are incomplete.")

    consecutive_passes = 0
    first_acceptance: int | None = None
    sample_values: list[float] = []
    for expected_sequence, sample in enumerate(samples, start=1):
        if not isinstance(sample, dict) or sample.get("sequence") != expected_sequence:
            raise PerformanceEvidenceError("Host preflight sample sequence is invalid.")
        value = finite_number(
            sample.get("cpuUtilization"),
            f"hostPreflight.samples[{expected_sequence - 1}].cpuUtilization",
            minimum=0,
        )
        if value > 1:
            raise PerformanceEvidenceError("Host preflight CPU utilization is invalid.")
        within_limit = value <= preconditions["maximumCpuUtilization"]
        if sample.get("withinLimit") is not within_limit:
            raise PerformanceEvidenceError("Host preflight sample decision is invalid.")
        sample_values.append(value)
        consecutive_passes = consecutive_passes + 1 if within_limit else 0
        if consecutive_passes == expected_required_passes and first_acceptance is None:
            first_acceptance = expected_sequence

    success = report.get("success")
    if not isinstance(success, bool):
        raise PerformanceEvidenceError("Host preflight success flag is invalid.")
    if success:
        if first_acceptance != len(samples):
            raise PerformanceEvidenceError(
                "Host preflight did not stop at the first successful admission window."
            )
        admitted_sample_values = sample_values[-expected_required_passes:]
    else:
        if first_acceptance is not None or len(samples) != expected_maximum_attempts:
            raise PerformanceEvidenceError(
                "Failed host preflight did not exhaust its admission window."
            )

    observed_maximum_cpu_utilization = finite_number(
        report.get("observedMaximumCpuUtilization"),
        "hostPreflight.observedMaximumCpuUtilization",
        minimum=0,
    )
    maximum_cpu_utilization = finite_number(
        report.get("maximumCpuUtilization"),
        "hostPreflight.maximumCpuUtilization",
        minimum=0,
    )
    expected_maximum = float(preconditions["maximumCpuUtilization"])
    if maximum_cpu_utilization != expected_maximum:
        raise PerformanceEvidenceError("Host preflight CPU ceiling is invalid.")
    if not close_enough(observed_maximum_cpu_utilization, max(sample_values)):
        raise PerformanceEvidenceError(
            "Host preflight observed maximum CPU utilization does not match its samples."
        )

    admitted_cpu_utilization = report.get("admittedCpuUtilization")
    if success:
        admitted_cpu_utilization = finite_number(
            admitted_cpu_utilization,
            "hostPreflight.admittedCpuUtilization",
            minimum=0,
        )
        if not close_enough(
            admitted_cpu_utilization,
            max(admitted_sample_values),
        ):
            raise PerformanceEvidenceError(
                "Host preflight admitted CPU utilization does not match "
                "its acceptance window."
            )
    elif admitted_cpu_utilization is not None:
        raise PerformanceEvidenceError(
            "Failed host preflight cannot record an admitted CPU utilization."
        )

    if not success:
        observed_samples = ", ".join(
            f"{sample_value:.4f}" for sample_value in sample_values
        )
        raise PerformanceEvidenceError(
            "Benchmark host admission did not produce "
            f"{expected_required_passes} consecutive CPU samples at or below "
            f"{maximum_cpu_utilization:.4f} in {expected_maximum_attempts} attempts; "
            f"observed samples: [{observed_samples}]."
        )

    return report


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
    if report.get("schemaVersion") != 3 or report.get("kind") != "performance-workloads":
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
    for key in (
        "hostLoadAverage1Minute",
        "hostLoadAverage5Minutes",
        "hostLoadAverage15Minutes",
        "hostLoadAverage1MinutePerProcessor",
    ):
        finite_number(
            environment.get(key),
            f"workloadReport.environment.{key}",
            minimum=0,
        )

    admission_metric = environment.get("hostAdmissionMetric")
    if admission_metric != HOST_ADMISSION_METRIC:
        raise PerformanceEvidenceError(
            "workloadReport.environment host admission metric is invalid."
        )
    cpu_utilization = finite_number(
        environment.get("admittedHostCpuUtilization"),
        "workloadReport.environment.admittedHostCpuUtilization",
        minimum=0,
    )
    maximum_cpu_utilization = finite_number(
        environment.get("maximumHostCpuUtilization"),
        "workloadReport.environment.maximumHostCpuUtilization",
        minimum=0,
    )
    expected_maximum_cpu = float(
        contract["hostPreconditions"]["maximumCpuUtilization"]
    )
    if (
        maximum_cpu_utilization != expected_maximum_cpu
        or cpu_utilization > maximum_cpu_utilization
    ):
        raise PerformanceEvidenceError(
            "workloadReport.environment records a saturated benchmark host."
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
        expected_warmup_samples = expected_warmup_sample_count(
            profile_contract,
            definition,
        )
        if warmup_samples != expected_warmup_samples:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' has {warmup_samples} warmups, "
                f"expected {expected_warmup_samples}."
            )

        sample_count = required_positive_integer(entry, "sampleCount", workload_id)
        measured_utc = required_current_timestamp(
            entry,
            "measuredUtc",
            workload_id,
            float(contract["evidenceMaximumAgeHours"]),
        )
        expected_sample_count = expected_measurement_sample_count(
            profile_contract,
            definition,
        )
        if sample_count < expected_sample_count:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' has {sample_count} samples, "
                f"expected at least {expected_sample_count}."
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
        calibration_kind = required_string(entry, "calibrationKind", workload_id)
        cpu_families = set(contract["calibration"]["cpuFamilies"])
        expected_calibration_kind = (
            "cpu"
            if definition["family"] in cpu_families
            else "database"
        )
        if calibration_kind != expected_calibration_kind:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' reports calibrationKind="
                f"'{calibration_kind}', expected '{expected_calibration_kind}'."
            )

        calibration_payload = entry.get("calibrationNanoseconds")
        calibration_pulse_payload = entry.get("calibrationPulseNanoseconds")
        calibration_pulse_index_payload = entry.get("calibrationPulseIndices")
        normalized_payload = entry.get("normalizedSamples")
        if (
            not isinstance(calibration_payload, list)
            or len(calibration_payload) != sample_count
        ):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' calibration array does not match sampleCount."
            )
        if (
            not isinstance(normalized_payload, list)
            or len(normalized_payload) != sample_count
        ):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' normalized array does not match sampleCount."
            )
        if not isinstance(calibration_pulse_payload, list) or not calibration_pulse_payload:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' has no calibration pulse evidence."
            )
        if (
            not isinstance(calibration_pulse_index_payload, list)
            or len(calibration_pulse_index_payload) != sample_count
        ):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' calibration pulse index array does not match sampleCount."
            )
        calibration_samples = [
            finite_number(
                sample,
                f"{workload_id}.calibrationNanoseconds[{index}]",
                minimum=0.000001,
            )
            for index, sample in enumerate(calibration_payload)
        ]
        calibration_pulses = [
            finite_number(
                sample,
                f"{workload_id}.calibrationPulseNanoseconds[{index}]",
                minimum=0.000001,
            )
            for index, sample in enumerate(calibration_pulse_payload)
        ]
        calibration_pulse_indices = [
            non_negative_integer(
                value,
                f"{workload_id}.calibrationPulseIndices[{index}]",
            )
            for index, value in enumerate(calibration_pulse_index_payload)
        ]
        if calibration_pulse_indices[0] != 0:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' does not start with calibration pulse zero."
            )
        maximum_interval = int(profile_contract["calibrationIntervalSamples"])
        samples_per_pulse: dict[int, int] = {}
        previous_pulse_index = 0
        for sample_index, pulse_index in enumerate(calibration_pulse_indices):
            if pulse_index >= len(calibration_pulses):
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' calibration pulse index {pulse_index} is out of range."
                )
            if pulse_index not in (previous_pulse_index, previous_pulse_index + 1):
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' calibration pulse indices are not sequential."
                )
            if not close_enough(
                calibration_samples[sample_index],
                calibration_pulses[pulse_index],
            ):
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' sample {sample_index} is not bound to its calibration pulse."
                )
            samples_per_pulse[pulse_index] = samples_per_pulse.get(pulse_index, 0) + 1
            previous_pulse_index = pulse_index
        if set(samples_per_pulse) != set(range(len(calibration_pulses))):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' contains an unused calibration pulse."
            )
        if any(count > maximum_interval for count in samples_per_pulse.values()):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' reuses a calibration pulse for too many samples."
            )
        normalized_samples = [
            finite_number(
                sample,
                f"{workload_id}.normalizedSamples[{index}]",
                minimum=0.000001,
            )
            for index, sample in enumerate(normalized_payload)
        ]
        recomputed_normalized = [
            sample / calibration
            for sample, calibration in zip(samples, calibration_samples)
        ]
        for index, (actual, expected) in enumerate(
            zip(normalized_samples, recomputed_normalized)
        ):
            if not close_enough(actual, expected):
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' normalizedSamples[{index}]="
                    f"{actual}, recomputed value is {expected}."
                )

        measurement_duration_nanoseconds = sum(samples) * operations_per_sample
        minimum_measurement_duration_nanoseconds = (
            int(profile_contract["minimumMeasurementDurationMilliseconds"])
            * 1_000_000
        )
        if measurement_duration_nanoseconds < minimum_measurement_duration_nanoseconds:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' measured {measurement_duration_nanoseconds} ns, "
                f"expected at least {minimum_measurement_duration_nanoseconds} ns."
            )
        sorted_samples = sorted(samples)
        sorted_calibration_pulses = sorted(calibration_pulses)
        sorted_normalized_samples = sorted(normalized_samples)
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

        calibrated = {
            "calibrationMedianNanoseconds": percentile(
                sorted_calibration_pulses,
                0.5,
            ),
            "calibrationStandardErrorNanoseconds": standard_error(
                calibration_pulses
            ),
            "normalizedMedian": percentile(sorted_normalized_samples, 0.5),
            "normalizedP95": percentile(sorted_normalized_samples, 0.95),
            "normalizedP99": percentile(sorted_normalized_samples, 0.99),
        }
        for key, expected_value in calibrated.items():
            actual_value = finite_number(entry.get(key), f"{workload_id}.{key}", minimum=0)
            if not close_enough(actual_value, expected_value):
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' reports {key}={actual_value}, "
                    f"recomputed value is {expected_value}."
                )

        normalized_standard_error = standard_error(normalized_samples)
        relative_standard_error = (
            normalized_standard_error / calibrated["normalizedMedian"]
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
        calibration_relative_standard_error = (
            calibrated["calibrationStandardErrorNanoseconds"]
            / calibrated["calibrationMedianNanoseconds"]
        )
        maximum_calibration_relative_standard_error = finite_number(
            profile_contract.get("maximumCalibrationRelativeStandardError"),
            f"profiles.{profile}.maximumCalibrationRelativeStandardError",
            minimum=0,
        )
        if (
            calibration_relative_standard_error
            > maximum_calibration_relative_standard_error
        ):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' calibration relative standard error "
                f"{calibration_relative_standard_error:.6f}, maximum is "
                f"{maximum_calibration_relative_standard_error:.6f}."
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
                "measuredUtc": measured_utc.isoformat().replace("+00:00", "Z"),
                "operationsPerSample": operations_per_sample,
                "measurementDurationNanoseconds": measurement_duration_nanoseconds,
                "relativeStandardError": relative_standard_error,
                "calibrationKind": calibration_kind,
                "calibrationRelativeStandardError": calibration_relative_standard_error,
                "normalizedStandardError": normalized_standard_error,
                "_normalizedSamples": normalized_samples,
                **derived,
                **calibrated,
                **metrics,
            }
        )

    return normalized


def public_workload_metrics(
    workloads: Sequence[dict[str, Any]],
) -> list[dict[str, Any]]:
    """Remove validator-only sample arrays from persisted normalized evidence."""
    return [
        {
            key: value
            for key, value in workload.items()
            if not key.startswith("_")
        }
        for workload in workloads
    ]


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
    """Identify isolated p99 regressions that require independent confirmation."""
    baseline_workloads = baseline_entry.get("workloads")
    if not isinstance(baseline_workloads, list):
        raise PerformanceEvidenceError("Accepted baseline entry has no workloads.")

    baseline_by_id = {
        workload.get("id"): workload
        for workload in baseline_workloads
        if isinstance(workload, dict) and isinstance(workload.get("id"), str)
    }
    policy = contract["historicalBudgets"]
    ratio = finite_number(
        policy.get("normalizedP99Ratio"),
        "historicalBudgets.normalizedP99Ratio",
        minimum=1,
    )
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
        adjustment_factor = calibration_adjustment_factor(
            workload_id,
            workload,
            baseline_workload,
        )
        actual = observed * adjustment_factor
        baseline_value = finite_number(
            baseline_workload.get("normalizedP99"),
            f"baseline.{workload_id}.normalizedP99",
            minimum=0,
        )
        baseline_calibration = finite_number(
            baseline_workload.get("calibrationMedianNanoseconds"),
            f"baseline.{workload_id}.calibrationMedianNanoseconds",
            minimum=0.000001,
        )
        maximum = baseline_value * ratio
        if actual > maximum:
            candidates.append(
                {
                    "workloadId": workload_id,
                    "baseline": baseline_value,
                    "baselineCalibrationMedianNanoseconds": baseline_calibration,
                    "observed": observed,
                    "actual": actual,
                    "maximum": maximum,
                    "calibrationAdjustmentFactor": adjustment_factor,
                    "confirmationRuns": P99_CONFIRMATION_RUNS,
                }
            )

    return sorted(candidates, key=lambda candidate: candidate["workloadId"])


def plan_tail_confirmation(args: argparse.Namespace) -> dict[str, Any]:
    """Plan bounded reruns only for current p99 values outside historical budgets."""
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
        candidates = historical_p99_confirmation_candidates(
            normalized,
            matching_entry,
            contract,
        )

    return {
        "schemaVersion": 1,
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
        plan.get("schemaVersion") != 1
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
        if confirmation_runs != P99_CONFIRMATION_RUNS:
            raise PerformanceEvidenceError(
                f"Tail confirmation plan requires {confirmation_runs} runs for "
                f"'{workload_id}', expected {P99_CONFIRMATION_RUNS}."
            )
        original_p99 = normalized_by_id[workload_id]["normalizedP99"]
        planned_observed = finite_number(
            candidate.get("observed"),
            f"{workload_id}.observed",
            minimum=0,
        )
        if not close_enough(
            planned_observed,
            original_p99,
        ):
            raise PerformanceEvidenceError(
                f"Tail confirmation plan observed value for '{workload_id}' drifted."
            )
        adjustment_factor = finite_number(
            candidate.get("calibrationAdjustmentFactor"),
            f"{workload_id}.calibrationAdjustmentFactor",
            minimum=0.000001,
        )
        if adjustment_factor > 1:
            raise PerformanceEvidenceError(
                f"Tail confirmation plan adjustment for '{workload_id}' is invalid."
            )
        planned_actual = finite_number(
            candidate.get("actual"),
            f"{workload_id}.actual",
            minimum=0,
        )
        if not close_enough(planned_actual, planned_observed * adjustment_factor):
            raise PerformanceEvidenceError(
                f"Tail confirmation plan actual value for '{workload_id}' drifted."
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
        maximum = finite_number(
            candidate.get("maximum"),
            f"tailConfirmationPlan.{workload_id}.maximum",
            minimum=0,
        )
        p99_check = historical_p99_check(
            workload_id,
            merged,
            {
                "normalizedP99": candidate.get("baseline"),
                "calibrationMedianNanoseconds": candidate.get(
                    "baselineCalibrationMedianNanoseconds"
                ),
            },
            contract,
        )
        if not close_enough(p99_check["maximum"], maximum):
            raise PerformanceEvidenceError(
                f"Tail confirmation plan maximum for '{workload_id}' drifted."
            )
        confirmation_results.append(
            {
                "workloadId": workload_id,
                "confirmationRuns": len(confirmations),
                "originalSampleCount": original_by_id[workload_id]["sampleCount"],
                "confirmationSampleCount": merged["sampleCount"],
                "originalNormalizedP99": original_by_id[workload_id]["normalizedP99"],
                "confirmationNormalizedP99": merged["normalizedP99"],
                "confirmationCalibrationMedianNanoseconds": merged[
                    "calibrationMedianNanoseconds"
                ],
                "maximumNormalizedP99": maximum,
                "calibrationAdjustmentFactor": p99_check[
                    "calibrationAdjustmentFactor"
                ],
                "exceedanceCount": p99_check["exceedanceCount"],
                "exceedanceRate": p99_check["exceedanceRate"],
                "expectedExceedanceProbability": p99_check[
                    "expectedExceedanceProbability"
                ],
                "pValue": p99_check["pValue"],
                "significanceLevel": p99_check["significanceLevel"],
                "normalizedSamples": merged["normalizedSamples"],
                "passed": p99_check["passed"],
            }
        )
        if not p99_check["passed"]:
            raise PerformanceEvidenceError(
                f"Confirmed historical p99 regression for '{workload_id}': "
                f"{p99_check['exceedanceCount']} of {p99_check['sampleCount']} "
                f"samples exceeded {maximum}; p-value {p99_check['pValue']}."
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
    metrics = {
        "normalizedMedian": ("normalizedMedianRatio", 0.0),
        "normalizedP95": ("normalizedP95Ratio", 0.0),
        "allocatedBytesPerOperation": ("allocatedBytesRatio", 0.0),
    }
    checks: list[dict[str, Any]] = []

    for workload_id in sorted(current_by_id):
        current_workload = current_by_id[workload_id]
        baseline_workload = baseline_by_id[workload_id]
        for metric, (ratio_key, allowance) in metrics.items():
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
            ratio = finite_number(policy.get(ratio_key), f"historicalBudgets.{ratio_key}", minimum=1)
            maximum = max(baseline_value * ratio, baseline_value + allowance)
            adjustment_factor = (
                calibration_adjustment_factor(
                    workload_id,
                    current_workload,
                    baseline_workload,
                )
                if metric.startswith("normalized")
                else 1.0
            )
            current_value = observed_value * adjustment_factor
            passed = current_value <= maximum
            checks.append(
                {
                    "workloadId": workload_id,
                    "metric": metric,
                    "baseline": baseline_value,
                    "observed": observed_value,
                    "actual": current_value,
                    "maximum": maximum,
                    "calibrationAdjustmentFactor": adjustment_factor,
                    "passed": passed,
                }
            )
            if not passed:
                raise PerformanceEvidenceError(
                    f"Historical budget failed for '{workload_id}' {metric}: "
                    f"{current_value} > {maximum} from baseline {baseline_value}."
                )

    confirmation_candidates = historical_p99_confirmation_candidates(
        current,
        baseline_entry,
        contract,
    )
    candidate_ids = {
        candidate["workloadId"]
        for candidate in confirmation_candidates
    }
    confirmation_by_id: dict[str, dict[str, Any]] = {}
    if candidate_ids:
        if not isinstance(tail_confirmations, dict):
            raise PerformanceEvidenceError(
                "Historical p99 candidates require independent confirmations."
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
                "Tail confirmation result matrix does not match current p99 candidates."
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
            count != P99_CONFIRMATION_RUNS
            for count in artifact_counts.values()
        ):
            raise PerformanceEvidenceError(
                "Tail confirmation artifact counts do not match the required runs."
            )
    elif tail_confirmations is not None:
        raise PerformanceEvidenceError(
            "Tail confirmation evidence exists without a current p99 candidate."
        )

    for workload_id in sorted(current_by_id):
        current_workload = current_by_id[workload_id]
        baseline_workload = baseline_by_id[workload_id]
        if workload_id in confirmation_by_id:
            p99_check = validate_confirmed_p99_check(
                workload_id,
                current_workload,
                baseline_workload,
                confirmation_by_id[workload_id],
                contract,
            )
        else:
            p99_check = {
                **historical_p99_check(
                    workload_id,
                    current_workload,
                    baseline_workload,
                    contract,
                ),
                "confirmationRequired": False,
            }
        checks.append(p99_check)
        if not p99_check["passed"]:
            raise PerformanceEvidenceError(
                f"Historical budget failed for '{workload_id}' normalizedP99: "
                f"{p99_check['exceedanceCount']} of {p99_check['sampleCount']} "
                f"samples exceeded {p99_check['maximum']}; p-value "
                f"{p99_check['pValue']} is below "
                f"{p99_check['significanceLevel']}."
            )

    return checks


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


def validate_host_workload_binding(
    host_preflight: dict[str, Any],
    workload_environment: dict[str, Any],
) -> None:
    """Bind workload metadata to the exact accepted host preflight."""
    exact_fields = {
        "processor": "processor",
        "processorCount": "processorCount",
    }
    numeric_fields = {
        "loadAverage1Minute": "hostLoadAverage1Minute",
        "loadAverage5Minutes": "hostLoadAverage5Minutes",
        "loadAverage15Minutes": "hostLoadAverage15Minutes",
        "loadAverage1MinutePerProcessor": "hostLoadAverage1MinutePerProcessor",
    }

    for host_key, environment_key in exact_fields.items():
        if host_preflight.get(host_key) != workload_environment.get(environment_key):
            raise PerformanceEvidenceError(
                f"Host preflight and workload environment disagree on '{host_key}'."
            )

    for host_key, environment_key in numeric_fields.items():
        host_value = finite_number(
            host_preflight.get(host_key),
            f"hostPreflight.{host_key}",
            minimum=0,
        )
        environment_value = finite_number(
            workload_environment.get(environment_key),
            f"workloadReport.environment.{environment_key}",
            minimum=0,
        )
        if not close_enough(host_value, environment_value):
            raise PerformanceEvidenceError(
                f"Host preflight and workload environment disagree on '{host_key}'."
            )

    if (
        host_preflight.get("admissionMetric")
        != workload_environment.get("hostAdmissionMetric")
    ):
        raise PerformanceEvidenceError(
            "Host preflight and workload environment disagree on 'admissionMetric'."
        )
    for host_key, environment_key in (
        ("admittedCpuUtilization", "admittedHostCpuUtilization"),
        ("maximumCpuUtilization", "maximumHostCpuUtilization"),
    ):
        host_value = finite_number(
            host_preflight.get(host_key),
            f"hostPreflight.{host_key}",
            minimum=0,
        )
        environment_value = finite_number(
            workload_environment.get(environment_key),
            f"workloadReport.environment.{environment_key}",
            minimum=0,
        )
        if not close_enough(host_value, environment_value):
            raise PerformanceEvidenceError(
                "Host preflight and workload environment disagree on "
                f"'{host_key}'."
            )


def validate_bdn_workload_environment(
    bdn_host: dict[str, Any],
    workload_environment: dict[str, Any],
) -> None:
    """Reject same-run controls produced on a different processor or architecture."""
    if bdn_host.get("ProcessorName") != workload_environment.get("processor"):
        raise PerformanceEvidenceError(
            "BenchmarkDotNet and workload evidence report different processors."
        )
    if bdn_host.get("Architecture") != workload_environment.get("processArchitecture"):
        raise PerformanceEvidenceError(
            "BenchmarkDotNet and workload evidence report different process architectures."
        )
    if str(bdn_host.get("Configuration", "")).upper() != "RELEASE":
        raise PerformanceEvidenceError("BenchmarkDotNet evidence was not built in Release mode.")


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


def validate_seed_evaluation(
    evaluation: dict[str, Any],
    contract: dict[str, Any],
    contract_path: Path,
    *,
    maximum_age_hours: float | None = None,
) -> None:
    """Reject a seed record that is incomplete or cannot reproduce its verdicts."""
    if evaluation.get("schemaVersion") != 3 or evaluation.get("kind") != "performance-evaluation":
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
    for key in (
        "hostLoadAverage1Minute",
        "hostLoadAverage5Minutes",
        "hostLoadAverage15Minutes",
        "hostLoadAverage1MinutePerProcessor",
        "admittedHostCpuUtilization",
        "maximumHostCpuUtilization",
    ):
        finite_number(
            environment.get(key),
            f"seedEvaluation.environment.{key}",
            minimum=0,
        )
    if environment["serverImage"] != contract["requiredTargets"][target]["serverImage"]:
        raise PerformanceEvidenceError(
            f"Seed input target '{target}' has the wrong server image."
        )

    host_preflight = evaluation.get("hostPreflight")
    if not isinstance(host_preflight, dict):
        raise PerformanceEvidenceError("Seed input has no host preflight evidence.")
    validate_host_preflight(
        host_preflight,
        contract,
        maximum_age_hours=maximum_age_hours,
    )
    validate_host_workload_binding(host_preflight, environment)

    bdn_host_environment = evaluation.get("benchmarkDotNetHostEnvironment")
    if not isinstance(bdn_host_environment, dict):
        raise PerformanceEvidenceError(
            "Seed input has no BenchmarkDotNet host environment."
        )
    validate_bdn_workload_environment(bdn_host_environment, environment)

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
    expected_hash_keys = {
        "contract",
        "hostPreflight",
        "workloads",
        "benchmarkDotNet",
        "soak",
    }
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
        expected_sample_count = expected_measurement_sample_count(
            contract["profiles"][profile],
            expected[workload_id],
        )
        if sample_count < expected_sample_count:
            raise PerformanceEvidenceError(
                f"{label} workload '{workload_id}' has the wrong sampleCount."
            )
        measurement_duration_nanoseconds = workload.get(
            "measurementDurationNanoseconds"
        )
        if measurement_duration_nanoseconds is not None:
            actual_measurement_duration = finite_number(
                measurement_duration_nanoseconds,
                f"{label}.{workload_id}.measurementDurationNanoseconds",
                minimum=0,
            )
            minimum_measurement_duration = (
                int(
                    contract["profiles"][profile][
                        "minimumMeasurementDurationMilliseconds"
                    ]
                )
                * 1_000_000
            )
            if actual_measurement_duration < minimum_measurement_duration:
                raise PerformanceEvidenceError(
                    f"{label} workload '{workload_id}' has insufficient measurement duration."
                )
        normalized_median = finite_number(
            workload.get("normalizedMedian"),
            f"{label}.{workload_id}.normalizedMedian",
            minimum=0.000001,
        )
        normalized_standard_error = finite_number(
            workload.get("normalizedStandardError"),
            f"{label}.{workload_id}.normalizedStandardError",
            minimum=0,
        )
        relative_error = finite_number(
            workload.get("relativeStandardError"),
            f"{label}.{workload_id}.relativeStandardError",
            minimum=0,
        )
        if (
            not close_enough(
                relative_error,
                normalized_standard_error / normalized_median,
            )
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
        calibration_kind = required_string(
            workload,
            "calibrationKind",
            f"{label}.{workload_id}",
        )
        expected_calibration_kind = (
            "cpu"
            if workload["family"] in contract["calibration"]["cpuFamilies"]
            else "database"
        )
        if calibration_kind != expected_calibration_kind:
            raise PerformanceEvidenceError(
                f"{label} workload '{workload_id}' has the wrong calibrationKind."
            )
        calibration_relative_error = finite_number(
            workload.get("calibrationRelativeStandardError"),
            f"{label}.{workload_id}.calibrationRelativeStandardError",
            minimum=0,
        )
        maximum_calibration_relative_error = float(
            contract["profiles"][profile][
                "maximumCalibrationRelativeStandardError"
            ]
        )
        if calibration_relative_error > maximum_calibration_relative_error:
            raise PerformanceEvidenceError(
                f"{label} workload '{workload_id}' has unstable calibration."
            )
        for key in (
            "medianNanoseconds",
            "p95Nanoseconds",
            "p99Nanoseconds",
            "standardErrorNanoseconds",
            "calibrationMedianNanoseconds",
            "calibrationStandardErrorNanoseconds",
            "normalizedP95",
            "normalizedP99",
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
                "hostPreflight": evaluation.get("hostPreflight"),
                "sourceEvidenceSha256": sha256(Path(evidence_path)),
                "artifactHashes": evaluation.get("artifactHashes"),
                "rawReports": evaluation.get("rawReports"),
                "benchmarkDotNetHostEnvironment": evaluation.get(
                    "benchmarkDotNetHostEnvironment"
                ),
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
        existing = load_json(Path(merge_existing))
        if (
            existing.get("schemaVersion") != 3
            or existing.get("baselineState") != "accepted"
        ):
            raise PerformanceEvidenceError(
                "The existing baseline must use schemaVersion 3 and state accepted."
            )
        existing_contract_version = required_string(
            existing,
            "contractVersion",
            "existingBaseline",
        )
        existing_entries = existing.get("baselines")
        if not isinstance(existing_entries, list):
            raise PerformanceEvidenceError("The existing baseline has no baselines array.")
        if existing_contract_version == contract["contractVersion"]:
            validate_baseline_file(
                argparse.Namespace(
                    contract=args.contract,
                    baseline=merge_existing,
                )
            )
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
        # A contract revision changes the meaning of accepted evidence. The
        # incoming seed must already cover every target, so retaining entries
        # accepted under the old contract would create a mixed, invalid baseline.

    combined_entries = retained_entries + baseline_entries

    return {
        "schemaVersion": 3,
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
    if baseline.get("schemaVersion") != 3 or baseline.get("baselineState") != "accepted":
        raise PerformanceEvidenceError("Baseline must use schemaVersion 3 and state accepted.")
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
                "schemaVersion": 3,
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
        "schemaVersion": 3,
        "kind": "baseline-validation",
        "baselineVersion": required_string(baseline, "baselineVersion", "baseline"),
        "success": True,
        "targetCount": len(observed_keys),
        "runnerGroupCount": len(target_groups),
    }


def resolve_baseline_mode(args: argparse.Namespace) -> dict[str, Any]:
    """Resolve the cheapest valid scorecard mode before CI allocates its matrix."""
    contract = load_json(Path(args.contract))
    validate_contract(contract)
    profile = args.profile
    if profile not in contract["profiles"]:
        raise PerformanceEvidenceError(
            f"Unknown performance profile '{profile}'."
        )
    if contract["profiles"][profile].get("baselineRequired") is not True:
        raise PerformanceEvidenceError(
            f"Performance profile '{profile}' does not require a baseline."
        )

    runner_class = args.runner_class.strip()
    if not runner_class:
        raise PerformanceEvidenceError("Runner class must not be empty.")

    requested_mode = args.requested_mode
    baseline_path = Path(args.baseline)
    baseline_disposition: str
    automatic_mode: str

    if not baseline_path.is_file():
        automatic_mode = "seed"
        baseline_disposition = "baseline-missing"
    else:
        baseline = load_json(baseline_path)
        if (
            baseline.get("schemaVersion") != 3
            or baseline.get("baselineState") != "accepted"
        ):
            raise PerformanceEvidenceError(
                "Baseline must use schemaVersion 3 and state accepted."
            )
        baseline_contract_version = required_string(
            baseline,
            "contractVersion",
            "baseline",
        )
        entries = baseline.get("baselines")
        if not isinstance(entries, list):
            raise PerformanceEvidenceError("Baseline baselines must be an array.")

        if baseline_contract_version != contract["contractVersion"]:
            automatic_mode = "seed"
            baseline_disposition = "contract-version-mismatch"
        else:
            # Full validation must precede lookup. Otherwise a complete-looking
            # runner pair could hide duplicate or partial groups elsewhere.
            validate_baseline_file(
                argparse.Namespace(
                    contract=args.contract,
                    baseline=args.baseline,
                )
            )
            observed_targets = {
                entry["target"]
                for entry in entries
                if entry["profile"] == profile
                and entry["runnerClass"] == runner_class
            }
            required_targets = set(contract["requiredTargets"])
            if observed_targets == required_targets:
                automatic_mode = "compare"
                baseline_disposition = "accepted-runner-pair"
            elif not observed_targets:
                automatic_mode = "seed"
                baseline_disposition = "runner-pair-missing"
            else:
                # validate_baseline_file rejects this already. Keep the branch
                # explicit so this resolver remains fail-closed if that contract
                # changes independently in the future.
                raise PerformanceEvidenceError(
                    "Baseline contains a partial target pair for the requested runner."
                )

    if requested_mode == "compare" and automatic_mode != "compare":
        raise PerformanceEvidenceError(
            "Compare mode requires a current accepted baseline pair for "
            f"profile '{profile}' and runner '{runner_class}'; disposition is "
            f"'{baseline_disposition}'."
        )

    selected_mode = automatic_mode if requested_mode == "auto" else requested_mode
    selection_reason = (
        baseline_disposition
        if requested_mode == "auto"
        else "operator-requested"
    )

    return {
        "schemaVersion": 1,
        "kind": "performance-baseline-mode",
        "contractVersion": contract["contractVersion"],
        "profile": profile,
        "runnerClass": runner_class,
        "requestedMode": requested_mode,
        "mode": selected_mode,
        "reason": selection_reason,
        "baselineDisposition": baseline_disposition,
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
    evaluate_parser.add_argument("--host", required=True)
    evaluate_parser.add_argument("--workloads", required=True)
    evaluate_parser.add_argument("--soak")
    evaluate_parser.add_argument("--bdn", required=True)
    evaluate_parser.add_argument("--baseline", required=True)
    evaluate_parser.add_argument("--mode", choices=("compare", "seed"), required=True)

    tail_plan_parser = subparsers.add_parser("plan-tail-confirmation")
    add_common_identity_arguments(tail_plan_parser)
    tail_plan_parser.add_argument("--workloads", required=True)
    tail_plan_parser.add_argument("--baseline", required=True)

    tail_merge_parser = subparsers.add_parser("merge-tail-confirmations")
    add_common_identity_arguments(tail_merge_parser)
    tail_merge_parser.add_argument("--workloads", required=True)
    tail_merge_parser.add_argument("--plan", required=True)
    tail_merge_parser.add_argument("--confirmation", action="append", required=True)
    tail_merge_parser.add_argument(
        "--confirmation-host",
        action="append",
        required=True,
    )

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

    resolve_parser = subparsers.add_parser("resolve-baseline-mode")
    resolve_parser.add_argument("--contract", required=True)
    resolve_parser.add_argument("--baseline", required=True)
    resolve_parser.add_argument("--profile", required=True)
    resolve_parser.add_argument("--runner-class", required=True)
    resolve_parser.add_argument(
        "--requested-mode",
        choices=("auto", "compare", "seed"),
        default="auto",
    )
    resolve_parser.add_argument("--output", required=True)

    source_hash_parser = subparsers.add_parser("source-hash")
    source_hash_parser.add_argument("--repo", required=True)

    host_parser = subparsers.add_parser("host-preflight")
    host_parser.add_argument("--contract", required=True)
    host_parser.add_argument("--output", required=True)

    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Run one deterministic evidence command."""
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        if args.command == "source-hash":
            print(repository_source_hash(Path(args.repo)))
            return 0
        if args.command == "host-preflight":
            contract = load_json(Path(args.contract))
            payload = capture_host_preflight(contract)
            write_json(Path(args.output), payload)
            if payload["success"] is not True:
                observed_samples = ", ".join(
                    f"{sample['cpuUtilization']:.4f}"
                    for sample in payload["samples"]
                )
                print(
                    "Performance host preflight failed: interval host CPU "
                    "utilization remained above the admission ceiling. "
                    f"Samples: [{observed_samples}]; maximum: "
                    f"{payload['observedMaximumCpuUtilization']:.4f}; ceiling: "
                    f"{payload['maximumCpuUtilization']:.4f}.",
                    file=sys.stderr,
                )
                return 1
            print(f"Performance host preflight passed: {args.output}")
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
        elif args.command == "plan-tail-confirmation":
            payload = plan_tail_confirmation(args)
        elif args.command == "merge-tail-confirmations":
            payload = merge_tail_confirmations(args)
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
        elif args.command == "resolve-baseline-mode":
            payload = resolve_baseline_mode(args)
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
