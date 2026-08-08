#!/usr/bin/env bash

# Runs the selected compatibility matrix with test-owned containers by default
# or an explicit Compose debugging stack on request. It always records target,
# lifecycle, cleanup, and process outcomes before returning the test exit code.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
integration_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Doka.EntityFrameworkCore.MySql.IntegrationTests.csproj"
compose_file="${repo_root}/docker/compose.yml"
repo_fingerprint="$(printf '%s' "${repo_root}" | cksum | awk '{print $1}')"
compose_project_name="${DOKA_COMPOSE_PROJECT_NAME:-doka-${repo_fingerprint}}"
compose_command=(docker compose -p "${compose_project_name}" -f "${compose_file}")
integration_run_id="${DOKA_INTEGRATION_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"

# Release orchestration overrides this root so lifecycle identity and cleanup
# evidence are hashed inside the same immutable candidate package.
integration_artifacts_dir="${DOKA_INTEGRATION_ARTIFACTS_DIR:-${repo_root}/artifacts/integration/${integration_run_id}}"
integration_summary_file="${integration_artifacts_dir}/compatibility-matrix-summary.md"
integration_evidence_file="${integration_artifacts_dir}/compatibility-matrix-evidence.json"
database_evidence_file="${integration_artifacts_dir}/test-database-evidence.json"
integration_targets_var="DOKA_INTEGRATION_TARGETS"

mysql80_target_id="mysql80"
mysql84_target_id="mysql84"
mariadb114_target_id="mariadb114"
mariadb118_target_id="mariadb118"

# Only the legacy MySQL 8.0 target names its connection-string variable in a
# diagnostic; the supported targets resolve theirs through the test host.
mysql80_env_var="DOKA_MYSQL80_CONNECTION_STRING"

mode="testcontainers"
should_stop_compose_on_exit=0
configured_target_selection="${DOKA_INTEGRATION_TARGETS:-mysql84,mariadb114,mariadb118}"
integration_test_filter="${DOKA_INTEGRATION_TEST_FILTER:-}"
require_full_configuration_matrix="${DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX:-0}"
target_selection_label=""
mysql80_target_enabled=0
mysql84_target_enabled=0
mariadb114_target_enabled=0
mariadb118_target_enabled=0

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/test-integration.sh
  ./eng/test-integration.sh --test-only
  ./eng/test-integration.sh --up-test-down
  ./eng/test-integration.sh --down

Modes:
  (no args)        Run integration tests with test-owned containers.
  --test-only      Alias for the canonical test-owned-container path.
  --up-test-down   Run against the explicit Compose debugging stack, then remove it and its volumes.
  --down           Remove the explicit Compose debugging stack and its volumes.

Environment:
  DOKA_INTEGRATION_TARGETS=mysql84,mariadb114,mariadb118
  DOKA_INTEGRATION_ARTIFACTS_DIR=<evidence output directory>
  DOKA_INTEGRATION_TEST_FILTER=<optional dotnet test filter>
  DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX=0|1
  DOKA_MYSQL84_CONNECTION_STRING=<external override>
  DOKA_MARIADB114_CONNECTION_STRING=<external override>
  DOKA_MARIADB118_CONNECTION_STRING=<external override>

MySQL 8.0 is outside the supported release matrix. Its legacy tests can only be
selected explicitly with DOKA_INTEGRATION_TARGETS=mysql80 and an external
DOKA_MYSQL80_CONNECTION_STRING.
EOF
}

cleanup() {
    local exit_code="$1"

    if [[ "${should_stop_compose_on_exit}" -eq 1 ]]; then
        set +e
        echo "Removing Compose integration-test stack '${compose_project_name}' and its volumes..."
        "${compose_command[@]}" down --volumes --remove-orphans
        local down_exit_code=$?
        set -e

        # Owned resources are part of the test contract. Preserve a test
        # failure, but do not report success when teardown fails.
        if [[ "${exit_code}" -eq 0 && "${down_exit_code}" -ne 0 ]]; then
            exit_code="${down_exit_code}"
        fi
    fi

    exit "${exit_code}"
}

trap 'cleanup "$?"' EXIT

configure_target_selection() {
    local normalized_target

    # Normalize once and export the canonical selection consumed by the test
    # fixture. Unknown targets fail here before resources are created.
    IFS=',' read -r -a configured_targets <<< "${configured_target_selection}"

    for raw_target in "${configured_targets[@]}"; do
        normalized_target="$(echo "${raw_target}" | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]')"

        if [[ -z "${normalized_target}" ]]; then
            continue
        fi

        case "${normalized_target}" in
            "${mysql80_target_id}")
                if [[ "${mysql80_target_enabled}" -eq 1 ]]; then
                    echo "Duplicate integration target '${normalized_target}'." >&2
                    exit 1
                fi
                mysql80_target_enabled=1
                ;;
            "${mysql84_target_id}")
                if [[ "${mysql84_target_enabled}" -eq 1 ]]; then
                    echo "Duplicate integration target '${normalized_target}'." >&2
                    exit 1
                fi
                mysql84_target_enabled=1
                ;;
            "${mariadb114_target_id}")
                if [[ "${mariadb114_target_enabled}" -eq 1 ]]; then
                    echo "Duplicate integration target '${normalized_target}'." >&2
                    exit 1
                fi
                mariadb114_target_enabled=1
                ;;
            "${mariadb118_target_id}")
                if [[ "${mariadb118_target_enabled}" -eq 1 ]]; then
                    echo "Duplicate integration target '${normalized_target}'." >&2
                    exit 1
                fi
                mariadb118_target_enabled=1
                ;;
            *)
                echo "Unsupported integration target '${normalized_target}' in ${integration_targets_var}." >&2
                echo "Accepted values are: mysql80, mysql84, mariadb114, mariadb118." >&2
                exit 1
                ;;
        esac

        if [[ -z "${target_selection_label}" ]]; then
            target_selection_label="${normalized_target}"
        else
            target_selection_label="${target_selection_label},${normalized_target}"
        fi
    done

    if [[ -z "${target_selection_label}" ]]; then
        echo "${integration_targets_var} must contain at least one accepted target id." >&2
        exit 1
    fi

    export DOKA_INTEGRATION_TARGETS="${target_selection_label}"
}

# A release candidate must exercise every supported engine and every test
# category. Keeping this assertion in the shared runner prevents a caller from
# accidentally combining the release flag with a smoke filter or a partial
# target list.
validate_full_configuration_matrix() {
    if [[ "${require_full_configuration_matrix}" != "0" \
        && "${require_full_configuration_matrix}" != "1" ]]; then
        echo "DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX must be either 0 or 1." >&2
        exit 1
    fi

    if [[ "${require_full_configuration_matrix}" != "1" ]]; then
        return 0
    fi

    if [[ "${mysql84_target_enabled}" -ne 1 \
        || "${mariadb114_target_enabled}" -ne 1 \
        || "${mariadb118_target_enabled}" -ne 1 \
        || "${mysql80_target_enabled}" -ne 0 ]]; then
        echo "The full configuration matrix requires mysql84, mariadb114, and mariadb118 only." >&2
        exit 1
    fi

    if [[ -n "${integration_test_filter}" ]]; then
        echo "The full configuration matrix cannot use DOKA_INTEGRATION_TEST_FILTER." >&2
        exit 1
    fi
}

ensure_docker_compose_available() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "docker is required for the explicit Compose debugging path." >&2
        exit 1
    fi

    if ! docker compose version >/dev/null 2>&1; then
        echo "docker compose is required for the explicit Compose debugging path." >&2
        exit 1
    fi
}

configure_compose_overrides() {
    local compose_services=()
    local mysql84_port="${DOKA_MYSQL84_PORT:-33068}"
    local mariadb114_port="${DOKA_MARIADB114_PORT:-33067}"
    local mariadb118_port="${DOKA_MARIADB118_PORT:-33069}"

    if [[ "${mysql80_target_enabled}" -eq 1 && -z "${DOKA_MYSQL80_CONNECTION_STRING:-}" ]]; then
        echo "MySQL 8.0 has no bundled Compose service because it is outside the supported matrix." >&2
        echo "Set ${mysql80_env_var} to run its legacy tests explicitly." >&2
        exit 1
    fi

    if [[ "${mysql84_target_enabled}" -eq 1 && -z "${DOKA_MYSQL84_CONNECTION_STRING:-}" ]]; then
        compose_services+=("mysql84")
        export DOKA_MYSQL84_CONNECTION_STRING="Server=127.0.0.1;Port=${mysql84_port};Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;"
    fi

    if [[ "${mariadb114_target_enabled}" -eq 1 && -z "${DOKA_MARIADB114_CONNECTION_STRING:-}" ]]; then
        compose_services+=("mariadb114")
        export DOKA_MARIADB114_CONNECTION_STRING="Server=127.0.0.1;Port=${mariadb114_port};Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;"
    fi

    if [[ "${mariadb118_target_enabled}" -eq 1 && -z "${DOKA_MARIADB118_CONNECTION_STRING:-}" ]]; then
        compose_services+=("mariadb118")
        export DOKA_MARIADB118_CONNECTION_STRING="Server=127.0.0.1;Port=${mariadb118_port};Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;"
    fi

    # External endpoints and owned Compose services may coexist in one run;
    # only missing selected endpoints receive a local service.
    if [[ "${#compose_services[@]}" -eq 0 ]]; then
        echo "Every selected target uses an external connection-string override; skipping Compose startup."
        return
    fi

    echo "Starting Compose integration-test stack '${compose_project_name}'..."
    should_stop_compose_on_exit=1
    "${compose_command[@]}" up -d --wait --wait-timeout 120 "${compose_services[@]}"
}

run_integration_tests() {
    local coverage_results_dir="${DOKA_COVERAGE_RESULTS_DIR:-${repo_root}/artifacts/coverage/integration}"
    local test_arguments=(
        --configuration Release
        --no-restore
        --tl:off
        --collect:"XPlat Code Coverage"
        --results-directory "${coverage_results_dir}"
        --logger trx
    )

    if [[ -n "${integration_test_filter}" ]]; then
        test_arguments+=(--filter "${integration_test_filter}")
    fi

    mkdir -p "${coverage_results_dir}" "${integration_artifacts_dir}"
    # The .NET fixture owns containers in the canonical path and writes exact
    # image and cleanup identity to the shared evidence file.
    export DOKA_TEST_DATABASE_EVIDENCE_FILE="${DOKA_TEST_DATABASE_EVIDENCE_FILE:-${database_evidence_file}}"

    "${repo_root}/eng/common/verify-dotnet.sh" || return $?
    dotnet restore "${integration_test_project}" --tl:off || return $?
    dotnet test "${integration_test_project}" "${test_arguments[@]}"
}

write_matrix_evidence() {
    local test_exit_code="$1"

    mkdir -p "${integration_artifacts_dir}"

    {
        echo "# Compatibility matrix summary"
        echo
        echo "- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
        echo "- integrationRunId: ${integration_run_id}"
        echo "- mode: ${mode}"
        echo "- targetSelection: ${target_selection_label}"
        echo "- testFilter: ${integration_test_filter:-<all>}"
        echo "- fullConfigurationMatrixRequired: ${require_full_configuration_matrix}"
        echo "- testExitCode: ${test_exit_code}"
        echo "- databaseEvidence: ${DOKA_TEST_DATABASE_EVIDENCE_FILE}"
        echo
        echo "The test database evidence records exact images, dynamic endpoints, ownership source, and cleanup state."
    } > "${integration_summary_file}"

    if [[ -f "${DOKA_TEST_DATABASE_EVIDENCE_FILE}" ]]; then
        jq \
            --arg generatedUtc "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
            --arg integrationRunId "${integration_run_id}" \
            --arg mode "${mode}" \
            --arg targetSelection "${target_selection_label}" \
            --arg testFilter "${integration_test_filter}" \
            --argjson fullConfigurationMatrixRequired "${require_full_configuration_matrix}" \
            --argjson testExitCode "${test_exit_code}" \
            '{
                generatedUtc: $generatedUtc,
                integrationRunId: $integrationRunId,
                mode: $mode,
                targetSelection: $targetSelection,
                testFilter: $testFilter,
                fullConfigurationMatrixRequired: ($fullConfigurationMatrixRequired == 1),
                testExitCode: $testExitCode,
                testDatabase: .
            }' \
            "${DOKA_TEST_DATABASE_EVIDENCE_FILE}" > "${integration_evidence_file}"
    else
        jq -n \
            --arg generatedUtc "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
            --arg integrationRunId "${integration_run_id}" \
            --arg mode "${mode}" \
            --arg targetSelection "${target_selection_label}" \
            --arg testFilter "${integration_test_filter}" \
            --argjson fullConfigurationMatrixRequired "${require_full_configuration_matrix}" \
            --argjson testExitCode "${test_exit_code}" \
            '{
                generatedUtc: $generatedUtc,
                integrationRunId: $integrationRunId,
                mode: $mode,
                targetSelection: $targetSelection,
                testFilter: $testFilter,
                fullConfigurationMatrixRequired: ($fullConfigurationMatrixRequired == 1),
                testExitCode: $testExitCode,
                testDatabase: null
            }' > "${integration_evidence_file}"
    fi
}

if (( $# > 1 )); then
    print_usage >&2
    exit 1
fi

if (( $# == 1 )); then
    case "$1" in
        --test-only)
            ;;
        --up-test-down)
            mode="compose"
            ;;
        --down)
            ensure_docker_compose_available
            "${compose_command[@]}" down --volumes --remove-orphans
            exit 0
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

command -v jq >/dev/null 2>&1 || {
    echo "jq is required to write integration-test evidence." >&2
    exit 1
}

configure_target_selection
validate_full_configuration_matrix

if [[ "${mode}" == "compose" ]]; then
    ensure_docker_compose_available
    configure_compose_overrides
fi

# Evidence must survive a red test run, so capture the status explicitly and
# return it only after the matrix record has been written.
set +e
run_integration_tests
test_exit_code=$?
set -e

write_matrix_evidence "${test_exit_code}"
exit "${test_exit_code}"
