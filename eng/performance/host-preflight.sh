#!/usr/bin/env bash

# Rejects a contended benchmark host before BenchmarkDotNet starts. GitHub owns
# retries at the job level; this function has no workflow or attempt state.

measure_linux_cpu() {
    local interval_seconds="$1"
    local first
    local second
    first="$(awk '/^cpu / { idle=$5+$6; total=0; for (i=2; i<=NF; i++) total+=$i; print idle, total; exit }' /proc/stat)"
    sleep "${interval_seconds}"
    second="$(awk '/^cpu / { idle=$5+$6; total=0; for (i=2; i<=NF; i++) total+=$i; print idle, total; exit }' /proc/stat)"
    awk -v first="${first}" -v second="${second}" 'BEGIN {
        split(first, a, " "); split(second, b, " ");
        total=b[2]-a[2]; idle=b[1]-a[1];
        if (total <= 0) exit 1;
        printf "%.6f\n", (total-idle)/total;
    }'
}

measure_portable_cpu() {
    local interval_seconds="$1"
    local processors
    processors="$(getconf _NPROCESSORS_ONLN 2>/dev/null || sysctl -n hw.logicalcpu)"
    sleep "${interval_seconds}"
    ps -A -o %cpu= | awk -v processors="${processors}" '
        { total += $1 }
        END {
            utilization = total / (processors * 100);
            if (utilization > 1) utilization = 1;
            printf "%.6f\n", utilization;
        }'
}

require_benchmark_host_headroom() {
    local contract="$1"
    local maximum
    local interval_milliseconds
    local interval_seconds
    local required
    local attempts
    local consecutive=0
    local attempt
    local utilization

    maximum="$(jq -er '.hostPreconditions.maximumCpuUtilization' "${contract}")"
    interval_milliseconds="$(jq -er '.hostPreconditions.sampleIntervalMilliseconds' "${contract}")"
    required="$(jq -er '.hostPreconditions.requiredConsecutivePassingSamples' "${contract}")"
    attempts="$(jq -er '.hostPreconditions.maximumSampleAttempts' "${contract}")"
    interval_seconds="$(awk -v milliseconds="${interval_milliseconds}" 'BEGIN { printf "%.3f", milliseconds / 1000 }')"

    for (( attempt = 1; attempt <= attempts; attempt++ )); do
        if [[ -r /proc/stat ]]; then
            utilization="$(measure_linux_cpu "${interval_seconds}")"
        else
            utilization="$(measure_portable_cpu "${interval_seconds}")"
        fi

        echo "Host CPU sample ${attempt}/${attempts}: ${utilization} (maximum ${maximum})."
        if awk -v actual="${utilization}" -v limit="${maximum}" 'BEGIN { exit !(actual <= limit) }'; then
            consecutive=$(( consecutive + 1 ))
            if (( consecutive >= required )); then
                echo "Benchmark host admission passed."
                return 0
            fi
        else
            consecutive=0
        fi
    done

    echo "Benchmark host admission failed after ${attempts} samples." >&2
    return 1
}
