#!/usr/bin/env bash

# Capture host admission evidence and export the identity every measurement
# report records.
#
# This is sourced rather than executed: the exports have to reach the shell that
# launches the driver. It exists as its own file because two orchestrations need
# it -- the historical run and the paired comparison -- and a second copy would
# be free to export a different set than the report requires, which is exactly
# how the paired path came to launch a driver that could not build its report.

capture_host_preflight() {
    # Require current CPU headroom across a bounded admission window. This
    # rejects sustained contention without mistaking the preceding build's
    # lifetime process averages for live host saturation.
    local output_path="$1"
    local contract="$2"
    local module="${3:-eng.performance.cli}"

    mkdir -p "$(dirname "${output_path}")"

    python3 -m "${module}" host-preflight \
        --contract "${contract}" \
        --output "${output_path}"

    export DOKA_BENCHMARK_PROCESSOR
    DOKA_BENCHMARK_PROCESSOR="$(jq -er '.processor' "${output_path}")"
    export DOKA_BENCHMARK_HOST_LOAD_AVERAGE_1M
    DOKA_BENCHMARK_HOST_LOAD_AVERAGE_1M="$(jq -er '.loadAverage1Minute' "${output_path}")"
    export DOKA_BENCHMARK_HOST_LOAD_AVERAGE_5M
    DOKA_BENCHMARK_HOST_LOAD_AVERAGE_5M="$(jq -er '.loadAverage5Minutes' "${output_path}")"
    export DOKA_BENCHMARK_HOST_LOAD_AVERAGE_15M
    DOKA_BENCHMARK_HOST_LOAD_AVERAGE_15M="$(jq -er '.loadAverage15Minutes' "${output_path}")"
    export DOKA_BENCHMARK_HOST_LOAD_RATIO_1M
    DOKA_BENCHMARK_HOST_LOAD_RATIO_1M="$(jq -er '.loadAverage1MinutePerProcessor' "${output_path}")"
    export DOKA_BENCHMARK_HOST_ADMISSION_METRIC
    DOKA_BENCHMARK_HOST_ADMISSION_METRIC="$(jq -er '.admissionMetric' "${output_path}")"
    export DOKA_BENCHMARK_HOST_CPU_UTILIZATION
    DOKA_BENCHMARK_HOST_CPU_UTILIZATION="$(jq -er '.admittedCpuUtilization' "${output_path}")"
    export DOKA_BENCHMARK_HOST_MAXIMUM_CPU_UTILIZATION
    DOKA_BENCHMARK_HOST_MAXIMUM_CPU_UTILIZATION="$(
        jq -er '.maximumCpuUtilization' "${output_path}"
    )"
}
