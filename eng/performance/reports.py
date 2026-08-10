#!/usr/bin/env python3
"""Validation for measured workload reports and absolute budgets."""

from typing import Any, Sequence

if __package__:
    from .contract import (
        HOST_ADMISSION_METRIC,
        MeasurementQualityError,
        PerformanceEvidenceError,
        SAMPLE_CAP_REACHED,
        TERMINATION_REASONS,
        applicable_workloads,
        close_enough,
        expected_measurement_sample_count,
        expected_warmup_sample_count,
        finite_number,
        non_negative_integer,
        require_identity,
        required_commit,
        required_current_timestamp,
        required_positive_integer,
        required_sha256,
        required_string,
    )
    from .statistics import percentile, standard_error
else:
    from contract import (
        HOST_ADMISSION_METRIC,
        MeasurementQualityError,
        PerformanceEvidenceError,
        SAMPLE_CAP_REACHED,
        TERMINATION_REASONS,
        applicable_workloads,
        close_enough,
        expected_measurement_sample_count,
        expected_warmup_sample_count,
        finite_number,
        non_negative_integer,
        require_identity,
        required_commit,
        required_current_timestamp,
        required_positive_integer,
        required_sha256,
        required_string,
    )
    from statistics import percentile, standard_error


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
    schema_version = report.get("schemaVersion")
    if schema_version == 3:
        raise PerformanceEvidenceError(
            "Workload report declares schema version 3, which predates the "
            "required terminationReason and minimumDurationReached fields. "
            "Re-measure with the current benchmark build to produce version 4."
        )
    if schema_version != 4 or report.get("kind") != "performance-workloads":
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

        # The runner cannot exceed this bound, so a larger population means the
        # evidence did not come from the reviewed sampling loop.
        maximum_sample_count = expected_sample_count * int(
            profile_contract["maximumMeasurementSampleMultiplier"]
        )
        if sample_count > maximum_sample_count:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' has {sample_count} samples, above "
                f"the contract maximum of {maximum_sample_count}."
            )

        termination_reason = required_string(
            entry,
            "terminationReason",
            workload_id,
        )
        if termination_reason not in TERMINATION_REASONS:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' reports unknown termination reason "
                f"'{termination_reason}'."
            )

        minimum_duration_reached = entry.get("minimumDurationReached")
        if not isinstance(minimum_duration_reached, bool):
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' must report minimumDurationReached "
                "as a boolean."
            )

        # The two fields describe one outcome, so a combination the runner
        # cannot produce is corrupt evidence rather than a poor measurement.
        if not minimum_duration_reached and termination_reason != SAMPLE_CAP_REACHED:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' reports an unreached minimum "
                f"duration with termination reason '{termination_reason}'. "
                "Only a capped run can miss the duration target."
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
        # A short measurement is only legitimate when the cap stopped sampling,
        # and the reported flag must agree with the samples themselves. Failing
        # here unconditionally would make every genuine capped run look corrupt
        # and would hide it from the quality policy that is meant to judge it.
        duration_satisfied = (
            measurement_duration_nanoseconds >= minimum_measurement_duration_nanoseconds
        )
        if duration_satisfied != minimum_duration_reached:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' reports minimumDurationReached="
                f"{minimum_duration_reached} while its samples measure "
                f"{measurement_duration_nanoseconds} ns against a required "
                f"{minimum_measurement_duration_nanoseconds} ns."
            )
        if not duration_satisfied and sample_count != maximum_sample_count:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' measured {measurement_duration_nanoseconds} ns, "
                f"expected at least {minimum_measurement_duration_nanoseconds} ns. "
                f"Only a run stopped at the {maximum_sample_count}-sample cap "
                "may fall short."
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
        measurement_quality_policy = required_string(
            profile_contract,
            "measurementQualityPolicy",
            f"profiles.{profile}",
        )

        # The termination reason, the sample population, and the two quality
        # targets describe one outcome. The comparisons against the error
        # ceiling are tolerant because the runner and this validator compute
        # the statistic independently; only a clear contradiction is corrupt.
        precision_reached = relative_standard_error <= maximum_relative_standard_error or close_enough(
            relative_standard_error,
            maximum_relative_standard_error,
        )

        if termination_reason == SAMPLE_CAP_REACHED:
            if sample_count != maximum_sample_count:
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' reports '{SAMPLE_CAP_REACHED}' "
                    f"with {sample_count} samples, but the contract cap is "
                    f"{maximum_sample_count}."
                )
            if minimum_duration_reached and precision_reached:
                raise PerformanceEvidenceError(
                    f"Workload '{workload_id}' reports '{SAMPLE_CAP_REACHED}' "
                    "although both the minimum duration and the precision "
                    "target were met. A run that satisfies both is precise."
                )
        elif not precision_reached:
            raise PerformanceEvidenceError(
                f"Workload '{workload_id}' reports termination reason "
                f"'{termination_reason}' with relative standard error "
                f"{relative_standard_error:.6f} above the contract maximum "
                f"{maximum_relative_standard_error:.6f}."
            )

        # A capped sample is a valid observation of an unsuitable contract, not
        # a corrupt one. The cap is the trigger, not the duration flag: a run
        # that met its duration and still ran out of budget chasing precision is
        # exactly as unusable, and it must not pass silently under observe.
        #
        # This is the only measurement-quality verdict for a workload. Every
        # other way to miss the precision target is a contradiction the
        # invariants above already rejected, so no second branch is reachable
        # here. The diagnostic carries all three bounds next to their achieved
        # values, so recalibration needs no rerun.
        if termination_reason == SAMPLE_CAP_REACHED:
            diagnostic = (
                f"Workload '{workload_id}' stopped at the sample cap. "
                f"Samples: {sample_count} of {maximum_sample_count} allowed "
                f"(measurementSamples={expected_sample_count} x "
                f"maximumMeasurementSampleMultiplier="
                f"{profile_contract['maximumMeasurementSampleMultiplier']}). "
                f"Duration: {measurement_duration_nanoseconds} ns measured "
                f"against {minimum_measurement_duration_nanoseconds} ns "
                f"required. Relative standard error: "
                f"{relative_standard_error:.6f} achieved against "
                f"{maximum_relative_standard_error:.6f} allowed. "
                "Recalibrate operationsPerSample, the minimum duration, or the "
                "cap for this workload."
            )
            if measurement_quality_policy == "enforce":
                raise MeasurementQualityError(diagnostic)
            print(f"Measurement quality observation: {diagnostic}")
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
            measurement_quality_policy == "enforce"
            and calibration_relative_standard_error
            > maximum_calibration_relative_standard_error
        ):
            raise MeasurementQualityError(
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
                "terminationReason": termination_reason,
                "minimumDurationReached": minimum_duration_reached,
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


def validate_absolute_budgets(
    workloads: Sequence[dict[str, Any]],
    contract: dict[str, Any],
    *,
    strict: bool = True,
) -> list[dict[str, Any]]:
    """Evaluate every workload against its family's absolute ceilings.

    The historical gate treats a breach as unusable evidence and stops. The
    paired comparison needs the same measurements as a verdict instead: a
    candidate over its ceiling is a regression, not a run that failed to
    produce a result. `strict=False` returns the complete set of checks and
    leaves that decision to the caller.
    """
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
            if not passed and strict:
                raise PerformanceEvidenceError(
                    f"Absolute budget failed for '{workload['id']}' {metric_name}: "
                    f"{actual} > {maximum}."
                )

    return checks
