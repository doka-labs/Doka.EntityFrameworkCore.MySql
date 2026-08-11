#!/usr/bin/env bash

# Produces source-bound performance evidence for one engine and profile. A run
# is accepted only after container identity, workload completeness, statistical
# budgets, allocation budgets, and applicable soak invariants agree.
# Lifecycle state, cleanup traps, and resume checkpoints intentionally share
# this process. Statistical policy and evidence transformation remain in the
# focused Python modules under eng.performance.

set -euo pipefail

# The CLI otherwise attempts to contact a long-lived MSBuild server before it
# emits build output. Disabling that server keeps local and CI qualification
# isolated and lets the outer deadline own the complete process tree.
export DOTNET_CLI_USE_MSBUILD_SERVER=0

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
benchmark_project="${repo_root}/benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks"
benchmark_project="${benchmark_project}/Doka.EntityFrameworkCore.MySql.Benchmarks.csproj"
benchmark_assembly="${repo_root}/artifacts/bin/Doka.EntityFrameworkCore.MySql.Benchmarks/release"
benchmark_assembly="${benchmark_assembly}/Doka.EntityFrameworkCore.MySql.Benchmarks.dll"
# The path is overridable so a test can exercise this entry point against a
# modified policy without editing the repository it runs in. The digest the
# evidence records is taken from whatever file was loaded, so a run under an
# override cannot present itself as a run under the shipped contract: release
# qualification loads the shipped path and rejects the mismatch.
performance_contract="${DOKA_BENCHMARK_CONTRACT_PATH:-${repo_root}/benchmarks/performance-contract.json}"
baseline_manifest="${DOKA_BENCHMARK_BASELINE_PATH:-${repo_root}/benchmarks/baselines/doka-benchmark-baseline.json}"
evidence_module="eng.performance.cli"
deadline_module="eng.common.deadline"
compose_file="${repo_root}/docker/compose.yml"
benchmark_profile="${DOKA_BENCHMARK_PROFILE:-smoke}"
benchmark_target="${DOKA_BENCHMARK_TARGET:-mysql84}"
benchmark_run_id="${DOKA_BENCHMARK_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
repo_fingerprint="$(printf '%s' "${repo_root}" | cksum | awk '{print $1}')"
compose_project_name="${DOKA_BENCHMARK_COMPOSE_PROJECT_NAME:-doka-benchmark-${repo_fingerprint}-${benchmark_target}}"
compose_command=(docker compose -p "${compose_project_name}" -f "${compose_file}")
baseline_mode="${DOKA_BENCHMARK_BASELINE_MODE:-compare}"
# `historical` compares one measurement against an accepted baseline recorded
# on another host. `paired` measures a reference and a candidate provider
# alternately on this machine, which is a different orchestration entirely and
# is delegated below rather than folded into this one.
comparison_mode="${DOKA_BENCHMARK_COMPARISON_MODE:-historical}"
resume_mode="${DOKA_BENCHMARK_RESUME:-0}"
runner_class="${DOKA_BENCHMARK_RUNNER_CLASS:-local-$(uname -s | tr '[:upper:]' '[:lower:]')-$(uname -m)}"
benchmark_commit="${DOKA_BENCHMARK_COMMIT:-$(git -C "${repo_root}" rev-parse HEAD)}"
benchmark_source_hash="$(
    python3 -m "${evidence_module}" source-hash --repo "${repo_root}"
)"
benchmark_source_hash="${DOKA_BENCHMARK_SOURCE_HASH:-${benchmark_source_hash}}"
benchmark_artifacts_dir="${repo_root}/artifacts/benchmarks/${benchmark_target}"
benchmark_report_dir="${benchmark_artifacts_dir}/reports/${benchmark_run_id}"
benchmark_evidence_dir="${benchmark_report_dir}/evidence"
host_evidence="${benchmark_evidence_dir}/host-preflight.json"
bdn_evidence="${benchmark_evidence_dir}/benchmarkdotnet-evidence.json"
workload_evidence="${benchmark_evidence_dir}/workload-evidence.json"
workload_checkpoint_dir="${benchmark_artifacts_dir}/checkpoints/${benchmark_run_id}"
tail_confirmation_plan="${benchmark_evidence_dir}/tail-confirmation-plan.json"
tail_confirmation_dir="${benchmark_evidence_dir}/tail-confirmations"
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

# Re-execute the complete benchmark driver below a profile-owned wall-clock
# deadline. The helper owns a new process group, so timeout cleanup includes
# BenchmarkDotNet and any database client descendants rather than only Bash.
if [[ "${DOKA_BENCHMARK_DEADLINE_ACTIVE:-0}" != "1" ]]; then
    # A paired run measures two providers across many blocks, so the profile
    # deadline of a single scorecard would end it well inside its own budget.
    # The comparison that owns the run owns its wall clock.
    if [[ "${DOKA_BENCHMARK_COMPARISON_MODE:-historical}" == "paired" ]]; then
        maximum_total_duration_seconds="$(
            jq -er '.pairedPolicy.durations.maximumPairedRunSeconds' \
                "${performance_contract}"
        )"
    else
        maximum_total_duration_seconds="$(
            jq -er \
                --arg profile "${benchmark_profile}" \
                '.profiles[$profile].maximumTotalDurationSeconds' \
                "${performance_contract}"
        )"
    fi

    export DOKA_BENCHMARK_DEADLINE_ACTIVE=1
    export DOKA_BENCHMARK_RUN_ID="${benchmark_run_id}"

    # Not exec: the deadline helper reports its own timeout as 124, which the
    # attempt recorder does not know and therefore files as invalid evidence --
    # a state no retry can clear. A run the clock cut short produced no verdict
    # about the provider, so it leaves as the registered measurement-quality
    # code and keeps its bounded retry.
    deadline_status=0
    python3 -m "${deadline_module}" \
        --seconds "${maximum_total_duration_seconds}" \
        --label "${benchmark_profile} performance qualification" \
        -- bash "$0" "$@" || deadline_status=$?

    if (( deadline_status == 124 )); then
        echo "The ${benchmark_profile} run exceeded" \
            "${maximum_total_duration_seconds}s." >&2
        exit 75
    fi

    exit "${deadline_status}"
fi

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
  DOKA_BENCHMARK_COMPARISON_MODE=historical|paired
  DOKA_BENCHMARK_RESUME=0|1
  DOKA_BENCHMARK_RUNNER_CLASS=<stable comparable runner identity>
  DOKA_BENCHMARK_SOURCE_HASH=<optional exact 64-character source digest>
  DOKA_BENCHMARK_COMPOSE_PROJECT_NAME=<owned Compose project>
  DOKA_BENCHMARK_PORT=<published target port; use 0 for a dynamic port>
EOF
}

cleanup() {
    local exit_code="$1"

    if [[ "${should_stop_stack_on_exit}" -eq 1 ]]; then
        set +e
        echo "Stopping bundled benchmark Compose stack..."
        "${compose_command[@]}" down --volumes --remove-orphans
        local down_exit_code=$?
        set -e

        # Cleanup is part of a successful owned-stack run. Preserve an earlier
        # benchmark failure, but surface teardown failure after a green run.
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

    # Minimal CI images may omit netcat; Bash TCP sockets keep the readiness
    # probe dependency-free on supported hosts.
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
            benchmark_target_port="${DOKA_BENCHMARK_PORT:-33068}"
            benchmark_compose_service="mysql84"
            export DOKA_MYSQL84_PORT="${benchmark_target_port}"
            ;;
        mariadb118)
            benchmark_target_display_name="MariaDB 11.8"
            benchmark_target_host="127.0.0.1"
            benchmark_target_port="${DOKA_BENCHMARK_PORT:-33069}"
            benchmark_compose_service="mariadb118"
            export DOKA_MARIADB118_PORT="${benchmark_target_port}"
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

    if [[ "${resume_mode}" != "0" && "${resume_mode}" != "1" ]]; then
        echo "DOKA_BENCHMARK_RESUME must be 0 or 1." >&2
        exit 1
    fi

    if [[ ! "${benchmark_run_id}" =~ ^[0-9A-Za-z._-]+$ ]]; then
        echo "Benchmark run ID '${benchmark_run_id}' contains unsupported characters." >&2
        exit 1
    fi

    if [[ ! "${runner_class}" =~ ^[0-9A-Za-z._-]+$ ]]; then
        echo "Benchmark runner class '${runner_class}' contains unsupported characters." >&2
        exit 1
    fi

    if [[ ! "${compose_project_name}" =~ ^[a-z0-9][a-z0-9_-]+$ ]]; then
        echo "Benchmark Compose project '${compose_project_name}' is invalid." >&2
        exit 1
    fi

    if [[ ! "${benchmark_target_port}" =~ ^[0-9]+$ ]] \
        || (( 10#${benchmark_target_port} > 65535 )); then
        echo "Benchmark port '${benchmark_target_port}' is invalid." >&2
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

    # Benchmark comparisons are meaningful only against the exact image named
    # by the versioned performance contract.
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

        # Container health alone does not prove the published socket is ready
        # for a client on the host network.
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

    if [[ -z "${port_matches}" && "${mode}" == "test-only" ]]; then
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

    # A unique port owner prevents an unrelated local database from producing
    # evidence under the selected target label.
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

    if [[ "${benchmark_target_port}" == "0" ]]; then
        local port_output
        port_output="$("${compose_command[@]}" port "${benchmark_compose_service}" 3306)"
        benchmark_target_port="${port_output##*:}"
    fi

    export DOKA_BENCHMARK_DATABASE_PORT="${benchmark_target_port}"
}

stop_compose_stack() {
    echo "Stopping bundled benchmark Compose stack..."
    "${compose_command[@]}" down --volumes --remove-orphans
}

ensure_fresh_run_directory() {
    if [[ -d "${benchmark_report_dir}" ]] \
        && find "${benchmark_report_dir}" -mindepth 1 -print -quit | grep -q .; then
        if [[ "${resume_mode}" != "1" ]]; then
            echo "Current-run benchmark directory '${benchmark_report_dir}' is not empty." >&2
            echo "Use a new run ID, or explicitly resume the same identity with DOKA_BENCHMARK_RESUME=1." >&2
            exit 1
        fi

        echo "Resuming benchmark run ${benchmark_run_id}; checkpoints remain identity-validated."
    elif [[ "${resume_mode}" != "1" \
        && -d "${workload_checkpoint_dir}" \
        && -n "$(find "${workload_checkpoint_dir}" -mindepth 1 -print -quit)" ]]; then
        echo "Workload checkpoints already exist for run ${benchmark_run_id}." >&2
        echo "Use a new run ID, or explicitly resume the same identity." >&2
        exit 1
    fi

    mkdir -p "${benchmark_evidence_dir}"
    mkdir -p "${workload_checkpoint_dir}"
}

# Shared with the paired comparison so both orchestrations export exactly the
# identity the measurement report requires.
# shellcheck source=eng/performance/host-preflight.sh
source "${repo_root}/eng/performance/host-preflight.sh"


run_host_preflight() {
    capture_host_preflight "${host_evidence}" "${performance_contract}" "${evidence_module}"
}

run_benchmarkdotnet() {
    if [[ "${resume_mode}" == "1" && -d "${benchmark_report_dir}/results" ]]; then
        local archive_directory="${benchmark_artifacts_dir}/resume-archives/${benchmark_run_id}"
        local archive_path
        archive_path="${archive_directory}/results-$(date -u +%Y%m%dT%H%M%SZ)-$$"

        mkdir -p "${archive_directory}"
        mv "${benchmark_report_dir}/results" "${archive_path}"
        echo "Archived incomplete BenchmarkDotNet output to ${archive_path}."
    fi

    local benchmark_filters=(--filter)
    local benchmark_name

    while IFS= read -r benchmark_name; do
        if [[ ! "${benchmark_name}" =~ ^[A-Za-z_][A-Za-z0-9_.]*$ ]]; then
            echo "Invalid BenchmarkDotNet control name '${benchmark_name}'." >&2
            exit 1
        fi

        benchmark_filters+=("*${benchmark_name}*")
    done < <(
        jq -r \
            '.benchmarkDotNetControls[]
             | [.type + "." + .method,
                (if has("baselineMethod") then .type + "." + .baselineMethod else empty end)]
             | .[]' \
            "${performance_contract}" \
            | sort -u
    )

    if (( ${#benchmark_filters[@]} == 1 )); then
        echo "The performance contract defines no BenchmarkDotNet controls." >&2
        exit 1
    fi

    dotnet "${benchmark_assembly}" \
        "${benchmark_filters[@]}" \
        --artifacts "${benchmark_report_dir}"

    # Validate raw BenchmarkDotNet output before any summarized evaluation can
    # turn a failed or incomplete benchmark process into apparent evidence.
    python3 -m "${evidence_module}" validate-bdn \
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

confirm_historical_tail_if_required() {
    if [[ "${baseline_mode}" != "compare" ]]; then
        return 0
    fi

    python3 -m "${evidence_module}" plan-tail-confirmation \
        --contract "${performance_contract}" \
        --baseline "${baseline_manifest}" \
        --workloads "${workload_evidence}" \
        --run-id "${benchmark_run_id}" \
        --target "${benchmark_target}" \
        --profile "${benchmark_profile}" \
        --output "${tail_confirmation_plan}"

    local workload_count
    workload_count="$(jq -er '.workloads | length' "${tail_confirmation_plan}")"
    if (( workload_count == 0 )); then
        return 0
    fi

    # A single p99 observation can be dominated by a scheduler or server
    # maintenance burst. Confirm only the failing normalized tail on two fresh
    # admitted snapshots and merge their calibrated samples before enforcement.
    mkdir -p "${tail_confirmation_dir}"
    local confirmation_arguments=()
    local workload_index
    local workload_id
    local safe_workload_id
    local confirmation_runs
    local run_index
    local confirmation_host
    local confirmation_report

    for (( workload_index = 0; workload_index < workload_count; workload_index++ )); do
        workload_id="$(
            jq -er ".workloads[${workload_index}].workloadId" \
                "${tail_confirmation_plan}"
        )"
        confirmation_runs="$(
            jq -er ".workloads[${workload_index}].confirmationRuns" \
                "${tail_confirmation_plan}"
        )"
        safe_workload_id="${workload_id//[^0-9A-Za-z._-]/_}"

        for (( run_index = 1; run_index <= confirmation_runs; run_index++ )); do
            confirmation_host="${tail_confirmation_dir}/${safe_workload_id}.${run_index}.host.json"
            confirmation_report="${tail_confirmation_dir}/${safe_workload_id}.${run_index}.json"

            capture_host_preflight \
                "${confirmation_host}" "${performance_contract}" "${evidence_module}"
            dotnet "${benchmark_assembly}" \
                --workload "${workload_id}" \
                "${confirmation_report}"
            confirmation_arguments+=(
                --confirmation "${confirmation_report}"
                --confirmation-host "${confirmation_host}"
            )
        done
    done

    local merged_workloads="${benchmark_evidence_dir}/workload-evidence.merged.json"
    python3 -m "${evidence_module}" merge-tail-confirmations \
        --contract "${performance_contract}" \
        --workloads "${workload_evidence}" \
        --plan "${tail_confirmation_plan}" \
        --run-id "${benchmark_run_id}" \
        --target "${benchmark_target}" \
        --profile "${benchmark_profile}" \
        --output "${merged_workloads}" \
        "${confirmation_arguments[@]}"
    mv "${merged_workloads}" "${workload_evidence}"
}

run_soak_if_required() {
    if [[ "${benchmark_profile}" == "smoke" ]]; then
        return 0
    fi

    dotnet "${benchmark_assembly}" --soak "${soak_evidence}"
}

evaluate_current_run() {
    # The evaluator owns absolute and historical budget decisions. The shell
    # only selects the optional soak input that exists for this profile.
    local command=(
        python3 -m "${evidence_module}" evaluate
        --contract "${performance_contract}"
        --baseline "${baseline_manifest}"
        --host "${host_evidence}"
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
        echo "- hostPreflightEvidence: ${host_evidence}"
        echo "- benchmarkDotNetEvidence: ${bdn_evidence}"
        echo "- workloadEvidence: ${workload_evidence}"
        echo "- workloadCheckpointDirectory: ${workload_checkpoint_dir}"
        echo "- tailConfirmationPlan: ${tail_confirmation_plan}"
        echo "- tailConfirmationDirectory: ${tail_confirmation_dir}"
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

    "${repo_root}/eng/common/verify-dotnet.sh"
    export DOKA_BENCHMARK_PROFILE="${benchmark_profile}"
    export DOKA_BENCHMARK_TARGET="${benchmark_target}"
    export DOKA_BENCHMARK_RUN_ID="${benchmark_run_id}"
    export DOKA_BENCHMARK_RUNNER_CLASS="${runner_class}"
    export DOKA_BENCHMARK_COMMIT="${benchmark_commit}"
    export DOKA_BENCHMARK_SOURCE_HASH="${benchmark_source_hash}"
    export DOKA_BENCHMARK_SERVER_IMAGE="${verified_server_image}"
    export DOKA_BENCHMARK_CHECKPOINT_DIRECTORY="${workload_checkpoint_dir}"

    dotnet restore "${benchmark_project}" --tl:off
    dotnet build "${benchmark_project}" \
        --configuration Release \
        --no-restore \
        --tl:off \
        -m:1

    # Build activity is intentionally outside the host-admission boundary.
    run_host_preflight

    # Run provider workloads immediately after the accepted host snapshot.
    # BenchmarkDotNet creates sustained CPU load of its own and therefore runs
    # only after the latency and allocation matrix has completed.
    run_workload_matrix
    confirm_historical_tail_if_required
    run_benchmarkdotnet
    run_soak_if_required
    evaluate_current_run
    write_summary
}

configure_benchmark_target
validate_configuration
export DOKA_BENCHMARK_DATABASE_PORT="${benchmark_target_port}"

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

case "${comparison_mode}" in
    historical)
        run_benchmarks
        ;;
    paired)
        # The paired orchestration builds a second provider revision and
        # alternates the two sides, so it owns the measurement loop. The
        # container lifecycle above is already established for it, and the
        # verified image digest is handed over rather than re-derived: it is
        # part of the identity both sides must share, and the check that
        # produced it has already run.
        "${repo_root}/eng/common/verify-dotnet.sh"
        DOKA_BENCHMARK_TARGET="${benchmark_target}" \
        DOKA_BENCHMARK_RUN_ID="${benchmark_run_id}" \
        DOKA_BENCHMARK_COMMIT="${benchmark_commit}" \
        DOKA_BENCHMARK_SOURCE_HASH="${benchmark_source_hash}" \
        DOKA_BENCHMARK_RUNNER_CLASS="${runner_class}" \
        DOKA_BENCHMARK_SERVER_IMAGE="${verified_server_image}" \
            bash "${repo_root}/eng/performance/paired-benchmark.sh"
        ;;
    *)
        echo "Unsupported comparison mode '${comparison_mode}'." >&2
        exit 1
        ;;
esac
