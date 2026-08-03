#!/usr/bin/env bash

# Proves the provider's ordinary and fully trimmed self-contained runtime paths
# against MySQL 8.4 while keeping container ownership explicit per invocation.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_smoke_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.RuntimeSmoke"
runtime_smoke_project+="/Doka.EntityFrameworkCore.MySql.RuntimeSmoke.csproj"
compose_file="${repo_root}/docker/compose.yml"
runtime_run_id="${DOKA_RUNTIME_POSTURE_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
repo_fingerprint="$(printf '%s' "${repo_root}" | cksum | awk '{print $1}')"
compose_run_id="$(printf '%s' "${runtime_run_id}" | tr '[:upper:]' '[:lower:]')"
compose_project_name="${DOKA_RUNTIME_COMPOSE_PROJECT_NAME:-doka-runtime-${repo_fingerprint}-${compose_run_id}}"
compose_command=(docker compose -p "${compose_project_name}" -f "${compose_file}")
wait_timeout_seconds=60
wait_interval_seconds=2
health_status_format='{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}'
mysql_host="127.0.0.1"
mysql_port="${DOKA_RUNTIME_MYSQL_PORT:-33068}"
runtime_smoke_name="Doka.EntityFrameworkCore.MySql.RuntimeSmoke"
runtime_artifacts_root="${DOKA_RUNTIME_POSTURE_ARTIFACTS_ROOT:-${repo_root}/artifacts/runtime-smoke}"
runtime_evidence_dir="${DOKA_RUNTIME_POSTURE_EVIDENCE_DIR:-${runtime_artifacts_root}/${runtime_run_id}}"
trimmed_output_dir="${DOKA_RUNTIME_POSTURE_PUBLISH_DIR:-${runtime_evidence_dir}/trimmed}"
runtime_summary_file="${runtime_evidence_dir}/runtime-posture-summary.md"
runtime_evidence_file="${runtime_evidence_dir}/runtime-posture-evidence.json"
mode="ensure-up"
should_stop_stack_on_exit=0

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/test-runtime-posture.sh
  ./eng/test-runtime-posture.sh --test-only
  ./eng/test-runtime-posture.sh --down
  ./eng/test-runtime-posture.sh --up-test-down

Modes:
  (no args)        Ensure the bundled MySQL 8.4 Compose service is up, then run the runtime posture smoke path.
  --test-only      Run the runtime posture smoke path without starting the bundled Compose service.
  --down           Stop and remove the bundled Compose stack without running the runtime posture smoke path.
  --up-test-down   Start the bundled Compose service, run the runtime posture smoke path, then stop the stack again.
EOF
}

cleanup() {
    local exit_code="$1"

    if [[ "${should_stop_stack_on_exit}" -eq 1 ]]; then
        set +e
        echo "Stopping bundled runtime-posture Compose stack..."
        "${compose_command[@]}" down --volumes --remove-orphans
        local down_exit_code=$?
        set -e

        # Teardown is part of a successful owned-stack run. Preserve the smoke
        # failure when both execution and cleanup fail.
        if [[ "${exit_code}" -eq 0 && "${down_exit_code}" -ne 0 ]]; then
            exit_code="${down_exit_code}"
        fi
    fi

    exit "${exit_code}"
}

trap 'cleanup "$?"' EXIT

ensure_docker_compose_available() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "docker is required for the bundled runtime-posture path." >&2
        echo "Use --test-only only when the local MySQL 8.4 smoke target is already reachable on ${mysql_host}:${mysql_port}." >&2
        exit 1
    fi

    if ! docker compose version >/dev/null 2>&1; then
        echo "docker compose is required for the bundled runtime-posture path." >&2
        echo "Use --test-only only when the local MySQL 8.4 smoke target is already reachable on ${mysql_host}:${mysql_port}." >&2
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

wait_for_mysql() {
    local start_time
    local current_time
    local elapsed_seconds
    local health_status
    local mysql_container_id

    echo "Waiting for MySQL 8.4 runtime-smoke target on ${mysql_host}:${mysql_port}..."
    start_time="$(date +%s)"

    while true; do
        mysql_container_id="$("${compose_command[@]}" ps -q mysql84 2>/dev/null || true)"
        health_status=""
        if [[ -n "${mysql_container_id}" ]]; then
            health_status="$(
                docker inspect \
                    --format "${health_status_format}" \
                    "${mysql_container_id}" \
                    2>/dev/null \
                    || true
            )"
        fi

        # Require both container health and host reachability; either signal on
        # its own can race the first client connection.
        if [[ "${health_status}" == "healthy" ]] && can_connect "${mysql_host}" "${mysql_port}"; then
            echo "MySQL 8.4 runtime-smoke target is reachable."
            return 0
        fi

        current_time="$(date +%s)"
        elapsed_seconds=$(( current_time - start_time ))

        if (( elapsed_seconds >= wait_timeout_seconds )); then
            echo "Timed out waiting for MySQL 8.4 on ${mysql_host}:${mysql_port} after ${wait_timeout_seconds} seconds." >&2
            exit 1
        fi

        sleep "${wait_interval_seconds}"
    done
}

start_compose_stack() {
    local published_endpoint
    local runtime_connection_string

    echo "Starting bundled runtime-posture MySQL 8.4 Compose service..."
    export DOKA_MYSQL84_PORT="${mysql_port}"
    "${compose_command[@]}" up -d mysql84

    if [[ "${mysql_port}" == "0" ]]; then
        published_endpoint="$("${compose_command[@]}" port mysql84 3306)"
        mysql_port="${published_endpoint##*:}"
    fi

    # The smoke application accepts an override so an owned release run can
    # use an ephemeral host port instead of colliding with a developer stack.
    runtime_connection_string="Server=${mysql_host};Port=${mysql_port};"
    runtime_connection_string+="User ID=root;Password=root_password;"
    export DOKA_RUNTIME_SMOKE_CONNECTION_STRING="${runtime_connection_string}"
}

stop_compose_stack() {
    echo "Stopping bundled runtime-posture Compose stack..."
    "${compose_command[@]}" down --volumes --remove-orphans
}

resolve_runtime_identifier() {
    local os_name
    local architecture

    os_name="$(uname -s)"
    architecture="$(uname -m)"

    case "${os_name}-${architecture}" in
        Darwin-arm64)
            echo "osx-arm64"
            ;;
        Darwin-x86_64)
            echo "osx-x64"
            ;;
        Linux-x86_64)
            echo "linux-x64"
            ;;
        Linux-aarch64|Linux-arm64)
            echo "linux-arm64"
            ;;
        *)
            echo "Unsupported runtime posture host: ${os_name}-${architecture}" >&2
            exit 1
            ;;
    esac
}

runtime_smoke_executable_path() {
    local publish_dir="$1"

    echo "${publish_dir}/${runtime_smoke_name}"
}

sha256_file() {
    local path="$1"

    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "${path}" | awk '{print $1}'
        return
    fi

    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "${path}" | awk '{print $1}'
        return
    fi

    echo "sha256sum or shasum is required to bind runtime-posture evidence." >&2
    exit 1
}

resolve_runtime_target_image() {
    local mysql_container_id

    if [[ -n "${DOKA_RUNTIME_TARGET_IMAGE:-}" ]]; then
        echo "${DOKA_RUNTIME_TARGET_IMAGE}"
        return
    fi

    mysql_container_id="$("${compose_command[@]}" ps -q mysql84 2>/dev/null || true)"
    if [[ -n "${mysql_container_id}" ]]; then
        docker inspect --format '{{.Config.Image}}' "${mysql_container_id}"
        return
    fi

    # The test-only mode can target a database owned by the caller. Keep the
    # missing identity visible instead of guessing which image is listening.
    echo "unknown"
}

prepare_runtime_output() {
    if [[ -d "${trimmed_output_dir}" \
        && -n "$(find "${trimmed_output_dir}" -mindepth 1 -print -quit)" ]]; then
        echo "Runtime publish directory already contains artifacts: ${trimmed_output_dir}" >&2
        echo "Use a new DOKA_RUNTIME_POSTURE_RUN_ID so stale binaries cannot enter the evidence." >&2
        exit 1
    fi

    mkdir -p "${runtime_evidence_dir}" "${trimmed_output_dir}"
}

write_runtime_evidence() {
    local runtime_identifier="$1"
    local trimmed_executable="$2"
    local executable_sha256
    local executable_size
    local source_commit
    local source_tree_state
    local target_image

    executable_sha256="$(sha256_file "${trimmed_executable}")"
    executable_size="$(wc -c < "${trimmed_executable}" | tr -d ' ')"
    source_commit="$(git -C "${repo_root}" rev-parse HEAD)"
    source_tree_state="clean"
    if [[ -n "$(git -C "${repo_root}" status --porcelain --untracked-files=all)" ]]; then
        source_tree_state="dirty"
    fi
    target_image="$(resolve_runtime_target_image)"

    cat > "${runtime_summary_file}" <<EOF
# Runtime posture summary

- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
- runId: ${runtime_run_id}
- sourceCommit: ${source_commit}
- sourceTreeState: ${source_tree_state}
- target: mysql84
- targetImage: ${target_image}
- runtimeIdentifier: ${runtime_identifier}
- ordinaryExecution: pass
- fullTrimPublish: pass
- trimmedExecution: pass
- executableSha256: ${executable_sha256}

The ordinary application and the self-contained binary published with
PublishTrimmed=true and TrimMode=full both executed the provider smoke contract.
EOF

    cat > "${runtime_evidence_file}" <<EOF
{
  "schemaVersion": 1,
  "generatedUtc": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "runId": "${runtime_run_id}",
  "source": {
    "commit": "${source_commit}",
    "treeState": "${source_tree_state}"
  },
  "target": {
    "targetId": "mysql84",
    "image": "${target_image}"
  },
  "runtimeIdentifier": "${runtime_identifier}",
  "dotnetSdk": "$(dotnet --version)",
  "configuration": "Release",
  "ordinaryExecution": "pass",
  "publish": {
    "selfContained": true,
    "publishTrimmed": true,
    "trimMode": "full",
    "status": "pass"
  },
  "trimmedExecution": "pass",
  "executable": {
    "sha256": "${executable_sha256}",
    "sizeBytes": ${executable_size}
  }
}
EOF
}

run_runtime_posture() {
    local runtime_identifier
    local trimmed_executable

    runtime_identifier="$(resolve_runtime_identifier)"
    trimmed_executable="$(runtime_smoke_executable_path "${trimmed_output_dir}")"

    prepare_runtime_output
    "${repo_root}/eng/verify-dotnet.sh"

    dotnet restore "${runtime_smoke_project}"

    dotnet run --project "${runtime_smoke_project}" --configuration Release --no-restore --disable-build-servers

    # Executing the published binary, rather than accepting publish success,
    # catches provider paths removed by trimming.
    dotnet publish "${runtime_smoke_project}" \
        --configuration Release \
        --runtime "${runtime_identifier}" \
        --self-contained true \
        -p:PublishTrimmed=true \
        -p:TrimMode=full \
        -o "${trimmed_output_dir}" \
        --disable-build-servers

    "${trimmed_executable}"

    write_runtime_evidence "${runtime_identifier}" "${trimmed_executable}"

    # NativeAOT publish + smoke is intentionally not run. EF Core 10 NativeAOT
    # is upstream-experimental (Microsoft Learn); the provider's Design.Internal
    # assembly reference forces the AOT publish to load Microsoft.EntityFrameworkCore.Design
    # which is not AOT-friendly (heavy reflection, [RequiresUnreferencedCode]
    # everywhere). The ecosystem-wide fix sits with EF Core's precompiled-
    # queries + provider-AOT story, not with a provider-side assembly split.
    # See ADR D-017 for the full record + re-evaluation trigger.
}

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
        --up-test-down)
            mode="up-test-down"
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
        wait_for_mysql
        ;;
    test-only)
        ;;
    down)
        ensure_docker_compose_available
        stop_compose_stack
        exit 0
        ;;
    up-test-down)
        ensure_docker_compose_available
        start_compose_stack
        wait_for_mysql
        ;;
esac

run_runtime_posture

echo "Runtime posture gate passed."
echo "Evidence: ${runtime_evidence_file}"
