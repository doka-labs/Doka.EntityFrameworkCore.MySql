#!/usr/bin/env python3
"""Validate, compare, promote, and resolve performance baselines."""

import argparse
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Sequence

if __package__:
    from .confirmation import validate_historical_budgets
    from .contract import (
        SOAK_SCENARIO_IDS,
        PerformanceEvidenceError,
        applicable_workloads,
        close_enough,
        expected_measurement_sample_count,
        finite_number,
        load_json,
        required_commit,
        required_current_timestamp,
        required_positive_integer,
        required_sha256,
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
    from .reports import validate_absolute_budgets
    from .soak import validate_soak_scenario
else:
    from confirmation import validate_historical_budgets
    from contract import (
        SOAK_SCENARIO_IDS,
        PerformanceEvidenceError,
        applicable_workloads,
        close_enough,
        expected_measurement_sample_count,
        finite_number,
        load_json,
        required_commit,
        required_current_timestamp,
        required_positive_integer,
        required_sha256,
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
    from reports import validate_absolute_budgets
    from soak import validate_soak_scenario


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


def validate_compare_evaluation(
    evaluation: dict[str, Any],
    contract: dict[str, Any],
    contract_path: Path,
    baseline: dict[str, Any],
    *,
    maximum_age_hours: float | None = None,
) -> None:
    """Validate a compare result before promoting it to the accepted baseline."""
    if evaluation.get("mode") != "compare" or evaluation.get("success") is not True:
        raise PerformanceEvidenceError(
            "Promotion input must be a successful compare-mode evaluation."
        )
    if evaluation.get("baselineVersion") != baseline.get("baselineVersion"):
        raise PerformanceEvidenceError(
            "Compare input baselineVersion does not match the accepted baseline."
        )

    target = required_string(evaluation, "target", "compareEvaluation")
    profile = required_string(evaluation, "profile", "compareEvaluation")
    runner_class = required_string(
        evaluation,
        "runnerClass",
        "compareEvaluation",
    )
    matching_entries = [
        entry
        for entry in baseline.get("baselines", [])
        if isinstance(entry, dict)
        and entry.get("target") == target
        and entry.get("profile") == profile
        and entry.get("runnerClass") == runner_class
    ]
    if len(matching_entries) != 1:
        raise PerformanceEvidenceError(
            "Compare input does not identify exactly one accepted baseline entry."
        )

    # Structural and absolute-budget validation is identical for seed and
    # compare evidence. Reusing the seed validator prevents those contracts
    # from drifting while historical checks remain compare-specific below.
    validate_seed_evaluation(
        {
            **evaluation,
            "mode": "seed",
            "historicalChecks": [],
        },
        contract,
        contract_path,
        maximum_age_hours=maximum_age_hours,
    )

    baseline_entry = matching_entries[0]
    environment = evaluation.get("environment")
    if not isinstance(environment, dict):
        raise PerformanceEvidenceError("Compare input has no environment evidence.")
    baseline_environment = baseline_entry.get("environment")
    if not isinstance(baseline_environment, dict):
        raise PerformanceEvidenceError(
            "Accepted baseline entry has no environment evidence."
        )
    validate_environment_compatibility(environment, baseline_environment)

    workloads = evaluation.get("workloads")
    if not isinstance(workloads, list):
        raise PerformanceEvidenceError("Compare input has no normalized workloads.")
    expected_checks = validate_historical_budgets(
        workloads,
        baseline_entry,
        contract,
        evaluation.get("tailConfirmations"),
    )
    if evaluation.get("historicalChecks") != expected_checks:
        raise PerformanceEvidenceError(
            "Compare input historical checks cannot be reproduced."
        )


def promote_baseline(
    args: argparse.Namespace,
    *,
    required_mode: str | None = None,
) -> dict[str, Any]:
    """Promote one complete, successful hosted pair to the accepted baseline."""
    contract = load_json(Path(args.contract))
    validate_contract(contract)
    evaluations = [load_json(Path(path)) for path in args.evidence]
    required_targets = set(contract["requiredTargets"])
    observed_targets: set[str] = set()
    baseline_entries: list[dict[str, Any]] = []
    merge_existing = getattr(args, "merge_existing", None)
    existing = load_json(Path(merge_existing)) if merge_existing else None

    modes = {evaluation.get("mode") for evaluation in evaluations}
    if len(modes) != 1 or modes.pop() not in {"seed", "compare"}:
        raise PerformanceEvidenceError(
            "Baseline promotion requires one homogeneous seed or compare pair."
        )
    mode = evaluations[0]["mode"]
    if required_mode is not None and mode != required_mode:
        raise PerformanceEvidenceError(
            f"Baseline command requires {required_mode}-mode evidence."
        )
    if mode == "compare" and existing is None:
        raise PerformanceEvidenceError(
            "Compare-mode promotion requires the accepted baseline."
        )

    identities: set[tuple[str, str, str, str, str]] = set()

    for evidence_path, evaluation in zip(args.evidence, evaluations, strict=True):
        if mode == "seed":
            validate_seed_evaluation(
                evaluation,
                contract,
                Path(args.contract),
                maximum_age_hours=float(contract["evidenceMaximumAgeHours"]),
            )
        else:
            assert existing is not None
            validate_compare_evaluation(
                evaluation,
                contract,
                Path(args.contract),
                existing,
                maximum_age_hours=float(contract["evidenceMaximumAgeHours"]),
            )

        target = required_string(evaluation, "target", "performanceEvaluation")
        if target in observed_targets:
            raise PerformanceEvidenceError(
                f"Promotion input contains duplicate target '{target}'."
            )
        observed_targets.add(target)
        identities.add(
            (
                required_string(evaluation, "profile", "performanceEvaluation"),
                required_string(
                    evaluation,
                    "runnerClass",
                    "performanceEvaluation",
                ),
                required_string(evaluation, "commit", "performanceEvaluation"),
                required_sha256(
                    evaluation,
                    "sourceHash",
                    "performanceEvaluation",
                ),
                required_string(evaluation, "runId", "performanceEvaluation"),
            )
        )
        baseline_entries.append(
            {
                "target": target,
                "profile": required_string(
                    evaluation,
                    "profile",
                    "performanceEvaluation",
                ),
                "runnerClass": required_string(
                    evaluation,
                    "runnerClass",
                    "performanceEvaluation",
                ),
                "commit": required_string(
                    evaluation,
                    "commit",
                    "performanceEvaluation",
                ),
                "sourceHash": required_sha256(
                    evaluation,
                    "sourceHash",
                    "performanceEvaluation",
                ),
                "runId": required_string(
                    evaluation,
                    "runId",
                    "performanceEvaluation",
                ),
                "generatedUtc": required_string(
                    evaluation,
                    "generatedUtc",
                    "performanceEvaluation",
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

    if len(identities) != 1:
        raise PerformanceEvidenceError(
            "Promotion evidence must share one profile, runner, commit, source hash, and run ID."
        )

    if observed_targets != required_targets:
        missing = sorted(required_targets - observed_targets)
        unknown = sorted(observed_targets - required_targets)
        operation = "seed" if required_mode == "seed" else "promotion"
        raise PerformanceEvidenceError(
            f"Baseline {operation} target drift. Missing: [{', '.join(missing)}]. "
            f"Unknown: [{', '.join(unknown)}]."
        )

    retained_entries: list[dict[str, Any]] = []
    if merge_existing:
        assert existing is not None
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


def seed_baseline(args: argparse.Namespace) -> dict[str, Any]:
    """Create an explicit accepted baseline from successful seed evaluations."""
    return promote_baseline(args, required_mode="seed")


_BASELINE_PROVENANCE_FIELDS = frozenset(
    {
        "acceptedUtc",
        "admittedHostCpuUtilization",
        "artifactHashes",
        "baselineVersion",
        "commit",
        "generatedUtc",
        "hostLoadAverage15Minutes",
        "hostLoadAverage1Minute",
        "hostLoadAverage1MinutePerProcessor",
        "hostLoadAverage5Minutes",
        "hostPreflight",
        "maximumHostCpuUtilization",
        "measuredUtc",
        "rawReports",
        "runId",
        "sourceEvidenceSha256",
        "sourceHash",
    },
)


def _baseline_acceptance_projection(value: Any) -> Any:
    """Remove volatile provenance while preserving the accepted contract."""
    if isinstance(value, dict):
        return {
            key: _baseline_acceptance_projection(member)
            for key, member in value.items()
            if key not in _BASELINE_PROVENANCE_FIELDS
        }
    if isinstance(value, list):
        return [_baseline_acceptance_projection(member) for member in value]
    return value


def compare_baseline_files(args: argparse.Namespace) -> dict[str, Any]:
    """Report whether a candidate changes accepted baseline semantics.

    Run identifiers, timestamps, hashes, and transient host-load samples remain
    available in immutable evidence artifacts, but cannot create review noise by
    themselves. Stable environment identity, workloads, statistics, budgets,
    and benchmark controls remain part of the reviewed acceptance contract.
    """
    candidate_path = Path(args.candidate)
    validate_baseline_file(
        argparse.Namespace(
            contract=args.contract,
            baseline=candidate_path,
        ),
    )

    current_path = Path(args.current)
    if not current_path.is_file():
        return {
            "schemaVersion": 1,
            "kind": "performance-baseline-comparison",
            "changed": True,
            "disposition": "accepted-baseline-missing",
            "ignoredFields": sorted(_BASELINE_PROVENANCE_FIELDS),
        }

    validate_baseline_file(
        argparse.Namespace(
            contract=args.contract,
            baseline=current_path,
        ),
    )
    current = _baseline_acceptance_projection(load_json(current_path))
    candidate = _baseline_acceptance_projection(load_json(candidate_path))
    changed = current != candidate
    return {
        "schemaVersion": 1,
        "kind": "performance-baseline-comparison",
        "changed": changed,
        "disposition": "contract-changed" if changed else "provenance-only",
        "ignoredFields": sorted(_BASELINE_PROVENANCE_FIELDS),
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
