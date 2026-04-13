#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
integration_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Doka.EntityFrameworkCore.MySql.IntegrationTests.csproj"
compose_file="${repo_root}/docker/compose.yml"
compose_command=(docker compose -f "${compose_file}")
wait_timeout_seconds=60
wait_interval_seconds=2
integration_run_id="${DOKA_INTEGRATION_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
integration_artifacts_dir="${repo_root}/artifacts/integration/${integration_run_id}"
integration_summary_file="${integration_artifacts_dir}/compatibility-matrix-summary.md"
integration_evidence_file="${integration_artifacts_dir}/compatibility-matrix-evidence.json"
integration_targets_var="DOKA_INTEGRATION_TARGETS"

mysql80_env_var="DOKA_MYSQL80_CONNECTION_STRING"
mysql84_env_var="DOKA_MYSQL84_CONNECTION_STRING"
mariadb114_env_var="DOKA_MARIADB114_CONNECTION_STRING"
mariadb118_env_var="DOKA_MARIADB118_CONNECTION_STRING"
mysql80_target_id="mysql80"
mysql84_target_id="mysql84"
mariadb114_target_id="mariadb114"
mariadb118_target_id="mariadb118"

mysql80_container_name="doka-mysql80"
mysql84_container_name="doka-mysql84"
mariadb114_container_name="doka-mariadb114"
mariadb118_container_name="doka-mariadb118"
mysql80_host="127.0.0.1"
mysql80_port="33066"
mysql84_host="127.0.0.1"
mysql84_port="33068"
mariadb114_host="127.0.0.1"
mariadb114_port="33067"
mariadb118_host="127.0.0.1"
mariadb118_port="33069"

mode="ensure-up"
should_stop_stack_on_exit=0
configured_target_selection="${DOKA_INTEGRATION_TARGETS:-}"
target_selection_label="all-supported-targets"
mysql80_target_enabled=1
mysql84_target_enabled=1
mariadb114_target_enabled=1
mariadb118_target_enabled=1

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/test-integration.sh
  ./eng/test-integration.sh --test-only
  ./eng/test-integration.sh --down
  ./eng/test-integration.sh --up-test-down

Modes:
  (no args)        Ensure the bundled Compose stack is up when local defaults are needed, then run integration tests.
  --test-only      Run integration tests without starting the bundled Compose stack.
  --down           Stop and remove the bundled Compose stack without running tests.
  --up-test-down   Start the bundled Compose stack, run integration tests, then stop the stack again.

Environment:
  DOKA_INTEGRATION_TARGETS=mysql80,mysql84,mariadb114,mariadb118
EOF
}

cleanup() {
    local exit_code="$1"

    if [[ "${should_stop_stack_on_exit}" -eq 1 ]]; then
        set +e
        echo "Stopping bundled integration-test Compose stack..."
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
        echo "docker is required when local integration-test defaults are used." >&2
        echo "Set ${integration_targets_var} and the corresponding connection-string environment variables to external targets, or run 'docker compose -f docker/compose.yml up -d'." >&2
        exit 1
    fi

    if ! docker compose version >/dev/null 2>&1; then
        echo "docker compose is required when local integration-test defaults are used." >&2
        echo "Set ${integration_targets_var} and the corresponding connection-string environment variables to external targets, or run 'docker compose -f docker/compose.yml up -d'." >&2
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

configure_target_selection() {
    local normalized_selection
    local normalized_target

    if [[ -z "${configured_target_selection}" ]]; then
        return 0
    fi

    mysql80_target_enabled=0
    mysql84_target_enabled=0
    mariadb114_target_enabled=0
    mariadb118_target_enabled=0
    normalized_selection=""

    IFS=',' read -r -a configured_targets <<< "${configured_target_selection}"

    for raw_target in "${configured_targets[@]}"; do
        normalized_target="$(echo "${raw_target}" | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]')"

        if [[ -z "${normalized_target}" ]]; then
            continue
        fi

        case "${normalized_target}" in
            "${mysql80_target_id}")
                mysql80_target_enabled=1
                ;;
            "${mysql84_target_id}")
                mysql84_target_enabled=1
                ;;
            "${mariadb114_target_id}")
                mariadb114_target_enabled=1
                ;;
            "${mariadb118_target_id}")
                mariadb118_target_enabled=1
                ;;
            *)
                echo "Unsupported integration target '${normalized_target}' in ${integration_targets_var}." >&2
                echo "Supported values are: ${mysql80_target_id}, ${mysql84_target_id}, ${mariadb114_target_id}, ${mariadb118_target_id}." >&2
                exit 1
                ;;
        esac

        if [[ -z "${normalized_selection}" ]]; then
            normalized_selection="${normalized_target}"
        else
            normalized_selection="${normalized_selection},${normalized_target}"
        fi
    done

    if [[ -z "${normalized_selection}" ]]; then
        echo "${integration_targets_var} must contain at least one supported target id when it is configured." >&2
        exit 1
    fi

    target_selection_label="${normalized_selection}"
}

wait_for_target() {
    local host="$1"
    local port="$2"
    local target_name="$3"
    local container_name="$4"
    local start_time
    local current_time
    local elapsed_seconds
    local health_status

    echo "Waiting for ${target_name} on ${host}:${port}..."
    start_time="$(date +%s)"

    while true; do
        health_status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "${container_name}" 2>/dev/null || true)"

        if [[ "${health_status}" == "healthy" ]] && can_connect "${host}" "${port}"; then
            echo "${target_name} is reachable on ${host}:${port}."
            return 0
        fi

        current_time="$(date +%s)"
        elapsed_seconds=$(( current_time - start_time ))

        if (( elapsed_seconds >= wait_timeout_seconds )); then
            echo "Timed out waiting for ${target_name} on ${host}:${port} after ${wait_timeout_seconds} seconds." >&2
            exit 1
        fi

        sleep "${wait_interval_seconds}"
    done
}

start_compose_stack() {
    local compose_services=()

    if [[ "${mysql80_target_enabled}" -eq 1 ]]; then
        compose_services+=("${mysql80_container_name#doka-}")
    fi

    if [[ "${mysql84_target_enabled}" -eq 1 ]]; then
        compose_services+=("${mysql84_container_name#doka-}")
    fi

    if [[ "${mariadb114_target_enabled}" -eq 1 ]]; then
        compose_services+=("${mariadb114_container_name#doka-}")
    fi

    if [[ "${mariadb118_target_enabled}" -eq 1 ]]; then
        compose_services+=("${mariadb118_container_name#doka-}")
    fi

    if [[ "${#compose_services[@]}" -eq 0 ]]; then
        echo "No repo-local integration targets are selected; skipping bundled Compose startup."
        return 0
    fi

    echo "Starting bundled integration-test Compose stack..."
    "${compose_command[@]}" up -d "${compose_services[@]}"
}

stop_compose_stack() {
    echo "Stopping bundled integration-test Compose stack..."
    "${compose_command[@]}" down
}

run_integration_tests() {
    "${repo_root}/eng/verify-dotnet.sh"
    dotnet restore "${integration_test_project}"
    dotnet test "${integration_test_project}" --configuration Release --no-restore
}

target_source_label() {
    local env_is_set="$1"
    local env_name="$2"

    if [[ "${env_is_set}" -eq 1 ]]; then
        echo "environment override (${env_name})"
        return 0
    fi

    echo "bundled Compose default"
}

target_source_value() {
    local selected="$1"
    local env_is_set="$2"

    if [[ "${selected}" -eq 0 ]]; then
        echo "not-selected"
        return 0
    fi

    if [[ "${env_is_set}" -eq 1 ]]; then
        echo "environment"
        return 0
    fi

    echo "compose-default"
}

write_matrix_evidence() {
    mkdir -p "${integration_artifacts_dir}"

    {
        echo "# Compatibility matrix summary"
        echo
        echo "- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
        echo "- integrationRunId: ${integration_run_id}"
        echo "- mode: ${mode}"
        echo "- targetSelection: ${target_selection_label}"
        echo
        echo "## Repo-local targets"
        echo
        echo "- MySQL 8.0 (${mysql80_target_id}): $(target_source_value "${mysql80_target_enabled}" "${mysql80_env_is_set}")"
        echo "- MySQL 8.4 (${mysql84_target_id}): $(target_source_value "${mysql84_target_enabled}" "${mysql84_env_is_set}")"
        echo "- MariaDB 11.4 (${mariadb114_target_id}): $(target_source_value "${mariadb114_target_enabled}" "${mariadb114_env_is_set}")"
        echo "- MariaDB 11.8 (${mariadb118_target_id}): $(target_source_value "${mariadb118_target_enabled}" "${mariadb118_env_is_set}")"
        echo
        echo "This evidence covers the credential-free repo-local compatibility matrix only."
    } > "${integration_summary_file}"

    cat > "${integration_evidence_file}" <<EOF
{
  "generatedUtc": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "integrationRunId": "${integration_run_id}",
  "mode": "${mode}",
  "targetSelection": "${target_selection_label}",
  "targets": [
    {
      "targetId": "${mysql80_target_id}",
      "name": "MySQL 8.0",
      "engineFamily": "MySQL",
      "serverVersion": "8.0",
      "selected": ${mysql80_target_enabled},
      "source": "$(target_source_value "${mysql80_target_enabled}" "${mysql80_env_is_set}")",
      "environmentVariable": "${mysql80_env_var}"
    },
    {
      "targetId": "${mysql84_target_id}",
      "name": "MySQL 8.4",
      "engineFamily": "MySQL",
      "serverVersion": "8.4",
      "selected": ${mysql84_target_enabled},
      "source": "$(target_source_value "${mysql84_target_enabled}" "${mysql84_env_is_set}")",
      "environmentVariable": "${mysql84_env_var}"
    },
    {
      "targetId": "${mariadb114_target_id}",
      "name": "MariaDB 11.4",
      "engineFamily": "MariaDB",
      "serverVersion": "11.4",
      "selected": ${mariadb114_target_enabled},
      "source": "$(target_source_value "${mariadb114_target_enabled}" "${mariadb114_env_is_set}")",
      "environmentVariable": "${mariadb114_env_var}"
    },
    {
      "targetId": "${mariadb118_target_id}",
      "name": "MariaDB 11.8",
      "engineFamily": "MariaDB",
      "serverVersion": "11.8",
      "selected": ${mariadb118_target_enabled},
      "source": "$(target_source_value "${mariadb118_target_enabled}" "${mariadb118_env_is_set}")",
      "environmentVariable": "${mariadb118_env_var}"
    }
  ]
}
EOF
}

configure_target_selection

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

mysql80_env_is_set=0
mysql84_env_is_set=0
mariadb114_env_is_set=0
mariadb118_env_is_set=0

if [[ -n "${DOKA_MYSQL80_CONNECTION_STRING:-}" ]]; then
    mysql80_env_is_set=1
fi

if [[ -n "${DOKA_MYSQL84_CONNECTION_STRING:-}" ]]; then
    mysql84_env_is_set=1
fi

if [[ -n "${DOKA_MARIADB114_CONNECTION_STRING:-}" ]]; then
    mariadb114_env_is_set=1
fi

if [[ -n "${DOKA_MARIADB118_CONNECTION_STRING:-}" ]]; then
    mariadb118_env_is_set=1
fi

should_start_compose=0
should_wait_for_mysql80=0
should_wait_for_mysql84=0
should_wait_for_mariadb114=0
should_wait_for_mariadb118=0

case "${mode}" in
    ensure-up)
        if [[ ( "${mysql80_target_enabled}" -eq 1 && "${mysql80_env_is_set}" -eq 0 ) \
            || ( "${mysql84_target_enabled}" -eq 1 && "${mysql84_env_is_set}" -eq 0 ) \
            || ( "${mariadb114_target_enabled}" -eq 1 && "${mariadb114_env_is_set}" -eq 0 ) \
            || ( "${mariadb118_target_enabled}" -eq 1 && "${mariadb118_env_is_set}" -eq 0 ) ]]; then
            should_start_compose=1
        fi

        if [[ "${mysql80_target_enabled}" -eq 1 && "${mysql80_env_is_set}" -eq 0 ]]; then
            should_wait_for_mysql80=1
        fi

        if [[ "${mysql84_target_enabled}" -eq 1 && "${mysql84_env_is_set}" -eq 0 ]]; then
            should_wait_for_mysql84=1
        fi

        if [[ "${mariadb114_target_enabled}" -eq 1 && "${mariadb114_env_is_set}" -eq 0 ]]; then
            should_wait_for_mariadb114=1
        fi

        if [[ "${mariadb118_target_enabled}" -eq 1 && "${mariadb118_env_is_set}" -eq 0 ]]; then
            should_wait_for_mariadb118=1
        fi
        ;;
    test-only)
        ;;
    down)
        ensure_docker_compose_available
        stop_compose_stack
        exit 0
        ;;
    up-test-down)
        if [[ "${mysql80_target_enabled}" -eq 1 || "${mysql84_target_enabled}" -eq 1 || "${mariadb114_target_enabled}" -eq 1 || "${mariadb118_target_enabled}" -eq 1 ]]; then
            should_start_compose=1
            should_stop_stack_on_exit=1
        fi
        should_wait_for_mysql80="${mysql80_target_enabled}"
        should_wait_for_mysql84="${mysql84_target_enabled}"
        should_wait_for_mariadb114="${mariadb114_target_enabled}"
        should_wait_for_mariadb118="${mariadb118_target_enabled}"
        ;;
esac

if [[ "${should_start_compose}" -eq 1 ]]; then
    ensure_docker_compose_available

    if [[ "${mode}" == "up-test-down" && ( "${mysql80_env_is_set}" -eq 1 || "${mysql84_env_is_set}" -eq 1 || "${mariadb114_env_is_set}" -eq 1 || "${mariadb118_env_is_set}" -eq 1 ) ]]; then
        echo "Explicit integration-test environment variables remain in effect; the bundled Compose stack is started in addition to them."
    fi

    start_compose_stack
fi

if [[ "${should_wait_for_mysql80}" -eq 1 ]]; then
    wait_for_target "${mysql80_host}" "${mysql80_port}" "MySQL 8.0" "${mysql80_container_name}"
fi

if [[ "${should_wait_for_mysql84}" -eq 1 ]]; then
    wait_for_target "${mysql84_host}" "${mysql84_port}" "MySQL 8.4" "${mysql84_container_name}"
fi

if [[ "${should_wait_for_mariadb114}" -eq 1 ]]; then
    wait_for_target "${mariadb114_host}" "${mariadb114_port}" "MariaDB 11.4" "${mariadb114_container_name}"
fi

if [[ "${should_wait_for_mariadb118}" -eq 1 ]]; then
    wait_for_target "${mariadb118_host}" "${mariadb118_port}" "MariaDB 11.8" "${mariadb118_container_name}"
fi

run_integration_tests
write_matrix_evidence
