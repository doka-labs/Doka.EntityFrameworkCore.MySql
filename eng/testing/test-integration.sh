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

resolve_repo_path() {
    local configured_path="$1"

    if [[ "${configured_path}" == /* ]]; then
        printf '%s\n' "${configured_path}"
        return
    fi

    # VSTest changes the test host's working directory to its build output.
    # Passing an absolute path keeps evidence in the repository artifact tree
    # even when an operator configures a convenient relative output path.
    printf '%s/%s\n' "${repo_root}" "${configured_path}"
}

# Release orchestration overrides this root so lifecycle identity and cleanup
# evidence are hashed inside the same immutable candidate package.
integration_artifacts_dir="$(
    resolve_repo_path "${DOKA_INTEGRATION_ARTIFACTS_DIR:-artifacts/integration/${integration_run_id}}"
)"
integration_summary_file="${integration_artifacts_dir}/compatibility-matrix-summary.md"
integration_evidence_file="${integration_artifacts_dir}/compatibility-matrix-evidence.json"
database_evidence_file="$(
    resolve_repo_path "${DOKA_TEST_DATABASE_EVIDENCE_FILE:-${integration_artifacts_dir}/test-database-evidence.json}"
)"
integration_targets_var="DOKA_INTEGRATION_TARGETS"

mysql80_target_id="mysql80"
mysql84_target_id="mysql84"
mysql97_target_id="mysql97"
mariadb1011_target_id="mariadb1011"
mariadb114_target_id="mariadb114"
mariadb118_target_id="mariadb118"
mariadb123_target_id="mariadb123"

# Only the legacy MySQL 8.0 target names its connection-string variable in a
# diagnostic; the supported targets resolve theirs through the test host.
mysql80_env_var="DOKA_MYSQL80_CONNECTION_STRING"

mode="testcontainers"
should_stop_compose_on_exit=0
configured_target_selection="${DOKA_INTEGRATION_TARGETS:-mysql84,mysql97,mariadb1011,mariadb114,mariadb118,mariadb123}"
integration_test_filter="${DOKA_INTEGRATION_TEST_FILTER:-}"
require_full_configuration_matrix="${DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX:-0}"
target_selection_label=""
selected_targets=()
mysql80_target_enabled=0
mysql84_target_enabled=0
mysql97_target_enabled=0
mariadb1011_target_enabled=0
mariadb114_target_enabled=0
mariadb118_target_enabled=0
mariadb123_target_enabled=0

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
  DOKA_INTEGRATION_TARGETS=mysql84,mysql97,mariadb1011,mariadb114,mariadb118,mariadb123
  DOKA_INTEGRATION_ARTIFACTS_DIR=<evidence output directory>
  DOKA_INTEGRATION_TEST_FILTER=<optional dotnet test filter>
  DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX=0|1
  DOKA_MYSQL84_CONNECTION_STRING=<external override>
  DOKA_MYSQL97_CONNECTION_STRING=<external override>
  DOKA_MARIADB1011_CONNECTION_STRING=<external override>
  DOKA_MARIADB114_CONNECTION_STRING=<external override>
  DOKA_MARIADB118_CONNECTION_STRING=<external override>
  DOKA_MARIADB123_CONNECTION_STRING=<external override>

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
            "${mysql97_target_id}")
                if [[ "${mysql97_target_enabled}" -eq 1 ]]; then
                    echo "Duplicate integration target '${normalized_target}'." >&2
                    exit 1
                fi
                mysql97_target_enabled=1
                ;;
            "${mariadb1011_target_id}")
                if [[ "${mariadb1011_target_enabled}" -eq 1 ]]; then
                    echo "Duplicate integration target '${normalized_target}'." >&2
                    exit 1
                fi
                mariadb1011_target_enabled=1
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
            "${mariadb123_target_id}")
                if [[ "${mariadb123_target_enabled}" -eq 1 ]]; then
                    echo "Duplicate integration target '${normalized_target}'." >&2
                    exit 1
                fi
                mariadb123_target_enabled=1
                ;;
            *)
                echo "Unsupported integration target '${normalized_target}' in ${integration_targets_var}." >&2
                echo "Accepted values are: mysql80, mysql84, mysql97, mariadb1011, mariadb114, mariadb118, mariadb123." >&2
                exit 1
                ;;
        esac

        if [[ -z "${target_selection_label}" ]]; then
            target_selection_label="${normalized_target}"
        else
            target_selection_label="${target_selection_label},${normalized_target}"
        fi

        selected_targets+=("${normalized_target}")
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
        || "${mysql97_target_enabled}" -ne 1 \
        || "${mariadb1011_target_enabled}" -ne 1 \
        || "${mariadb114_target_enabled}" -ne 1 \
        || "${mariadb118_target_enabled}" -ne 1 \
        || "${mariadb123_target_enabled}" -ne 1 \
        || "${mysql80_target_enabled}" -ne 0 ]]; then
        echo "The full configuration matrix requires every active LTS target and excludes mysql80." >&2
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
    local mysql97_port="${DOKA_MYSQL97_PORT:-33070}"
    local mariadb1011_port="${DOKA_MARIADB1011_PORT:-33066}"
    local mariadb114_port="${DOKA_MARIADB114_PORT:-33067}"
    local mariadb118_port="${DOKA_MARIADB118_PORT:-33069}"
    local mariadb123_port="${DOKA_MARIADB123_PORT:-33071}"

    if [[ "${mysql80_target_enabled}" -eq 1 && -z "${DOKA_MYSQL80_CONNECTION_STRING:-}" ]]; then
        echo "MySQL 8.0 has no bundled Compose service because it is outside the supported matrix." >&2
        echo "Set ${mysql80_env_var} to run its legacy tests explicitly." >&2
        exit 1
    fi

    if [[ "${mysql84_target_enabled}" -eq 1 && -z "${DOKA_MYSQL84_CONNECTION_STRING:-}" ]]; then
        compose_services+=("mysql84")
        export DOKA_MYSQL84_CONNECTION_STRING="Server=127.0.0.1;Port=${mysql84_port};Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;"
    fi

    if [[ "${mysql97_target_enabled}" -eq 1 && -z "${DOKA_MYSQL97_CONNECTION_STRING:-}" ]]; then
        compose_services+=("mysql97")
        export DOKA_MYSQL97_CONNECTION_STRING="Server=127.0.0.1;Port=${mysql97_port};Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;"
    fi

    if [[ "${mariadb1011_target_enabled}" -eq 1 && -z "${DOKA_MARIADB1011_CONNECTION_STRING:-}" ]]; then
        compose_services+=("mariadb1011")
        export DOKA_MARIADB1011_CONNECTION_STRING="Server=127.0.0.1;Port=${mariadb1011_port};Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;"
    fi

    if [[ "${mariadb114_target_enabled}" -eq 1 && -z "${DOKA_MARIADB114_CONNECTION_STRING:-}" ]]; then
        compose_services+=("mariadb114")
        export DOKA_MARIADB114_CONNECTION_STRING="Server=127.0.0.1;Port=${mariadb114_port};Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;"
    fi

    if [[ "${mariadb118_target_enabled}" -eq 1 && -z "${DOKA_MARIADB118_CONNECTION_STRING:-}" ]]; then
        compose_services+=("mariadb118")
        export DOKA_MARIADB118_CONNECTION_STRING="Server=127.0.0.1;Port=${mariadb118_port};Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;"
    fi

    if [[ "${mariadb123_target_enabled}" -eq 1 && -z "${DOKA_MARIADB123_CONNECTION_STRING:-}" ]]; then
        compose_services+=("mariadb123")
        export DOKA_MARIADB123_CONNECTION_STRING="Server=127.0.0.1;Port=${mariadb123_port};Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;"
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
    local coverage_results_dir
    local target_evidence_dir="${integration_artifacts_dir}/database-targets"
    local matrix_exit_code=0
    local target
    local target_exit_code
    local target_evidence_file
    local target_results_dir
    local target_evidence_files=()

    coverage_results_dir="$(
        resolve_repo_path "${DOKA_COVERAGE_RESULTS_DIR:-artifacts/coverage/integration}"
    )"

    mkdir -p \
        "${coverage_results_dir}" \
        "${integration_artifacts_dir}" \
        "${target_evidence_dir}" \
        "$(dirname "${database_evidence_file}")"

    "${repo_root}/eng/common/verify-dotnet.sh" || return $?
    dotnet restore "${integration_test_project}" --tl:off || return $?
    dotnet build \
        "${integration_test_project}" \
        --configuration Release \
        --no-restore \
        --tl:off || return $?

    # EF Core caches internal service providers process-wide. Server profiles
    # legitimately differ between LTS targets, so one process per target keeps
    # those singleton contracts isolated while preserving one operator command.
    for target in "${selected_targets[@]}"; do
        target_evidence_file="${target_evidence_dir}/${target}/test-database-evidence.json"
        target_results_dir="${coverage_results_dir}/${target}"
        target_evidence_files+=("${target_evidence_file}")

        mkdir -p "$(dirname "${target_evidence_file}")" "${target_results_dir}"

        local test_arguments=(
            --configuration Release
            --no-build
            --no-restore
            --tl:off
            --collect:"XPlat Code Coverage"
            --results-directory "${target_results_dir}"
            --logger trx
        )

        if [[ -n "${integration_test_filter}" ]]; then
            test_arguments+=(--filter "${integration_test_filter}")
        fi

        echo "Running isolated integration process for ${target}..."
        DOKA_INTEGRATION_TARGETS="${target}" \
        DOKA_TEST_DATABASE_EVIDENCE_FILE="${target_evidence_file}" \
            dotnet test "${integration_test_project}" "${test_arguments[@]}"
        target_exit_code=$?

        if [[ "${target_exit_code}" -ne 0 ]]; then
            matrix_exit_code="${target_exit_code}"
        fi
    done

    merge_database_evidence "${target_evidence_files[@]}" || matrix_exit_code=1

    return "${matrix_exit_code}"
}

merge_database_evidence() {
    local evidence_files=("$@")
    local evidence_file

    for evidence_file in "${evidence_files[@]}"; do
        if [[ ! -f "${evidence_file}" ]]; then
            echo "Missing target database evidence: ${evidence_file}" >&2
            return 1
        fi
    done

    jq -s \
        '{
            schemaVersion: 1,
            generatedUtc: (map(.generatedUtc) | max),
            lifecycleState: (
                if all(.[]; .lifecycleState == "cleanup-completed")
                then "cleanup-completed"
                else "cleanup-incomplete"
                end
            ),
            targets: ([.[].targets[]] | sort_by(.targetId))
        }' \
        "${evidence_files[@]}" > "${database_evidence_file}"

    jq -e \
        --argjson expectedTargetCount "${#selected_targets[@]}" \
        '.lifecycleState == "cleanup-completed"
         and (.targets | length) == $expectedTargetCount
         and ([.targets[].targetId] | unique | length) == $expectedTargetCount' \
        "${database_evidence_file}" >/dev/null
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
        echo "- databaseEvidence: ${database_evidence_file}"
        echo
        echo "The test database evidence records exact images, dynamic endpoints, ownership source, and cleanup state."
    } > "${integration_summary_file}"

    if [[ -f "${database_evidence_file}" ]]; then
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
            "${database_evidence_file}" > "${integration_evidence_file}"
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
