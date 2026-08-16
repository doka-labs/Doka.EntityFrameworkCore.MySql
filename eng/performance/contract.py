#!/usr/bin/env python3
"""Versioned contracts and shared validation primitives for performance evidence."""

import hashlib
import json
import math
import re
import subprocess
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from typing import Any

BASELINE_PATH = Path("benchmarks/baselines/doka-benchmark-baseline.json")

# A contract revision is dated, and a second revision on the same day appends a
# counter. The shape is matched here and the date behind it is then checked as
# a real calendar date, because a shape alone accepts 2026-02-29 in a year that
# has no such day. One definition governs the format: the hosted benchmark
# workflow reads the version through this validator rather than applying a
# pattern of its own, which would otherwise disagree with it silently.
CONTRACT_VERSION = re.compile(
    r"(?P<date>[0-9]{4}-[0-9]{2}-[0-9]{2})(?:\.(?P<revision>[0-9]+))?"
)
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
MEASUREMENT_QUALITY_EXIT_CODE = 75
# A comparison can fail for reasons that are not a provider verdict. Each such
# reason gets its own exit code so the workflow can distinguish an
# infrastructure condition, which may retry, from a regression, which may not.
# The values continue the 75 range rather than reusing 1, which stays reserved
# for a conclusive gate failure.
ENVIRONMENT_NOT_COMPARABLE_EXIT_CODE = 76
RECALIBRATION_REQUIRED_EXIT_CODE = 77
INVALID_EVIDENCE_EXIT_CODE = 78
# These document types evolve independently even though two currently share a
# numeric version. Keeping their identities distinct prevents a change to one
# writer from silently changing an unrelated reader contract.
HISTORICAL_EVALUATION_SCHEMA_VERSION = 3
HISTORICAL_EVALUATION_KIND = "performance-evaluation"
PERFORMANCE_BASELINE_SCHEMA_VERSION = 3
PAIRED_EVALUATION_SCHEMA_VERSION = 6
PAIRED_EVALUATION_KIND = "paired-performance-evaluation"
# Why sampling stopped. The runner never exceeds the configured sample cap, so
# a capped run is a typed observation whose usability the quality policy
# decides. Mirrors the constants in PerformanceWorkloadRunner.
PRECISION_REACHED = "precision_reached"
SAMPLE_CAP_REACHED = "sample_cap_reached"
TERMINATION_REASONS = frozenset({PRECISION_REACHED, SAMPLE_CAP_REACHED})


class PerformanceEvidenceError(RuntimeError):
    """Raised when performance evidence violates the versioned contract."""


class MeasurementQualityError(PerformanceEvidenceError):
    """Raised when environmental noise prevents a conclusive measurement."""


class EnvironmentNotComparableError(PerformanceEvidenceError):
    """The comparator environment is not the measured environment.

    A hosted runner label is not a processor-model contract, so a historical
    comparator can legitimately describe different hardware. That is an
    infrastructure condition, never a provider regression.
    """


class RecalibrationRequiredError(PerformanceEvidenceError):
    """The accepted reference cannot run under the current contract.

    Raised when a reference provider, benchmark driver, runtime, or
    contract change
    invalidates the comparator itself, so no comparison exists to judge.
    """


class InvalidEvidenceError(PerformanceEvidenceError):
    """Evidence is incomplete, inconsistent, or not bound to its identity."""


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


def validate_contract_version(value: str) -> None:
    """Reject a contract version that is not a real date with an optional revision.

    The first revision of a day carries no counter, so a suffix starts at two.
    A version that merely looks like a date can otherwise reach the evidence:
    it would seed a baseline, name a proposal branch, and persist into release
    evidence while claiming a point in time that never existed.
    """
    parsed = CONTRACT_VERSION.fullmatch(value)
    if parsed is None:
        raise PerformanceEvidenceError(
            f"contract.contractVersion is '{value}', which is not a release date "
            "with an optional same-day revision, such as '2026-08-09' or "
            "'2026-08-09.2'."
        )

    try:
        date.fromisoformat(parsed.group("date"))
    except ValueError as error:
        raise PerformanceEvidenceError(
            f"contract.contractVersion is '{value}', whose date is not a real "
            "calendar date."
        ) from error

    revision = parsed.group("revision")
    if revision is None:
        return

    if revision != str(int(revision)) or int(revision) < 2:
        raise PerformanceEvidenceError(
            f"contract.contractVersion is '{value}'. A same-day revision counts "
            "from two, because the first revision of a day carries no suffix."
        )


# The paired policy is pre-registered: every value that can move a release
# decision is named here before any measurement runs. There are deliberately no
# implementation defaults, because a default would let a silently incomplete
# contract still produce a verdict.
# Each set holds exactly the procedures the evaluator implements. Listing an
# alternative the code cannot perform is worse than omitting it: the contract
# would validate a policy that reads as a deliberate choice while the run
# silently applies the only procedure there ever was, and the divergence would
# surface as a reviewer trusting a document the evidence does not support.
PAIRED_INTERVAL_METHODS = frozenset({"bca-bootstrap"})
PAIRED_MULTIPLE_COMPARISON_PROCEDURES = frozenset({"holm"})
PAIRED_RETRY_COMBINATIONS = frozenset({"replace-attempt"})
PAIRED_SENSITIVITY_METHODS = frozenset(
    {"deterministic-lognormal-bca-exact-sign-flip"}
)
PAIRED_SENSITIVITY_FAMILY_CASES = frozenset({"single-regression"})

# The paired comparison measures every registered workload; there is no
# narrowing path and no consumer for one.
PAIRED_WORKLOAD_SCOPES = frozenset({"complete-matrix"})
PAIRED_ENDPOINT_ROLES = frozenset({"required", "observational"})
PAIRED_PRIMARY_AGGREGATIONS = frozenset(
    {"geometric-mean-across-workloads"}
)
PAIRED_SECONDARY_AGGREGATIONS = frozenset({"per-workload"})
PAIRED_LATENCY_METRICS = frozenset(
    {"normalizedMedian", "normalizedP95", "normalizedP99"}
)

_PAIRED_POLICY_SHAPE: dict[str, tuple[str, ...]] = {
    "executionOrder": (
        "blockPatterns",
        "startingSideAlternatesPerBlock",
    ),
    "primaryFamily": ("workloadScope", "metric", "role", "aggregation"),
    "practicalBudgets": (),
    "interval": (
        "sidedness",
        "confidenceLevel",
        "method",
        "resampleCount",
        "resamplingSeed",
    ),
    "blocks": (
        "completeBlocks",
        "startingSamplesPerSidePerBlock",
        "maximumSampleCountRatio",
        "maximumRelativeStandardError",
        "profile",
    ),
    "multipleComparison": (
        "scope",
        "procedure",
        "familyWiseErrorRate",
    ),
    "sensitivity": (
        "method",
        "familyCase",
        "minimumPower",
        "simulationConfidenceLevel",
        "simulationTrials",
        "simulationSeed",
        "maximumLogRatioStandardDeviation",
        "minimumDetectableBudgetMultiple",
        "characterization",
    ),
    "retry": ("eligibleAttemptStates", "maximumRetries", "combination"),
    "durations": (
        "maximumPairedRunSeconds",
        "maximumWorkloadSeconds",
        "closingReserveSeconds",
        "finalizationReserveSeconds",
    ),
    "absoluteCeilings": ("appliesTo", "source", "metrics"),
    "soak": ("required", "appliesTo", "source"),
}

# Allocated bytes are stable enough to qualify a fixed code path against its
# paired reference. Gen2 collections are integer process events projected per
# 1,000 operations; one additional event can double a sparse observation, so
# their relative budget is diagnostic while the candidate's absolute ceiling
# remains the hard safety gate.
PAIRED_REQUIRED_RESOURCE_METRICS = frozenset({"allocatedBytesPerOperation"})
PAIRED_OBSERVATIONAL_RESOURCE_METRICS = frozenset({"gen2CollectionsPer1000"})
PAIRED_RESOURCE_METRICS = (
    PAIRED_REQUIRED_RESOURCE_METRICS | PAIRED_OBSERVATIONAL_RESOURCE_METRICS
)


def _paired_number(value: Any, label: str, *, minimum: float) -> float:
    """Validate one numeric policy value as evidence rather than as input.

    Every deviation from the registered policy is `invalid-evidence`, so the
    generic numeric guard is re-raised in that domain instead of surfacing as
    an ordinary contract error.
    """
    try:
        return finite_number(value, label, minimum=minimum)
    except PerformanceEvidenceError as error:
        raise InvalidEvidenceError(str(error)) from error


def validate_paired_policy(contract: dict[str, Any]) -> dict[str, Any]:
    """Validate the pre-registered paired-comparison policy.

    Every failure raises rather than falling back, so an incomplete policy can
    never produce a qualification. The caller maps that to `invalid-evidence`.
    """
    policy = contract.get("pairedPolicy")
    if not isinstance(policy, dict) or not policy:
        raise InvalidEvidenceError("Performance contract must define pairedPolicy.")

    for block, fields in _PAIRED_POLICY_SHAPE.items():
        section = policy.get(block)
        if not isinstance(section, dict) or not section:
            raise InvalidEvidenceError(f"pairedPolicy.{block} is required.")
        for field in fields:
            if field not in section:
                raise InvalidEvidenceError(
                    f"pairedPolicy.{block}.{field} is required."
                )

    families = policy.get("secondaryFamilies")
    if not isinstance(families, list):
        raise InvalidEvidenceError("pairedPolicy.secondaryFamilies is required.")

    scope = policy["primaryFamily"]["workloadScope"]
    if scope not in PAIRED_WORKLOAD_SCOPES:
        raise InvalidEvidenceError(
            f"pairedPolicy.primaryFamily.workloadScope '{scope}' is not a scope "
            "the comparison can measure."
        )

    primary_metric = policy["primaryFamily"]["metric"]
    metrics = [primary_metric]
    for index, family in enumerate(families):
        if not isinstance(family, dict) or "metric" not in family:
            raise InvalidEvidenceError(
                f"pairedPolicy.secondaryFamilies[{index}].metric is required."
            )
        metrics.append(family["metric"])
    for metric in metrics:
        if metric not in PAIRED_LATENCY_METRICS:
            raise InvalidEvidenceError(
                f"pairedPolicy declares unknown metric '{metric}'."
            )
    if len(set(metrics)) != len(metrics):
        raise InvalidEvidenceError("pairedPolicy declares a metric family twice.")

    primary_family = policy["primaryFamily"]
    if primary_family["role"] != "required":
        raise InvalidEvidenceError(
            "pairedPolicy.primaryFamily.role must be 'required'."
        )
    if primary_family["aggregation"] not in PAIRED_PRIMARY_AGGREGATIONS:
        raise InvalidEvidenceError(
            "pairedPolicy.primaryFamily.aggregation is not implemented."
        )
    for index, family in enumerate(families):
        if family.get("role") != "observational":
            raise InvalidEvidenceError(
                f"pairedPolicy.secondaryFamilies[{index}].role must be "
                "'observational'."
            )
        if family.get("aggregation") not in PAIRED_SECONDARY_AGGREGATIONS:
            raise InvalidEvidenceError(
                f"pairedPolicy.secondaryFamilies[{index}].aggregation is not "
                "implemented."
            )

    target_roles = policy.get("targetRoles")
    if not isinstance(target_roles, dict) or not target_roles:
        raise InvalidEvidenceError("pairedPolicy.targetRoles is required.")
    required_targets = contract.get("requiredTargets")
    if not isinstance(required_targets, dict) or set(target_roles) != set(
        required_targets
    ):
        raise InvalidEvidenceError(
            "pairedPolicy.targetRoles must classify every required target "
            "exactly once."
        )
    for target, role in target_roles.items():
        if role not in PAIRED_ENDPOINT_ROLES:
            raise InvalidEvidenceError(
                f"pairedPolicy.targetRoles.{target} has unknown role '{role}'."
            )
    if "required" not in target_roles.values():
        raise InvalidEvidenceError(
            "pairedPolicy.targetRoles must retain at least one required target."
        )

    # Every declared family needs its own practical bound. Without one, a
    # statistically detectable change would have nothing to be compared against
    # and the separation of practical from statistical significance collapses.
    budgets = policy["practicalBudgets"]
    for metric in metrics:
        _paired_number(
            budgets.get(metric),
            f"pairedPolicy.practicalBudgets.{metric}",
            minimum=1,
        )
    extra = sorted(set(budgets) - set(metrics))
    if extra:
        raise InvalidEvidenceError(
            f"pairedPolicy.practicalBudgets has no family for: {', '.join(extra)}."
        )

    interval = policy["interval"]
    if interval["sidedness"] not in ("one-sided", "two-sided"):
        raise InvalidEvidenceError("pairedPolicy.interval.sidedness is invalid.")
    if interval["method"] not in PAIRED_INTERVAL_METHODS:
        raise InvalidEvidenceError(
            f"pairedPolicy.interval.method '{interval['method']}' is unknown."
        )
    confidence = _paired_number(
        interval["confidenceLevel"], "pairedPolicy.interval.confidenceLevel",
        minimum=0,
    )
    if not 0 < confidence < 1:
        raise InvalidEvidenceError(
            "pairedPolicy.interval.confidenceLevel must lie between 0 and 1."
        )
    resamples = _paired_number(
        interval["resampleCount"], "pairedPolicy.interval.resampleCount", minimum=1
    )
    if resamples < 1000:
        raise InvalidEvidenceError(
            "pairedPolicy.interval.resampleCount must be at least 1000 so the "
            "interval is stable across runs."
        )
    _paired_number(
        interval["resamplingSeed"], "pairedPolicy.interval.resamplingSeed", minimum=0
    )

    blocks = policy["blocks"]
    complete_blocks = _paired_number(
        blocks["completeBlocks"],
        "pairedPolicy.blocks.completeBlocks",
        minimum=10,
    )
    if complete_blocks != int(complete_blocks):
        raise InvalidEvidenceError(
            "pairedPolicy.blocks.completeBlocks must be a whole number."
        )
    if complete_blocks != 10:
        raise InvalidEvidenceError(
            "pairedPolicy.blocks.completeBlocks must be exactly 10 for the "
            "registered exact sign-flip test."
        )
    samples = _paired_number(
        blocks["startingSamplesPerSidePerBlock"],
        "pairedPolicy.blocks.startingSamplesPerSidePerBlock",
        minimum=1,
    )

    # Where a block starts is registered; where it ends is measured. The
    # extension is driven by the profile's error budget and bounded by its
    # sample cap, and the achieved population travels in the report. Claiming a
    # fixed population here would describe a run that does not happen:
    # expensive workloads allocate differently and any workload may override.
    block_profile = contract.get("profiles", {}).get(blocks["profile"])
    if not isinstance(block_profile, dict):
        raise InvalidEvidenceError(
            f"pairedPolicy.blocks.profile '{blocks['profile']}' is not a "
            "registered profile."
        )
    starting = block_profile.get("measurementSamples")
    if starting != samples:
        raise InvalidEvidenceError(
            f"pairedPolicy.blocks.startingSamplesPerSidePerBlock is {samples:g} "
            f"while profile '{blocks['profile']}' starts at {starting}; the "
            "paired policy and the profile must start from the same population."
        )
    floor = block_profile.get("minimumValidSamples")
    if not isinstance(floor, int) or samples < floor:
        raise InvalidEvidenceError(
            f"pairedPolicy.blocks.startingSamplesPerSidePerBlock is {samples:g}, "
            f"below the {floor} samples profile '{blocks['profile']}' requires "
            "for a valid measurement."
        )

    # The execution order is a registered plan, not a description. Every
    # pattern has to cover both sides equally, or the counterbalancing the
    # pairing depends on would be an assumption instead of a property.
    order = policy["executionOrder"]
    patterns = order["blockPatterns"]
    if not isinstance(patterns, list) or not patterns:
        raise InvalidEvidenceError(
            "pairedPolicy.executionOrder.blockPatterns must name at least one "
            "pattern."
        )
    for index, pattern in enumerate(patterns):
        sides = str(pattern).split("-")
        if len(sides) < 2 or set(sides) != {"A", "B"}:
            raise InvalidEvidenceError(
                f"pairedPolicy.executionOrder.blockPatterns[{index}] "
                f"'{pattern}' is not an A/B execution pattern."
            )
        if sides.count("A") != sides.count("B"):
            raise InvalidEvidenceError(
                f"pairedPolicy.executionOrder.blockPatterns[{index}] "
                f"'{pattern}' does not measure both sides equally often."
            )
    if order["startingSideAlternatesPerBlock"] is not True:
        raise InvalidEvidenceError(
            "pairedPolicy.executionOrder.startingSideAlternatesPerBlock must "
            "be true; a fixed starting side gives one provider every warm-up "
            "advantage in the run."
        )
    starting = {str(pattern).split("-")[0] for pattern in patterns}
    if starting != {"A", "B"}:
        raise InvalidEvidenceError(
            "pairedPolicy.executionOrder.blockPatterns must offer a pattern "
            "starting with each side so the starting side can alternate."
        )
    _paired_number(
        blocks["maximumRelativeStandardError"],
        "pairedPolicy.blocks.maximumRelativeStandardError",
        minimum=0,
    )
    ratio = _paired_number(
        blocks["maximumSampleCountRatio"],
        "pairedPolicy.blocks.maximumSampleCountRatio",
        minimum=1,
    )
    if ratio < 1:
        raise InvalidEvidenceError(
            "pairedPolicy.blocks.maximumSampleCountRatio must be at least 1."
        )

    procedure = policy["multipleComparison"]["procedure"]
    if procedure not in PAIRED_MULTIPLE_COMPARISON_PROCEDURES:
        raise InvalidEvidenceError(
            f"pairedPolicy.multipleComparison.procedure '{procedure}' is unknown."
        )
    if policy["multipleComparison"]["scope"] != "all-required-targets":
        raise InvalidEvidenceError(
            "pairedPolicy.multipleComparison.scope must be "
            "'all-required-targets'."
        )
    rate = _paired_number(
        policy["multipleComparison"]["familyWiseErrorRate"],
        "pairedPolicy.multipleComparison.familyWiseErrorRate",
        minimum=0,
    )
    if not 0 < rate < 1:
        raise InvalidEvidenceError(
            "pairedPolicy.multipleComparison.familyWiseErrorRate must lie between "
            "0 and 1."
        )

    sensitivity = policy["sensitivity"]
    if sensitivity["method"] not in PAIRED_SENSITIVITY_METHODS:
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.method "
            f"'{sensitivity['method']}' is unknown."
        )
    if sensitivity["familyCase"] not in PAIRED_SENSITIVITY_FAMILY_CASES:
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.familyCase "
            f"'{sensitivity['familyCase']}' is unknown."
        )
    minimum_power = _paired_number(
        sensitivity["minimumPower"],
        "pairedPolicy.sensitivity.minimumPower",
        minimum=0,
    )
    if not 0.8 <= minimum_power < 1:
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.minimumPower must be at least 0.8 and "
            "less than 1."
        )
    simulation_confidence = _paired_number(
        sensitivity["simulationConfidenceLevel"],
        "pairedPolicy.sensitivity.simulationConfidenceLevel",
        minimum=0,
    )
    if not 0.95 <= simulation_confidence < 1:
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.simulationConfidenceLevel must be at "
            "least 0.95 and less than 1."
        )
    simulation_trials = _paired_number(
        sensitivity["simulationTrials"],
        "pairedPolicy.sensitivity.simulationTrials",
        minimum=200,
    )
    if simulation_trials != int(simulation_trials):
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.simulationTrials must be a whole number."
        )
    simulation_seed = _paired_number(
        sensitivity["simulationSeed"],
        "pairedPolicy.sensitivity.simulationSeed",
        minimum=0,
    )
    if simulation_seed != int(simulation_seed):
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.simulationSeed must be a whole number."
        )
    log_ratio_deviation = _paired_number(
        sensitivity["maximumLogRatioStandardDeviation"],
        "pairedPolicy.sensitivity.maximumLogRatioStandardDeviation",
        minimum=0,
    )
    if log_ratio_deviation <= 0:
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.maximumLogRatioStandardDeviation must "
            "be positive."
        )
    detectable_multiple = _paired_number(
        sensitivity["minimumDetectableBudgetMultiple"],
        "pairedPolicy.sensitivity.minimumDetectableBudgetMultiple",
        minimum=1,
    )
    if detectable_multiple <= 1:
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.minimumDetectableBudgetMultiple must "
            "exceed 1."
        )

    characterization = sensitivity["characterization"]
    if not isinstance(characterization, dict):
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.characterization is required."
        )
    characterization_path = characterization.get("path")
    if (
        not isinstance(characterization_path, str)
        or not characterization_path.startswith("benchmarks/characterization/")
        or not characterization_path.endswith(".json")
    ):
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.characterization.path must name a "
            "repository characterization JSON document."
        )
    characterization_digest = characterization.get("sha256")
    if not isinstance(characterization_digest, str) or not re.fullmatch(
        r"[0-9a-f]{64}", characterization_digest
    ):
        raise InvalidEvidenceError(
            "pairedPolicy.sensitivity.characterization.sha256 must be a "
            "lowercase SHA-256 digest."
        )

    retry = policy["retry"]
    eligible = retry["eligibleAttemptStates"]
    if not isinstance(eligible, list) or not eligible:
        raise InvalidEvidenceError(
            "pairedPolicy.retry.eligibleAttemptStates must name at least one state."
        )
    for state in eligible:
        if state not in ("measurement-inconclusive", "environment-not-comparable"):
            raise InvalidEvidenceError(
                f"pairedPolicy.retry may not make '{state}' retryable; a retry "
                "would select away a verdict about the code."
            )

    # The registered states have to be the states the recorder actually treats
    # as retryable. Two lists describing one decision are free to drift, and
    # the contract would keep describing a policy nothing applies.
    if __package__:
        from .attempts import MAXIMUM_ATTEMPTS, RETRYABLE_STATES
    else:  # pragma: no cover - direct execution path
        from attempts import MAXIMUM_ATTEMPTS, RETRYABLE_STATES
    if set(eligible) != set(RETRYABLE_STATES):
        raise InvalidEvidenceError(
            "pairedPolicy.retry.eligibleAttemptStates is "
            f"{sorted(eligible)} while the attempt recorder retries "
            f"{sorted(RETRYABLE_STATES)}."
        )
    registered_retries = _paired_number(
        retry["maximumRetries"], "pairedPolicy.retry.maximumRetries", minimum=0
    )
    if registered_retries + 1 != MAXIMUM_ATTEMPTS:
        raise InvalidEvidenceError(
            f"pairedPolicy.retry.maximumRetries is {registered_retries:g} while "
            f"the attempt path bounds a run at {MAXIMUM_ATTEMPTS} attempts."
        )
    if retry["combination"] not in PAIRED_RETRY_COMBINATIONS:
        raise InvalidEvidenceError(
            f"pairedPolicy.retry.combination '{retry['combination']}' is unknown."
        )

    durations = policy["durations"]
    run_seconds = _paired_number(
        durations["maximumPairedRunSeconds"],
        "pairedPolicy.durations.maximumPairedRunSeconds",
        minimum=1,
    )
    workload_seconds = _paired_number(
        durations["maximumWorkloadSeconds"],
        "pairedPolicy.durations.maximumWorkloadSeconds",
        minimum=1,
    )
    closing_reserve = _paired_number(
        durations["closingReserveSeconds"],
        "pairedPolicy.durations.closingReserveSeconds",
        minimum=1,
    )
    finalization_reserve = _paired_number(
        durations["finalizationReserveSeconds"],
        "pairedPolicy.durations.finalizationReserveSeconds",
        minimum=1,
    )
    if finalization_reserve >= closing_reserve:
        raise InvalidEvidenceError(
            f"pairedPolicy.durations.finalizationReserveSeconds is "
            f"{finalization_reserve:g}, which leaves nothing of the "
            f"{closing_reserve:g}s closing reserve for the sustained-use run."
        )
    if closing_reserve >= run_seconds:
        raise InvalidEvidenceError(
            f"pairedPolicy.durations.closingReserveSeconds is {closing_reserve:g}, "
            f"which leaves nothing of the {run_seconds:g}s paired budget for "
            "measuring."
        )

    # These two are bounds on the profile the blocks run under, not a second
    # set of timers. The driver enforces the profile; the policy states what
    # the profile is allowed to be. Stating a limit that nothing compares
    # against is how a registered value ends up describing nothing.
    profile_total = block_profile.get("maximumTotalDurationSeconds")
    if not isinstance(profile_total, (int, float)) or profile_total > run_seconds:
        raise InvalidEvidenceError(
            f"Profile '{blocks['profile']}' allows {profile_total} seconds per "
            f"block, above the registered paired run budget of {run_seconds:g}."
        )
    profile_workload = block_profile.get("maximumWorkloadDurationSeconds")
    if (
        not isinstance(profile_workload, (int, float))
        or profile_workload > workload_seconds
    ):
        raise InvalidEvidenceError(
            f"Profile '{blocks['profile']}' allows {profile_workload} seconds "
            f"per workload, above the registered paired ceiling of "
            f"{workload_seconds:g}."
        )


    # A ratio-only comparison qualifies a candidate that is no worse than its
    # reference, which says nothing about whether either is acceptable. The
    # absolute ceilings keep the released provider bound to the same budgets
    # the historical gate enforced, so a pair that regressed together is still
    # rejected.
    ceilings = policy["absoluteCeilings"]
    if ceilings["appliesTo"] != "candidate":
        raise InvalidEvidenceError(
            "pairedPolicy.absoluteCeilings.appliesTo must be 'candidate'; the "
            "released provider is the one that must satisfy the budget."
        )
    if ceilings["source"] != "familyBudgets":
        raise InvalidEvidenceError(
            "pairedPolicy.absoluteCeilings.source must be 'familyBudgets'."
        )
    ceiling_metrics = ceilings["metrics"]
    if not isinstance(ceiling_metrics, list) or not ceiling_metrics:
        raise InvalidEvidenceError(
            "pairedPolicy.absoluteCeilings.metrics must name at least one budget."
        )
    for metric in ceiling_metrics:
        for family, budget in contract.get("familyBudgets", {}).items():
            if metric not in budget:
                raise InvalidEvidenceError(
                    f"familyBudgets.{family} has no '{metric}' for the paired "
                    "absolute ceiling."
                )

    resources = policy.get("resourceFamilies")
    if not isinstance(resources, list) or not resources:
        raise InvalidEvidenceError(
            "pairedPolicy.resourceFamilies must guard at least one resource."
        )
    seen_resources = set()
    for index, family in enumerate(resources):
        if not isinstance(family, dict):
            raise InvalidEvidenceError(
                f"pairedPolicy.resourceFamilies[{index}] is not an object."
            )
        for field in ("metric", "budget"):
            if field not in family:
                raise InvalidEvidenceError(
                    f"pairedPolicy.resourceFamilies[{index}].{field} is required."
                )
        if family["metric"] not in PAIRED_RESOURCE_METRICS:
            raise InvalidEvidenceError(
                f"pairedPolicy.resourceFamilies[{index}].metric "
                f"'{family['metric']}' is not a resource metric."
            )
        if family["metric"] in seen_resources:
            raise InvalidEvidenceError(
                f"pairedPolicy.resourceFamilies declares '{family['metric']}' twice."
            )
        seen_resources.add(family["metric"])
        _paired_number(
            family["budget"],
            f"pairedPolicy.resourceFamilies[{index}].budget",
            minimum=1,
        )
    if seen_resources != PAIRED_RESOURCE_METRICS:
        missing = sorted(PAIRED_RESOURCE_METRICS - seen_resources)
        unexpected = sorted(seen_resources - PAIRED_RESOURCE_METRICS)
        raise InvalidEvidenceError(
            "pairedPolicy.resourceFamilies must register the complete resource "
            f"metric set; missing={missing}, unexpected={unexpected}."
        )

    soak = policy["soak"]
    if soak["required"] is not True:
        raise InvalidEvidenceError(
            "pairedPolicy.soak.required must be true; sustained-use evidence is "
            "not produced by a block comparison and would otherwise be lost."
        )
    if soak["appliesTo"] != "candidate":
        raise InvalidEvidenceError(
            "pairedPolicy.soak.appliesTo must be 'candidate'."
        )
    if soak["source"] != "soakBudgets":
        raise InvalidEvidenceError("pairedPolicy.soak.source must be 'soakBudgets'.")

    return policy


def validate_contract(contract: dict[str, Any]) -> None:
    """Validate uniqueness, references, and required dimension coverage."""
    if contract.get("schemaVersion") != 10:
        raise PerformanceEvidenceError("Performance contract schemaVersion must be 10.")

    validate_contract_version(
        required_string(contract, "contractVersion", "contract")
    )
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
    validate_paired_policy(contract)

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
        for key in ("displayName", "engineFamily", "serverVersion", "serverImage"):
            required_string(target_contract, key, f"requiredTargets.{target_id}")
        host_port = required_positive_integer(
            target_contract,
            "hostPort",
            f"requiredTargets.{target_id}",
        )
        if host_port > 65_535:
            raise PerformanceEvidenceError(
                f"requiredTargets.{target_id}.hostPort must be a TCP port."
            )

    required_profile_fields = (
        "warmupSamples",
        "measurementSamples",
        "expensiveMeasurementSamples",
        "minimumValidSamples",
        "minimumBenchmarkDotNetSamples",
        "maximumMeasurementSampleMultiplier",
        "maximumOperationsPerSampleMultiplier",
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
        adaptive_operations = profile_contract.get("adaptiveOperationsPerSample")
        if not isinstance(adaptive_operations, bool):
            raise PerformanceEvidenceError(
                f"profiles.{profile_name}.adaptiveOperationsPerSample must be "
                "a boolean."
            )
        duration_headroom_percent = finite_number(
            profile_contract.get("operationBatchingDurationHeadroomPercent"),
            f"profiles.{profile_name}.operationBatchingDurationHeadroomPercent",
            minimum=100,
        )
        if not duration_headroom_percent.is_integer():
            raise PerformanceEvidenceError(
                f"profiles.{profile_name}.operationBatchingDurationHeadroomPercent "
                "must be an integer."
            )
        maximum_pilot_samples = finite_number(
            profile_contract.get("operationBatchingPilotSamples"),
            f"profiles.{profile_name}.operationBatchingPilotSamples",
            minimum=0,
        )
        if not maximum_pilot_samples.is_integer():
            raise PerformanceEvidenceError(
                f"profiles.{profile_name}.operationBatchingPilotSamples "
                "must be an integer."
            )
        if adaptive_operations:
            if minimum_measurement_duration <= 0:
                raise PerformanceEvidenceError(
                    f"profiles.{profile_name} enables adaptive operation "
                    "batching without a positive minimum measurement duration."
                )
            if maximum_pilot_samples <= 0:
                raise PerformanceEvidenceError(
                    f"profiles.{profile_name} enables adaptive operation "
                    "batching without pilot samples."
                )
        elif (
            duration_headroom_percent != 100
            or maximum_pilot_samples != 0
            or profile_contract["maximumOperationsPerSampleMultiplier"] != 1
        ):
            raise PerformanceEvidenceError(
                f"profiles.{profile_name} configures adaptive operation-batching "
                "knobs while adaptiveOperationsPerSample is false."
            )
        finite_number(
            profile_contract.get("maximumRelativeStandardError"),
            f"profiles.{profile_name}.maximumRelativeStandardError",
            minimum=0,
        )
        finite_number(
            profile_contract.get("maximumCalibrationRelativeStandardError"),
            f"profiles.{profile_name}.maximumCalibrationRelativeStandardError",
            minimum=0,
        )
        measurement_quality_policy = required_string(
            profile_contract,
            "measurementQualityPolicy",
            f"profiles.{profile_name}",
        )
        if measurement_quality_policy not in {"observe", "enforce"}:
            raise PerformanceEvidenceError(
                f"profiles.{profile_name}.measurementQualityPolicy must be "
                "'observe' or 'enforce'."
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
        for profile_name, profile_contract in profiles.items():
            if (
                profile_contract["adaptiveOperationsPerSample"]
                and operations_per_sample
                * profile_contract["maximumOperationsPerSampleMultiplier"]
                > 2_147_483_647
            ):
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' adaptive operation batch exceeds "
                    f"the runner's integer range under profile '{profile_name}'."
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
