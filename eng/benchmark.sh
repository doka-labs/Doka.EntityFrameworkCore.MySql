#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
benchmark_project="${repo_root}/benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks.csproj"
benchmark_assembly="${repo_root}/artifacts/bin/Doka.EntityFrameworkCore.MySql.Benchmarks/release/Doka.EntityFrameworkCore.MySql.Benchmarks.dll"
compose_file="${repo_root}/docker/compose.yml"
compose_command=(docker compose -f "${compose_file}")
benchmark_profile="${DOKA_BENCHMARK_PROFILE:-smoke}"
benchmark_target="${DOKA_BENCHMARK_TARGET:-mysql84}"
benchmark_run_id="${DOKA_BENCHMARK_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
benchmark_artifacts_dir="${repo_root}/artifacts/benchmarks/${benchmark_target}"
benchmark_report_dir="${benchmark_artifacts_dir}/reports/${benchmark_run_id}"
baseline_manifest="${repo_root}/benchmarks/baselines/doka-benchmark-baseline.json"
wait_timeout_seconds=60
wait_interval_seconds=2
mode="ensure-up"
should_stop_stack_on_exit=0
benchmark_target_display_name=""
benchmark_target_engine_family=""
benchmark_target_server_version=""
benchmark_target_host=""
benchmark_target_port=""
benchmark_compose_service=""

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/benchmark.sh
  ./eng/benchmark.sh --test-only
  ./eng/benchmark.sh --down
  ./eng/benchmark.sh --up-smoke-down

Modes:
  (no args)         Ensure the bundled selected benchmark-target Compose service is up, then run the benchmark smoke suite.
  --test-only       Run the benchmark smoke suite without starting the bundled Compose service.
  --down            Stop and remove the bundled Compose stack without running benchmarks.
  --up-smoke-down   Start the bundled selected benchmark-target Compose service, run the benchmark smoke suite, then stop the stack again.

Environment:
  DOKA_BENCHMARK_TARGET=mysql84|mariadb118
EOF
}

cleanup() {
    local exit_code="$1"

    if [[ "${should_stop_stack_on_exit}" -eq 1 ]]; then
        set +e
        echo "Stopping bundled benchmark Compose stack..."
        "${compose_command[@]}" down
        local down_exit_code=$?
        set -e

        if [[ "${exit_code}" -eq 0 && "${down_exit_code}" -ne 0 ]]; then
            exit_code="${down_exit_code}"
        fi
    fi

    exit "${exit_code}"
}

trap 'cleanup "$?"' EXIT

ensure_docker_compose_available() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "docker is required for the bundled benchmark path." >&2
        exit 1
    fi

    if ! docker compose version >/dev/null 2>&1; then
        echo "docker compose is required for the bundled benchmark path." >&2
        exit 1
    fi
}

can_connect() {
    local host="$1"
    local port="$2"

    if command -v nc >/dev/null 2>&1; then
        nc -z "${host}" "${port}" >/dev/null 2>&1
        return $?
    fi

    (echo >"/dev/tcp/${host}/${port}") >/dev/null 2>&1
}

configure_benchmark_target() {
    case "${benchmark_target}" in
        mysql84)
            benchmark_target_display_name="MySQL 8.4"
            benchmark_target_engine_family="MySQL"
            benchmark_target_server_version="8.4.0"
            benchmark_target_host="127.0.0.1"
            benchmark_target_port="33068"
            benchmark_compose_service="mysql84"
            ;;
        mariadb118)
            benchmark_target_display_name="MariaDB 11.8"
            benchmark_target_engine_family="MariaDB"
            benchmark_target_server_version="11.8.0"
            benchmark_target_host="127.0.0.1"
            benchmark_target_port="33069"
            benchmark_compose_service="mariadb118"
            ;;
        *)
            echo "Unsupported benchmark target '${benchmark_target}'. Set DOKA_BENCHMARK_TARGET to 'mysql84' or 'mariadb118'." >&2
            exit 1
            ;;
    esac
}

wait_for_benchmark_target() {
    local start_time
    local current_time
    local elapsed_seconds
    local health_status
    local container_id

    echo "Waiting for ${benchmark_target_display_name} benchmark target on ${benchmark_target_host}:${benchmark_target_port}..."
    start_time="$(date +%s)"

    while true; do
        # Compose service identity is stable across checkout-directory and
        # project-name changes; generated container names are not.
        container_id="$("${compose_command[@]}" ps -q "${benchmark_compose_service}" 2>/dev/null || true)"
        health_status=""
        if [[ -n "${container_id}" ]]; then
            health_status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "${container_id}" 2>/dev/null || true)"
        fi

        if [[ "${health_status}" == "healthy" ]] && can_connect "${benchmark_target_host}" "${benchmark_target_port}"; then
            echo "${benchmark_target_display_name} benchmark target is reachable."
            return 0
        fi

        current_time="$(date +%s)"
        elapsed_seconds=$(( current_time - start_time ))

        if (( elapsed_seconds >= wait_timeout_seconds )); then
            echo "Timed out waiting for ${benchmark_target_display_name} on ${benchmark_target_host}:${benchmark_target_port} after ${wait_timeout_seconds} seconds." >&2
            exit 1
        fi

        sleep "${wait_interval_seconds}"
    done
}

start_compose_stack() {
    echo "Starting bundled benchmark ${benchmark_target_display_name} Compose service..."
    "${compose_command[@]}" up -d "${benchmark_compose_service}"
}

stop_compose_stack() {
    echo "Stopping bundled benchmark Compose stack..."
    "${compose_command[@]}" down
}

baseline_state() {
    if [[ ! -f "${baseline_manifest}" ]]; then
        echo "missing"
        return 0
    fi

    grep -o '"baselineState": "[^"]*"' "${baseline_manifest}" | head -n 1 | cut -d '"' -f 4
}

write_summary() {
    local state
    local evaluation_mode
    local summary_file="${benchmark_artifacts_dir}/benchmark-summary.md"

    state="$(baseline_state)"
    evaluation_mode="compare"

    if [[ "${state}" == "pending-seed" ]]; then
        evaluation_mode="seed-candidate"
    fi

    mkdir -p "${benchmark_artifacts_dir}"

    {
        echo "# Benchmark ${benchmark_profile} summary"
        echo
        echo "- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
        echo "- benchmarkTarget: ${benchmark_target}"
        echo "- engineFamily: ${benchmark_target_engine_family}"
        echo "- serverVersion: ${benchmark_target_server_version}"
        echo "- displayName: ${benchmark_target_display_name}"
        echo "- baselineManifest: ${baseline_manifest}"
        echo "- baselineState: ${state}"
        echo "- benchmarkProfile: ${benchmark_profile}"
        echo "- evaluationMode: ${evaluation_mode}"
        echo "- reportsDirectory: ${benchmark_report_dir}"
        echo
        if [[ "${evaluation_mode}" == "seed-candidate" ]]; then
            echo "The internal Doka baseline is still pending its first accepted seed run. This report set is retained as a reviewable seed candidate."
        else
            echo "The internal Doka baseline exists and this report set is retained for comparison against the current baseline contract."
        fi
    } > "${summary_file}"
}

write_evidence() {
    local state
    local evaluation_mode
    local report_file_count
    local evidence_file="${benchmark_artifacts_dir}/benchmark-evidence.json"

    state="$(baseline_state)"
    evaluation_mode="compare"

    if [[ "${state}" == "pending-seed" ]]; then
        evaluation_mode="seed-candidate"
    fi

    report_file_count="$(find "${benchmark_report_dir}" -type f | wc -l | tr -d ' ')"

    cat > "${evidence_file}" <<EOF
{
  "generatedUtc": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "benchmarkTarget": "${benchmark_target}",
  "engineFamily": "${benchmark_target_engine_family}",
  "serverVersion": "${benchmark_target_server_version}",
  "displayName": "${benchmark_target_display_name}",
  "baselineManifest": "${baseline_manifest}",
  "baselineState": "${state}",
  "benchmarkProfile": "${benchmark_profile}",
  "evaluationMode": "${evaluation_mode}",
  "reportsDirectory": "${benchmark_report_dir}",
  "reportFileCount": ${report_file_count}
}
EOF
}

structured_report_count() {
    find "${benchmark_report_dir}" -type f \( -name '*.json' -o -name '*.md' -o -name '*.csv' -o -name '*.html' \) | wc -l | tr -d ' '
}

run_benchmarks() {
    mkdir -p "${benchmark_report_dir}"

    "${repo_root}/eng/verify-dotnet.sh"
    export DOKA_BENCHMARK_PROFILE="${benchmark_profile}"
    export DOKA_BENCHMARK_TARGET="${benchmark_target}"
    dotnet restore "${benchmark_project}" --tl:off
    dotnet build "${benchmark_project}" \
        --configuration Release \
        --no-restore \
        --tl:off \
        -m:1
    dotnet "${benchmark_assembly}" \
        --filter '*' \
        --artifacts "${benchmark_report_dir}"

    if [[ "$(structured_report_count)" -eq 0 ]]; then
        echo "No structured benchmark reports were generated under ${benchmark_report_dir}." >&2
        exit 1
    fi

    write_summary
    write_evidence
}

configure_benchmark_target

if (( $# > 1 )); then
    print_usage >&2
    exit 1
fi

if (( $# == 1 )); then
    case "$1" in
        --test-only)
            mode="test-only"
            ;;
        --down)
            mode="down"
            ;;
        --up-smoke-down)
            mode="up-smoke-down"
            should_stop_stack_on_exit=1
            ;;
        --help|-h)
            print_usage
            exit 0
            ;;
        *)
            print_usage >&2
            exit 1
            ;;
    esac
fi

case "${mode}" in
    ensure-up)
        ensure_docker_compose_available
        start_compose_stack
        wait_for_benchmark_target
        ;;
    test-only)
        ;;
    down)
        ensure_docker_compose_available
        stop_compose_stack
        exit 0
        ;;
    up-smoke-down)
        ensure_docker_compose_available
        start_compose_stack
        wait_for_benchmark_target
        ;;
esac

run_benchmarks
