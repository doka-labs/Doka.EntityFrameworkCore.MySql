#!/usr/bin/env bash

# Runs one target directly through BenchmarkDotNet and evaluates its raw JSON
# once. Shell owns only process, container, and environment orchestration.

set -Eeuo pipefail
export DOTNET_CLI_USE_MSBUILD_SERVER=0

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
contract="${DOKA_BENCHMARK_CONTRACT_PATH:-${repo_root}/benchmarks/performance-contract.json}"
benchmark_project="${repo_root}/benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks.csproj"
benchmark_assembly="${repo_root}/artifacts/bin/Doka.EntityFrameworkCore.MySql.Benchmarks/release/Doka.EntityFrameworkCore.MySql.Benchmarks.dll"
compose_file="${repo_root}/docker/compose.yml"
target="${DOKA_BENCHMARK_TARGET:-mysql84}"
profile="${DOKA_BENCHMARK_PROFILE:-smoke}"
run_id="${DOKA_BENCHMARK_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
runner_class="${DOKA_BENCHMARK_RUNNER_CLASS:-local-$(uname -s | tr '[:upper:]' '[:lower:]')-$(uname -m)}"
commit="${DOKA_BENCHMARK_COMMIT:-$(git -C "${repo_root}" rev-parse HEAD)}"
source_hash="${DOKA_BENCHMARK_SOURCE_HASH:-$({ git -C "${repo_root}" rev-parse HEAD; git -C "${repo_root}" diff HEAD --binary --no-ext-diff; } | shasum -a 256 | awk '{print $1}')}"
fingerprint="$(printf '%s' "${repo_root}" | cksum | awk '{print $1}')"
compose_project="${DOKA_BENCHMARK_COMPOSE_PROJECT_NAME:-doka-benchmark-${fingerprint}-${target}}"
compose=(docker compose -p "${compose_project}" -f "${compose_file}")
report_directory="${repo_root}/artifacts/benchmarks/${target}/reports/${run_id}"
soak_report="${report_directory}/soak.json"
mode="ensure-up"
stop_on_exit=0

usage() {
    cat <<'EOF'
Usage:
  ./eng/benchmark.sh
  ./eng/benchmark.sh --test-only
  ./eng/benchmark.sh --down
  ./eng/benchmark.sh --up-run-down
  ./eng/benchmark.sh --up-smoke-down

Environment:
  DOKA_BENCHMARK_TARGET=<requiredTargets key; local default mysql84>
  DOKA_BENCHMARK_PROFILE=smoke|scorecard|stress
  DOKA_BENCHMARK_RUN_ID=<unique artifact identity>
  DOKA_BENCHMARK_PORT=<published target port; use 0 for a dynamic port>
EOF
}

if (( $# > 1 )); then
    usage >&2
    exit 78
fi

if (( $# == 1 )); then
    case "$1" in
        --test-only)
            mode="test-only"
            ;;
        --down)
            mode="down"
            ;;
        --up-run-down|--up-smoke-down)
            mode="up-run-down"
            stop_on_exit=1
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            usage >&2
            exit 78
            ;;
    esac
fi

if ! command -v jq >/dev/null 2>&1; then
    echo "jq is required to resolve the performance contract." >&2
    exit 78
fi

if ! jq -e --arg target "${target}" '.requiredTargets[$target] != null' "${contract}" >/dev/null; then
    echo "Unsupported benchmark target '${target}'." >&2
    exit 78
fi

if ! jq -e --arg profile "${profile}" '.profiles[$profile] != null' "${contract}" >/dev/null; then
    echo "Unsupported benchmark profile '${profile}'." >&2
    exit 78
fi

if [[ ! "${run_id}" =~ ^[0-9A-Za-z._-]+$ ]]; then
    echo "Benchmark run ID '${run_id}' contains unsupported characters." >&2
    exit 78
fi

default_port="$(jq -er --arg target "${target}" '.requiredTargets[$target].hostPort' "${contract}")"
port="${DOKA_BENCHMARK_PORT:-${default_port}}"
expected_image="$(jq -er --arg target "${target}" '.requiredTargets[$target].serverImage' "${contract}")"
port_variable="DOKA_$(printf '%s' "${target}" | tr '[:lower:]' '[:upper:]')_PORT"
export "${port_variable}=${port}"

cleanup() {
    local status=$?
    if (( stop_on_exit == 1 )); then
        set +e
        "${compose[@]}" down --volumes --remove-orphans
        local cleanup_status=$?
        set -e
        if (( status == 0 && cleanup_status != 0 )); then
            echo "Benchmark cleanup failed with exit code ${cleanup_status}." >&2
            status=78
        fi
    fi
    exit "${status}"
}
trap cleanup EXIT

start_target() {
    "${compose[@]}" up -d "${target}"
    if [[ "${port}" == "0" ]]; then
        local published
        published="$("${compose[@]}" port "${target}" 3306)"
        port="${published##*:}"
    fi

    local started
    started="$(date +%s)"
    while true; do
        local container
        local health
        container="$("${compose[@]}" ps -q "${target}")"
        health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "${container}")"
        if [[ "${health}" == "healthy" ]] && (echo > "/dev/tcp/127.0.0.1/${port}") 2>/dev/null; then
            break
        fi
        if (( $(date +%s) - started >= 60 )); then
            echo "Timed out waiting for benchmark target '${target}'." >&2
            exit 78
        fi
        sleep 2
    done
}

verify_target_identity() {
    local container
    local actual_image
    container="$("${compose[@]}" ps -q "${target}" 2>/dev/null || true)"
    if [[ -z "${container}" && "${mode}" == "test-only" ]]; then
        container="$(docker ps --filter "publish=${port}" --format '{{.ID}}')"
    fi
    if [[ "$(printf '%s\n' "${container}" | awk 'NF { count++ } END { print count + 0 }')" -ne 1 ]]; then
        echo "Expected exactly one benchmark container for '${target}'." >&2
        exit 78
    fi

    actual_image="$(docker inspect --format '{{.Config.Image}}' "${container}")"
    if [[ "${actual_image}" != "${expected_image}" ]]; then
        echo "Benchmark container '${actual_image}' does not match '${expected_image}'." >&2
        exit 78
    fi
}

case "${mode}" in
    down)
        "${compose[@]}" down --volumes --remove-orphans
        exit 0
        ;;
    ensure-up|up-run-down)
        start_target
        ;;
    test-only)
        ;;
esac

verify_target_identity
if [[ -d "${report_directory}" ]] && find "${report_directory}" -mindepth 1 -print -quit | grep -q .; then
    echo "Benchmark report directory '${report_directory}' is not empty." >&2
    exit 78
fi
mkdir -p "${report_directory}"

"${repo_root}/eng/common/verify-dotnet.sh"
dotnet restore "${benchmark_project}" --tl:off
dotnet build "${benchmark_project}" --configuration Release --no-restore --tl:off -m:1

# shellcheck source=eng/performance/host-preflight.sh
source "${repo_root}/eng/performance/host-preflight.sh"
if ! require_benchmark_host_headroom "${contract}"; then
    exit 78
fi

export DOKA_BENCHMARK_TARGET="${target}"
export DOKA_BENCHMARK_DATABASE_PORT="${port}"
export DOKA_BENCHMARK_PROFILE="${profile}"
export DOKA_BENCHMARK_RUN_ID="${run_id}"
export DOKA_BENCHMARK_RUNNER_CLASS="${runner_class}"
export DOKA_BENCHMARK_COMMIT="${commit}"
export DOKA_BENCHMARK_SOURCE_HASH="${source_hash}"

filters=(--filter '*ProviderWorkloadBenchmarks*')
while IFS= read -r benchmark_name; do
    filters+=("*${benchmark_name}*")
done < <(
    jq -r '.benchmarkDotNetControls[]
        | [.type + "." + .method,
           (if has("baselineMethod") then .type + "." + .baselineMethod else empty end)]
        | .[]' "${contract}" | sort -u
)

if ! dotnet "${benchmark_assembly}" "${filters[@]}" --artifacts "${report_directory}"; then
    exit 78
fi

soak_required="$(jq -er --arg profile "${profile}" '.profiles[$profile].soakRequired | tostring' "${contract}")"
gate_arguments=(
    --evaluate
    "${contract}"
    "${report_directory}"
    "${target}"
    "${profile}"
)
if [[ "${soak_required}" == "true" ]]; then
    if ! dotnet "${benchmark_assembly}" --soak "${soak_report}"; then
        exit 1
    fi
    gate_arguments+=("${soak_report}")
fi

set +e
dotnet "${benchmark_assembly}" "${gate_arguments[@]}"
gate_status=$?
set -e
echo "Performance gate concluded with exit code ${gate_status}."
case "${gate_status}" in
    0|1|78)
        exit "${gate_status}"
        ;;
    *)
        exit 78
        ;;
esac
