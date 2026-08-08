#!/usr/bin/env python3
"""Environment compatibility and workload-binding validation."""

import re
from typing import Any

if __package__:
    from .contract import (
        COMPARABLE_ENVIRONMENT_FIELDS,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
    )
else:
    from contract import (
        COMPARABLE_ENVIRONMENT_FIELDS,
        PerformanceEvidenceError,
        close_enough,
        finite_number,
    )

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


def canonical_processor_identity(value: Any, name: str) -> tuple[str, ...]:
    """Extract model tokens shared by OS and CPUID processor descriptions.

    Linux exposes a processor model through ``/proc/cpuinfo``, while BenchmarkDotNet
    reads the CPUID brand string. Hosted runners can therefore describe one processor
    as either ``96-Core Processor`` or its current clock frequency. Those descriptors
    are not model identity and must not invalidate otherwise bound same-run evidence.
    """
    if not isinstance(value, str) or not value.strip():
        raise PerformanceEvidenceError(f"{name} must be a non-empty string.")

    normalized = value.casefold()
    normalized = re.sub(r"\((?:c|r|tm)\)", " ", normalized)
    normalized = re.sub(r"\b\d+\s*-\s*core\b", " ", normalized)
    normalized = re.sub(r"\b\d+(?:\.\d+)?\s*(?:ghz|mhz)\b", " ", normalized)
    tokens = tuple(
        token
        for token in re.findall(r"[a-z0-9]+", normalized)
        if token not in {"cpu", "processor"}
    )
    if not tokens:
        raise PerformanceEvidenceError(f"{name} contains no model identity.")

    return tokens


def validate_bdn_workload_environment(
    bdn_host: dict[str, Any],
    workload_environment: dict[str, Any],
) -> None:
    """Reject same-run controls produced on a different processor or architecture."""
    benchmark_processor = canonical_processor_identity(
        bdn_host.get("ProcessorName"),
        "BenchmarkDotNet processor",
    )
    workload_processor = canonical_processor_identity(
        workload_environment.get("processor"),
        "workload processor",
    )
    if benchmark_processor != workload_processor:
        raise PerformanceEvidenceError(
            "BenchmarkDotNet and workload evidence report different processors."
        )
    if bdn_host.get("LogicalCoreCount") != workload_environment.get("processorCount"):
        raise PerformanceEvidenceError(
            "BenchmarkDotNet and workload evidence report different logical processor counts."
        )
    if bdn_host.get("Architecture") != workload_environment.get("processArchitecture"):
        raise PerformanceEvidenceError(
            "BenchmarkDotNet and workload evidence report different process architectures."
        )
    if str(bdn_host.get("Configuration", "")).upper() != "RELEASE":
        raise PerformanceEvidenceError("BenchmarkDotNet evidence was not built in Release mode.")
