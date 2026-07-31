#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
benchmark_project="${repo_root}/benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks"
benchmark_project="${benchmark_project}/Doka.EntityFrameworkCore.MySql.Benchmarks.csproj"
benchmark_assembly="${repo_root}/artifacts/bin/Doka.EntityFrameworkCore.MySql.Benchmarks/release"
benchmark_assembly="${benchmark_assembly}/Doka.EntityFrameworkCore.MySql.Benchmarks.dll"
performance_contract="${repo_root}/benchmarks/performance-contract.json"
baseline_manifest="${DOKA_BENCHMARK_BASELINE_PATH:-${repo_root}/benchmarks/baselines/doka-benchmark-baseline.json}"
evidence_tool="${repo_root}/eng/performance_evidence.py"
compose_file="${repo_root}/docker/compose.yml"
compose_command=(docker compose -f "${compose_file}")
benchmark_profile="${DOKA_BENCHMARK_PROFILE:-smoke}"
benchmark_target="${DOKA_BENCHMARK_TARGET:-mysql84}"
benchmark_run_id="${DOKA_BENCHMARK_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
baseline_mode="${DOKA_BENCHMARK_BASELINE_MODE:-compare}"
runner_class="${DOKA_BENCHMARK_RUNNER_CLASS:-local-$(uname -s | tr '[:upper:]' '[:lower:]')-$(uname -m)}"
benchmark_commit="${DOKA_BENCHMARK_COMMIT:-$(git -C "${repo_root}" rev-parse HEAD)}"
benchmark_source_hash="$(
    python3 "${evidence_tool}" source-hash --repo "${repo_root}"
)"
benchmark_source_hash="${DOKA_BENCHMARK_SOURCE_HASH:-${benchmark_source_hash}}"
benchmark_artifacts_dir="${repo_root}/artifacts/benchmarks/${benchmark_target}"
benchmark_report_dir="${benchmark_artifacts_dir}/reports/${benchmark_run_id}"
benchmark_evidence_dir="${benchmark_report_dir}/evidence"
bdn_evidence="${benchmark_evidence_dir}/benchmarkdotnet-evidence.json"
workload_evidence="${benchmark_evidence_dir}/workload-evidence.json"
soak_evidence="${benchmark_evidence_dir}/soak-evidence.json"
evaluation_evidence="${benchmark_evidence_dir}/performance-evaluation.json"
summary_file="${benchmark_evidence_dir}/performance-summary.md"
wait_timeout_seconds=60
wait_interval_seconds=2
mode="ensure-up"
should_stop_stack_on_exit=0
benchmark_target_display_name=""
benchmark_target_host=""
benchmark_target_port=""
benchmark_compose_service=""
verified_server_image=""

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/benchmark.sh
  ./eng/benchmark.sh --test-only
  ./eng/benchmark.sh --down
  ./eng/benchmark.sh --up-run-down

Modes:
  (no args)         Ensure the selected Compose target is up, then run the configured profile.
  --test-only       Run against an already reachable target.
  --down            Stop and remove the bundled Compose stack.
  --up-run-down     Start the selected target, run the configured profile, then stop the stack.

Environment:
  DOKA_BENCHMARK_TARGET=mysql84|mariadb118
  DOKA_BENCHMARK_PROFILE=smoke|scorecard|stress
  DOKA_BENCHMARK_BASELINE_MODE=compare|seed
  DOKA_BENCHMARK_RUNNER_CLASS=<stable comparable runner identity>
  DOKA_BENCHMARK_SOURCE_HASH=<optional exact 64-character source digest>
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

ensure_docker_available() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "docker is required for the bundled benchmark path." >&2
        exit 1
    fi
}

ensure_docker_compose_available() {
    ensure_docker_available

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
            benchmark_target_host="127.0.0.1"
            benchmark_target_port="33068"
            benchmark_compose_service="mysql84"
            ;;
        mariadb118)
            benchmark_target_display_name="MariaDB 11.8"
            benchmark_target_host="127.0.0.1"
            benchmark_target_port="33069"
            benchmark_compose_service="mariadb118"
            ;;
        *)
            echo "Unsupported benchmark target '${benchmark_target}'." >&2
            exit 1
            ;;
    esac
}

validate_configuration() {
    case "${benchmark_profile}" in
        smoke|scorecard|stress)
            ;;
        *)
            echo "Unsupported benchmark profile '${benchmark_profile}'." >&2
            exit 1
            ;;
    esac

    case "${baseline_mode}" in
        compare|seed)
            ;;
        *)
            echo "Unsupported baseline mode '${baseline_mode}'." >&2
            exit 1
            ;;
    esac

    if [[ ! "${benchmark_run_id}" =~ ^[0-9A-Za-z._-]+$ ]]; then
        echo "Benchmark run ID '${benchmark_run_id}' contains unsupported characters." >&2
        exit 1
    fi

    if [[ ! "${runner_class}" =~ ^[0-9A-Za-z._-]+$ ]]; then
        echo "Benchmark runner class '${runner_class}' contains unsupported characters." >&2
        exit 1
    fi

    if [[ ! "${benchmark_source_hash}" =~ ^[0-9a-f]{64}$ ]]; then
        echo "Benchmark source hash must be a lower-case SHA-256 digest." >&2
        exit 1
    fi

    if ! command -v jq >/dev/null 2>&1; then
        echo "jq is required to resolve the digest-pinned benchmark image." >&2
        exit 1
    fi

    verified_server_image="$(
        jq -er \
            --arg target "${benchmark_target}" \
            '.requiredTargets[$target].serverImage' \
            "${performance_contract}"
    )"

    if [[ ! "${verified_server_image}" =~ @sha256:[0-9a-f]{64}$ ]]; then
        echo "Benchmark target '${benchmark_target}' has no digest-pinned server image." >&2
        exit 1
    fi
}

wait_for_benchmark_target() {
    local start_time
    local current_time
    local elapsed_seconds
    local health_status
    local container_id

    echo "Waiting for ${benchmark_target_display_name} on ${benchmark_target_host}:${benchmark_target_port}..."
    start_time="$(date +%s)"

    while true; do
        container_id="$("${compose_command[@]}" ps -q "${benchmark_compose_service}" 2>/dev/null || true)"
        health_status=""
        if [[ -n "${container_id}" ]]; then
            health_status="$(
                docker inspect \
                    --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' \
                    "${container_id}" \
                    2>/dev/null \
                    || true
            )"
        fi

        if [[ "${health_status}" == "healthy" ]] \
            && can_connect "${benchmark_target_host}" "${benchmark_target_port}"; then
            echo "${benchmark_target_display_name} is reachable."
            return 0
        fi

        current_time="$(date +%s)"
        elapsed_seconds=$(( current_time - start_time ))

        if (( elapsed_seconds >= wait_timeout_seconds )); then
            echo "Timed out waiting for ${benchmark_target_display_name} after ${wait_timeout_seconds} seconds." >&2
            exit 1
        fi

        sleep "${wait_interval_seconds}"
    done
}

verify_benchmark_container_identity() {
    local container_id
    local actual_image
    local actual_image_id
    local expected_digest
    local repo_digests
    local port_matches
    local port_match_count

    port_matches="$("${compose_command[@]}" ps -q "${benchmark_compose_service}" 2>/dev/null || true)"

    if [[ -z "${port_matches}" ]]; then
        port_matches="$(
            docker ps \
                --filter "publish=${benchmark_target_port}" \
                --format '{{.ID}}'
        )"
    fi

    port_match_count="$(
        printf '%s\n' "${port_matches}" \
            | sed '/^$/d' \
            | wc -l \
            | tr -d ' '
    )"

    if [[ "${port_match_count}" -ne 1 ]]; then
        echo "Expected one benchmark container publishing port ${benchmark_target_port}." >&2
        exit 1
    fi

    container_id="${port_matches}"
    actual_image="$(docker inspect --format '{{.Config.Image}}' "${container_id}")"
    actual_image_id="$(docker inspect --format '{{.Image}}' "${container_id}")"
    expected_digest="${verified_server_image##*@}"

    if [[ "${actual_image}" != "${verified_server_image}" \
        && "${actual_image_id}" != "${expected_digest}" ]]; then
        repo_digests="$(
            docker image inspect \
                --format '{{range .RepoDigests}}{{println .}}{{end}}' \
                "${actual_image_id}"
        )"

        if ! grep -Fq "@${expected_digest}" <<< "${repo_digests}"; then
            echo "Benchmark container image '${actual_image}' does not match the contract." >&2
            echo "Expected '${verified_server_image}'." >&2
            exit 1
        fi
    fi
}

start_compose_stack() {
    echo "Starting bundled benchmark target ${benchmark_target_display_name}..."
    "${compose_command[@]}" up -d "${benchmark_compose_service}"
}

stop_compose_stack() {
    echo "Stopping bundled benchmark Compose stack..."
    "${compose_command[@]}" down
}

ensure_fresh_run_directory() {
    if [[ -d "${benchmark_report_dir}" ]] \
        && find "${benchmark_report_dir}" -mindepth 1 -print -quit | grep -q .; then
        echo "Current-run benchmark directory '${benchmark_report_dir}' is not empty." >&2
        echo "Use a new DOKA_BENCHMARK_RUN_ID; stale artifacts are never reused." >&2
        exit 1
    fi

    mkdir -p "${benchmark_evidence_dir}"
}

run_benchmarkdotnet() {
    dotnet "${benchmark_assembly}" \
        --filter '*' \
        --artifacts "${benchmark_report_dir}"

    python3 "${evidence_tool}" validate-bdn \
        --contract "${performance_contract}" \
        --reports "${benchmark_report_dir}" \
        --run-id "${benchmark_run_id}" \
        --target "${benchmark_target}" \
        --profile "${benchmark_profile}" \
        --output "${bdn_evidence}"
}

run_workload_matrix() {
    dotnet "${benchmark_assembly}" --workloads "${workload_evidence}"
}

run_soak_if_required() {
    if [[ "${benchmark_profile}" == "smoke" ]]; then
        return 0
    fi

    dotnet "${benchmark_assembly}" --soak "${soak_evidence}"
}

evaluate_current_run() {
    local command=(
        python3 "${evidence_tool}" evaluate
        --contract "${performance_contract}"
        --baseline "${baseline_manifest}"
        --workloads "${workload_evidence}"
        --bdn "${bdn_evidence}"
        --run-id "${benchmark_run_id}"
        --target "${benchmark_target}"
        --profile "${benchmark_profile}"
        --mode "${baseline_mode}"
        --output "${evaluation_evidence}"
    )

    if [[ -f "${soak_evidence}" ]]; then
        command+=(--soak "${soak_evidence}")
    fi

    "${command[@]}"
}

write_summary() {
    {
        echo "# Performance evidence summary"
        echo
        echo "- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
        echo "- runId: ${benchmark_run_id}"
        echo "- target: ${benchmark_target}"
        echo "- profile: ${benchmark_profile}"
        echo "- baselineMode: ${baseline_mode}"
        echo "- runnerClass: ${runner_class}"
        echo "- commit: ${benchmark_commit}"
        echo "- sourceHash: ${benchmark_source_hash}"
        echo "- reportsDirectory: ${benchmark_report_dir}"
        echo "- benchmarkDotNetEvidence: ${bdn_evidence}"
        echo "- workloadEvidence: ${workload_evidence}"
        echo "- soakEvidence: ${soak_evidence}"
        echo "- evaluationEvidence: ${evaluation_evidence}"
        echo
        echo "The evaluation succeeded only after current-run identity, matrix completeness,"
        echo "statistics, memory evidence, absolute budgets, applicable historical budgets,"
        echo "same-run controls, and applicable soak invariants passed."
    } > "${summary_file}"
}

run_benchmarks() {
    ensure_fresh_run_directory

    "${repo_root}/eng/verify-dotnet.sh"
    export DOKA_BENCHMARK_PROFILE="${benchmark_profile}"
    export DOKA_BENCHMARK_TARGET="${benchmark_target}"
    export DOKA_BENCHMARK_RUN_ID="${benchmark_run_id}"
    export DOKA_BENCHMARK_RUNNER_CLASS="${runner_class}"
    export DOKA_BENCHMARK_COMMIT="${benchmark_commit}"
    export DOKA_BENCHMARK_SOURCE_HASH="${benchmark_source_hash}"
    export DOKA_BENCHMARK_SERVER_IMAGE="${verified_server_image}"

    dotnet restore "${benchmark_project}" --tl:off
    dotnet build "${benchmark_project}" \
        --configuration Release \
        --no-restore \
        --tl:off \
        -m:1

    run_benchmarkdotnet
    run_workload_matrix
    run_soak_if_required
    evaluate_current_run
    write_summary
}

configure_benchmark_target
validate_configuration

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
        # Preserve the old smoke-specific spelling for existing automation.
        --up-run-down|--up-smoke-down)
            mode="up-run-down"
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
        ensure_docker_available
        ;;
    down)
        ensure_docker_compose_available
        stop_compose_stack
        exit 0
        ;;
    up-run-down)
        ensure_docker_compose_available
        start_compose_stack
        wait_for_benchmark_target
        ;;
esac

verify_benchmark_container_identity
run_benchmarks
