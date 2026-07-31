#!/usr/bin/env bash

# Proves the provider's ordinary and fully trimmed self-contained runtime paths
# against MySQL 8.4 while keeping container ownership explicit per invocation.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_smoke_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.RuntimeSmoke/Doka.EntityFrameworkCore.MySql.RuntimeSmoke.csproj"
compose_file="${repo_root}/docker/compose.yml"
compose_command=(docker compose -f "${compose_file}")
wait_timeout_seconds=60
wait_interval_seconds=2
mysql_host="127.0.0.1"
mysql_port="33068"
mysql_container_name="doka-mysql84"
runtime_smoke_name="Doka.EntityFrameworkCore.MySql.RuntimeSmoke"
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
        "${compose_command[@]}" down
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

    echo "Waiting for MySQL 8.4 runtime-smoke target on ${mysql_host}:${mysql_port}..."
    start_time="$(date +%s)"

    while true; do
        health_status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "${mysql_container_name}" 2>/dev/null || true)"

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
    echo "Starting bundled runtime-posture MySQL 8.4 Compose service..."
    "${compose_command[@]}" up -d mysql84
}

stop_compose_stack() {
    echo "Stopping bundled runtime-posture Compose stack..."
    "${compose_command[@]}" down
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

run_runtime_posture() {
    local runtime_identifier
    local trimmed_output_dir
    local trimmed_executable

    runtime_identifier="$(resolve_runtime_identifier)"
    trimmed_output_dir="${repo_root}/artifacts/runtime-smoke/trimmed"
    trimmed_executable="$(runtime_smoke_executable_path "${trimmed_output_dir}")"

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
