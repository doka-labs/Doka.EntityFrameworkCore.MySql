#!/usr/bin/env python3
"""Versioned contracts and shared validation primitives for performance evidence."""

import hashlib
import json
import math
import subprocess
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
LATENCY_CONFIRMATION_RUNS = 2
P99_CONFIRMATION_RUNS = LATENCY_CONFIRMATION_RUNS
P99_EXPECTED_EXCEEDANCE_PROBABILITY = 0.01
P99_SIGNIFICANCE_LEVEL = 0.01
LATENCY_METRICS = {
    "normalizedMedian": ("normalizedMedianRatio", 0.50),
    "normalizedP95": ("normalizedP95Ratio", 0.95),
    "normalizedP99": ("normalizedP99Ratio", 0.99),
}
HOST_ADMISSION_METRIC = "interval-host-cpu-utilization"


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


def applicable_workloads(contract: dict[str, Any], profile: str) -> list[dict[str, Any]]:
    """Return the exact workload set required for one profile."""
    profiles = contract["profiles"]
    if profile not in profiles:
        raise PerformanceEvidenceError(f"Unknown performance profile '{profile}'.")

    workloads = contract["workloads"]
    if profile == "smoke":
        workloads = [workload for workload in workloads if workload.get("smoke") is True]

    return sorted(workloads, key=lambda workload: workload["id"])

def close_enough(actual: float, expected: float) -> bool:
    """Compare derived floating-point evidence with a tight serialization tolerance."""
    return math.isclose(actual, expected, rel_tol=1e-9, abs_tol=1e-6)
