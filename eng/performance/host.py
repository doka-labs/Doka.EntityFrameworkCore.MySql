#!/usr/bin/env python3
"""Capture and validate host-admission evidence for performance runs.

The admission metric is sampled from platform-native cumulative CPU counters.
That interval measurement deliberately differs from Unix load averages: load
averages describe runnable work, while the gate needs current consumed CPU
capacity before accepting a machine for latency evidence.
"""

import ctypes
import math
import os
import platform
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, NamedTuple

if __package__:
    from .contract import (
        HOST_ADMISSION_METRIC,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
        required_current_timestamp,
        required_positive_integer,
        required_string,
        validate_contract,
    )
else:
    from contract import (
        HOST_ADMISSION_METRIC,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
        required_current_timestamp,
        required_positive_integer,
        required_string,
        validate_contract,
    )


class HostCpuCounterSnapshot(NamedTuple):
    """Represent cumulative host CPU counters used by one interval sample."""

    source: str
    counters: tuple[int, ...]
    busy_indices: tuple[int, ...]
    counter_modulus: int | None


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
