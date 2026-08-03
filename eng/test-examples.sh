#!/usr/bin/env bash

# Executes every database-backed example against the supported engine matrix.
# The runner owns an isolated Compose project, uses dynamically assigned host
# ports, records every example result, and removes its containers and volumes.

set -euo pipefail

export DOTNET_CLI_USE_MSBUILD_SERVER=0

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="${repo_root}/docker/compose.yml"
repo_fingerprint="$(printf '%s' "${repo_root}" | cksum | awk '{print $1}')"
run_id="${DOKA_EXAMPLE_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
compose_project_name="doka-examples-${repo_fingerprint}-$$"
evidence_dir="${DOKA_EXAMPLE_EVIDENCE_DIR:-${repo_root}/artifacts/examples/${run_id}}"
evidence_file="${evidence_dir}/live-example-matrix-evidence.json"
summary_file="${evidence_dir}/live-example-matrix-summary.md"
results_file="${evidence_dir}/results.tsv"
engines_file="${evidence_dir}/engines.tsv"
compose_command=(docker compose -p "${compose_project_name}" -f "${compose_file}")

example_projects=(
    "BulkOperations"
    "CharSetAndCollation"
    "CrudOperations"
    "DockerIntegration"
    "GeneratedColumns"
    "GettingStarted"
    "GuidFormats"
    "InheritancePatterns"
    "JsonColumns"
    "MultiTenancy"
    "PerformanceBestPractices"
    "Relationships"
    "SpatialQueries"
)
sentinel_database="doka_example_sentinel"

selected_targets=()
matrix_exit_code=0
cleanup_exit_code=0
cleanup_completed=false
cleanup_required=false

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/test-examples.sh

Environment:
  DOKA_EXAMPLE_TARGETS=mysql84,mariadb114,mariadb118
  DOKA_EXAMPLE_EVIDENCE_DIR=<evidence output directory>
  DOKA_EXAMPLE_RUN_ID=<stable run identity>

The default exercises all thirteen live-matrix examples against the complete
supported engine matrix. A subset is useful for local diagnosis; release
qualification always supplies the complete matrix explicitly.
EOF
}

require_command() {
    local command_name="$1"

    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Required command '${command_name}' is not available." >&2
        exit 1
    fi
}

configure_targets() {
    # Normalize the operator input once and reject duplicates before Docker
    # resources are created.
    local configured_targets="${DOKA_EXAMPLE_TARGETS:-mysql84,mariadb114,mariadb118}"
    local normalized_target
    local existing_target

    IFS=',' read -r -a raw_targets <<< "${configured_targets}"
    for raw_target in "${raw_targets[@]}"; do
        normalized_target="$(
            printf '%s' "${raw_target}" \
                | tr '[:upper:]' '[:lower:]' \
                | tr -d '[:space:]'
        )"

        if [[ -z "${normalized_target}" ]]; then
            continue
        fi

        case "${normalized_target}" in
            mysql84|mariadb114|mariadb118)
                ;;
            *)
                echo "Unsupported example target '${normalized_target}'." >&2
                echo "Accepted targets are mysql84, mariadb114, and mariadb118." >&2
                exit 1
                ;;
        esac

        for existing_target in "${selected_targets[@]:-}"; do
            if [[ "${existing_target}" == "${normalized_target}" ]]; then
                echo "Duplicate example target '${normalized_target}'." >&2
                exit 1
            fi
        done

        selected_targets+=("${normalized_target}")
    done

    if [[ "${#selected_targets[@]}" -eq 0 ]]; then
        echo "DOKA_EXAMPLE_TARGETS must select at least one supported target." >&2
        exit 1
    fi
}

capture_engine_identity() {
    # Retain both the pinned reference and platform-specific local image ID so
    # release evidence can bind the exact engine bytes used by this runner.
    local target="$1"
    local container_id
    local endpoint
    local image_reference
    local image_id

    container_id="$("${compose_command[@]}" ps -q "${target}")"
    endpoint="$("${compose_command[@]}" port "${target}" 3306)"
    image_reference="$(docker inspect --format '{{.Config.Image}}' "${container_id}")"
    image_id="$(docker inspect --format '{{.Image}}' "${container_id}")"

    if [[ -z "${container_id}" || -z "${endpoint}" || -z "${image_id}" ]]; then
        echo "Unable to resolve the live identity for ${target}." >&2
        return 1
    fi

    printf '%s\t%s\t%s\t%s\n' \
        "${target}" \
        "${endpoint}" \
        "${image_reference}" \
        "${image_id}" >> "${engines_file}"
}

build_examples() {
    # Build once per project before the matrix so runtime failures cannot be
    # confused with target-specific compilation behavior.
    local example_name
    local project

    for example_name in "${example_projects[@]}"; do
        project="${repo_root}/examples/${example_name}/${example_name}.csproj"
        echo "Building example ${example_name}..."
        dotnet restore "${project}" --tl:off || return $?
        dotnet build "${project}" \
            --configuration Release \
            --no-restore \
            --tl:off \
            -m:1 || return $?
    done
}

database_client() {
    local target="$1"

    case "${target}" in
        mysql84)
            printf '%s\n' "mysql"
            ;;
        mariadb114|mariadb118)
            printf '%s\n' "mariadb"
            ;;
        *)
            echo "Unsupported example target '${target}'." >&2
            return 1
            ;;
    esac
}

initialize_sentinel_catalog() {
    # The environment connection string intentionally names a non-example
    # catalog. A runnable example may reuse its endpoint and credentials, but
    # must replace this catalog before any EnsureDeleted call can execute.
    local target="$1"
    local container_id
    local client

    container_id="$("${compose_command[@]}" ps -q "${target}")"
    client="$(database_client "${target}")"

    docker exec \
        --env "MYSQL_PWD=root_password" \
        "${container_id}" \
        "${client}" \
        --user=root \
        --execute="
            CREATE DATABASE IF NOT EXISTS \`${sentinel_database}\`;
            CREATE TABLE IF NOT EXISTS \`${sentinel_database}\`.Sentinel (Id INT PRIMARY KEY);
            DELETE FROM \`${sentinel_database}\`.Sentinel;
            INSERT INTO \`${sentinel_database}\`.Sentinel (Id) VALUES (1);
        "
}

verify_sentinel_catalog() {
    # Checking the marker, rather than only catalog existence, also catches an
    # example that drops and silently recreates the caller-selected database.
    local target="$1"
    local container_id
    local client
    local marker_count

    container_id="$("${compose_command[@]}" ps -q "${target}")"
    client="$(database_client "${target}")"
    marker_count="$(
        docker exec \
            --env "MYSQL_PWD=root_password" \
            "${container_id}" \
            "${client}" \
            --batch \
            --skip-column-names \
            --user=root \
            --execute="SELECT COUNT(*) FROM \`${sentinel_database}\`.Sentinel WHERE Id = 1;"
    )"

    if [[ "${marker_count}" != "1" ]]; then
        echo "${target}: example modified the protected sentinel catalog." >&2
        return 1
    fi
}

run_example_matrix() {
    # Execute every selected target even after one example fails. The complete
    # result inventory makes a red release run actionable in one pass.
    local target
    local endpoint
    local port
    local connection_string
    local example_name
    local project
    local example_exit_code

    # Port zero asks Docker for a free loopback port. This prevents the live
    # example gate from colliding with developer databases or parallel jobs.
    export DOKA_MYSQL84_PORT=0
    export DOKA_MARIADB114_PORT=0
    export DOKA_MARIADB118_PORT=0

    echo "Starting isolated live-example stack '${compose_project_name}'..."
    cleanup_required=true
    "${compose_command[@]}" up \
        -d \
        --wait \
        --wait-timeout 120 \
        "${selected_targets[@]}" || return $?

    for target in "${selected_targets[@]}"; do
        capture_engine_identity "${target}" || return $?
    done

    build_examples || return $?

    for target in "${selected_targets[@]}"; do
        endpoint="$("${compose_command[@]}" port "${target}" 3306)"
        port="${endpoint##*:}"
        connection_string="Server=127.0.0.1;Port=${port};Database=${sentinel_database};User ID=root;Password=root_password;"
        initialize_sentinel_catalog "${target}" || return $?

        for example_name in "${example_projects[@]}"; do
            project="${repo_root}/examples/${example_name}/${example_name}.csproj"
            echo "Running ${example_name} against ${target}..."

            set +e
            DOKA_EXAMPLE_DATABASE_TARGET="${target}" \
            DOKA_EXAMPLE_CONNECTION_STRING="${connection_string}" \
                dotnet run \
                    --project "${project}" \
                    --configuration Release \
                    --no-build \
                    --no-restore
            example_exit_code=$?
            set -e

            if ! verify_sentinel_catalog "${target}"; then
                example_exit_code=1
            fi

            printf '%s\t%s\t%s\n' \
                "${target}" \
                "${example_name}" \
                "${example_exit_code}" >> "${results_file}"

            if [[ "${example_exit_code}" -ne 0 ]]; then
                matrix_exit_code=1
            fi
        done
    done
}

cleanup_stack() {
    # Volumes are test-owned as well as containers; preserving them would leak
    # state into a later supposedly isolated release qualification.
    echo "Removing live-example stack '${compose_project_name}' and its volumes..."
    set +e
    "${compose_command[@]}" down --volumes --remove-orphans
    cleanup_exit_code=$?
    set -e

    if [[ "${cleanup_exit_code}" -eq 0 ]]; then
        cleanup_completed=true
        cleanup_required=false
    else
        matrix_exit_code=1
    fi
}

cleanup_on_exit() {
    if [[ "${cleanup_required}" != true ]]; then
        return
    fi

    # Signals and unexpected shell failures bypass the normal evidence path.
    # The EXIT trap still removes anything the isolated Compose project owns.
    set +e
    echo "Emergency cleanup for live-example stack '${compose_project_name}'..." >&2
    "${compose_command[@]}" down --volumes --remove-orphans
}

write_evidence() {
    # Convert the append-only TSV receipts only after cleanup so the canonical
    # JSON records both the workload result and the resource lifecycle result.
    local completed_count
    local expected_count
    local passed_count
    local failed_count
    local results_json="${evidence_dir}/results.json"
    local engines_json="${evidence_dir}/engines.json"

    completed_count="$(wc -l < "${results_file}" | tr -d ' ')"
    expected_count=$(( ${#selected_targets[@]} * ${#example_projects[@]} ))
    passed_count="$(awk -F '\t' '$3 == 0 { count++ } END { print count + 0 }' "${results_file}")"
    failed_count=$(( completed_count - passed_count ))

    jq -Rn \
        '[inputs | split("\t") | {
            target: .[0],
            example: .[1],
            exitCode: (.[2] | tonumber),
            status: (if (.[2] | tonumber) == 0 then "pass" else "fail" end)
        }]' < "${results_file}" > "${results_json}"

    jq -Rn \
        '[inputs | split("\t") | {
            target: .[0],
            endpoint: .[1],
            imageReference: .[2],
            imageId: .[3]
        }]' < "${engines_file}" > "${engines_json}"

    jq -n \
        --arg generatedUtc "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
        --arg runId "${run_id}" \
        --arg composeProject "${compose_project_name}" \
        --argjson expectedCount "${expected_count}" \
        --argjson completedCount "${completed_count}" \
        --argjson passedCount "${passed_count}" \
        --argjson failedCount "${failed_count}" \
        --argjson matrixExitCode "${matrix_exit_code}" \
        --argjson cleanupExitCode "${cleanup_exit_code}" \
        --argjson cleanupCompleted "${cleanup_completed}" \
        --slurpfile engines "${engines_json}" \
        --slurpfile results "${results_json}" \
        '{
            schemaVersion: 1,
            generatedUtc: $generatedUtc,
            runId: $runId,
            composeProject: $composeProject,
            expectedCount: $expectedCount,
            completedCount: $completedCount,
            passedCount: $passedCount,
            failedCount: $failedCount,
            matrixExitCode: $matrixExitCode,
            cleanup: {
                completed: $cleanupCompleted,
                exitCode: $cleanupExitCode,
                volumesRemoved: $cleanupCompleted
            },
            engines: $engines[0],
            results: $results[0]
        }' > "${evidence_file}"

    {
        echo "# Live example matrix summary"
        echo
        echo "- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
        echo "- runId: ${run_id}"
        echo "- expectedRuns: ${expected_count}"
        echo "- completedRuns: ${completed_count}"
        echo "- passedRuns: ${passed_count}"
        echo "- failedRuns: ${failed_count}"
        echo "- cleanupCompleted: ${cleanup_completed}"
        echo
        echo "Each example owns its database and verifies a scenario-specific invariant."
    } > "${summary_file}"
}

if (( $# > 1 )); then
    print_usage >&2
    exit 1
fi

if (( $# == 1 )); then
    case "$1" in
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

require_command docker
require_command dotnet
require_command jq

if ! docker compose version >/dev/null 2>&1; then
    echo "docker compose is required for the live example matrix." >&2
    exit 1
fi

configure_targets
mkdir -p "${evidence_dir}"
: > "${results_file}"
: > "${engines_file}"
trap cleanup_on_exit EXIT

# Capture all executed cases before returning failure so a red run remains
# diagnosable. Infrastructure failures still produce a partial evidence file.
set +e
run_example_matrix
run_exit_code=$?
set -e
if [[ "${run_exit_code}" -ne 0 ]]; then
    matrix_exit_code="${run_exit_code}"
fi

cleanup_stack
write_evidence

if [[ "${matrix_exit_code}" -ne 0 ]]; then
    echo "Live example matrix failed; see ${evidence_file}." >&2
    exit "${matrix_exit_code}"
fi

echo "Live example matrix passed; evidence: ${evidence_file}"
