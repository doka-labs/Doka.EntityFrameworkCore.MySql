#!/usr/bin/env bash

# Builds an EF Core migration bundle and proves apply, idempotent reapply,
# rollback-to-zero, and recovery across the complete supported engine matrix.
# Release orchestration retains the evidence only when lifecycle and cleanup
# both return successfully.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
compose_file="${repo_root}/docker/compose.yml"
migration_project="${repo_root}/examples/MigrationsWorkflow/MigrationsWorkflow.csproj"
migration_context="Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow.MigrationWorkflowContext"
migration_executable="${repo_root}/artifacts/bin/MigrationsWorkflow/release/MigrationsWorkflow.dll"
run_id="${DOKA_MIGRATION_DEPLOYMENT_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
evidence_root="${DOKA_MIGRATION_DEPLOYMENT_EVIDENCE_ROOT:-${repo_root}/artifacts/migration-deployment}"
evidence_dir="${evidence_root}/${run_id}"
bundle_path="${evidence_dir}/efbundle"
summary_file="${evidence_dir}/migration-deployment-summary.md"
evidence_file="${evidence_dir}/migration-deployment-evidence.json"
database_evidence_file="${evidence_dir}/test-database-evidence.json"
repo_fingerprint="$(printf '%s' "${repo_root}" | cksum | awk '{print $1}')"
compose_run_id="$(printf '%s' "${run_id}" | tr '[:upper:]' '[:lower:]')"
compose_project_name="${DOKA_MIGRATION_COMPOSE_PROJECT_NAME:-doka-migration-${repo_fingerprint}-${compose_run_id}}"
compose_command=(docker compose -p "${compose_project_name}" -f "${compose_file}")
should_stop_compose=0
database_evidence_written=0

cleanup() {
    local exit_code="$1"

    if [[ "${should_stop_compose}" -eq 1 ]]; then
        set +e
        echo "Removing migration-deployment stack '${compose_project_name}' and its volumes..."
        "${compose_command[@]}" down --volumes --remove-orphans
        local down_exit_code=$?
        set -e

        # A leaked migration stack invalidates an otherwise green owned run.
        # Preserve an earlier workflow failure when both operations fail.
        if [[ "${exit_code}" -eq 0 && "${down_exit_code}" -ne 0 ]]; then
            exit_code="${down_exit_code}"
        fi

        if [[ "${down_exit_code}" -eq 0 && "${database_evidence_written}" -eq 1 ]]; then
            set +e
            finalize_database_evidence
            local evidence_exit_code=$?
            set -e

            if [[ "${exit_code}" -eq 0 && "${evidence_exit_code}" -ne 0 ]]; then
                exit_code="${evidence_exit_code}"
            fi
        fi
    fi

    exit "${exit_code}"
}

trap 'cleanup "$?"' EXIT

require_docker_compose() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "docker is required for the migration-deployment gate." >&2
        exit 1
    fi

    if ! docker compose version >/dev/null 2>&1; then
        echo "docker compose is required for the migration-deployment gate." >&2
        exit 1
    fi
}

published_port() {
    local service_name="$1"
    local port_output

    port_output="$("${compose_command[@]}" port "${service_name}" 3306)"
    echo "${port_output##*:}"
}

run_workflow_command() {
    local connection_string="$1"
    local server_version="$2"
    local command="$3"

    DOKA_MIGRATION_CONNECTION_STRING="${connection_string}" \
    DOKA_MIGRATION_SERVER_VERSION="${server_version}" \
        dotnet "${migration_executable}" "${command}"
}

run_bundle_command() {
    local connection_string="$1"
    local server_version="$2"
    shift 2

    DOKA_MIGRATION_SERVER_VERSION="${server_version}" \
        "${bundle_path}" "$@" --connection "${connection_string}"
}

run_bundle_lifecycle() {
    local target_id="$1"
    local port="$2"
    local server_version="$3"
    local connection_string

    # These credentials belong only to the isolated repository Compose stack;
    # no externally supplied database is mutated by this deployment gate.
    connection_string="Server=127.0.0.1;Port=${port};Database=doka_provider;"
    connection_string+="User ID=root;Password=root_password;Persist Security Info=True;"

    echo "Applying migration bundle to ${target_id}..."
    run_bundle_command "${connection_string}" "${server_version}"

    echo "Reapplying migration bundle to ${target_id}..."
    run_bundle_command "${connection_string}" "${server_version}"
    run_workflow_command "${connection_string}" "${server_version}" "verify-latest"

    echo "Rolling migration bundle back to zero on ${target_id}..."
    run_bundle_command "${connection_string}" "${server_version}" 0
    run_workflow_command "${connection_string}" "${server_version}" "verify-rolled-back"

    echo "Restoring the latest migration on ${target_id}..."
    run_bundle_command "${connection_string}" "${server_version}"
    run_workflow_command "${connection_string}" "${server_version}" "verify-latest"
}

write_target_identity() {
    local target_id="$1"
    local engine="$2"
    local server_version="$3"
    local container_id
    local image

    container_id="$("${compose_command[@]}" ps -q "${target_id}")"
    if [[ -z "${container_id}" ]]; then
        echo "No running container identity was found for ${target_id}." >&2
        return 1
    fi

    image="$(docker inspect --format '{{.Config.Image}}' "${container_id}")"
    if [[ "${image}" != *@sha256:* ]]; then
        echo "Migration target ${target_id} did not run a digest-pinned image." >&2
        return 1
    fi

    jq -n \
        --arg targetId "${target_id}" \
        --arg engine "${engine}" \
        --arg serverVersionToken "${server_version}" \
        --arg image "${image}" \
        --arg containerId "${container_id}" \
        '{
          targetId: $targetId,
          engine: $engine,
          serverVersionToken: $serverVersionToken,
          source: "compose",
          image: $image,
          containerId: $containerId
        }'
}

finalize_database_evidence() {
    local temporary_file="${database_evidence_file}.tmp"

    jq '.lifecycleState = "cleanup-completed"' \
        "${database_evidence_file}" > "${temporary_file}"
    mv "${temporary_file}" "${database_evidence_file}"
}

write_evidence() {
    local target_identities_file="${database_evidence_file}.targets.tmp"

    # This function is reached only after all target lifecycles complete. The
    # lifecycle remains pending until the cleanup trap removes every owned
    # container and volume; only then can release assembly consume it.
    cat > "${summary_file}" <<EOF
# Migration deployment summary

- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
- runId: ${run_id}
- modelSnapshot: pass
- bundleGeneration: pass
- mysql84Lifecycle: pass
- mysql97Lifecycle: pass
- mariadb1011Lifecycle: pass
- mariadb114Lifecycle: pass
- mariadb118Lifecycle: pass
- mariadb123Lifecycle: pass

The lifecycle applies, reapplies, rolls back to zero, reapplies, and reads back
the checked-in migration through the generated EF Core migration bundle.
EOF

    cat > "${evidence_file}" <<EOF
{
  "generatedUtc": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "runId": "${run_id}",
  "modelSnapshot": "pass",
  "bundleGeneration": "pass",
  "targets": {
    "mysql84": "pass",
    "mysql97": "pass",
    "mariadb1011": "pass",
    "mariadb114": "pass",
    "mariadb118": "pass",
    "mariadb123": "pass"
  }
}
EOF

    : > "${target_identities_file}"
    {
        write_target_identity "mysql84" "MySql" "mysql:8.4"
        write_target_identity "mysql97" "MySql" "mysql:9.7"
        write_target_identity "mariadb1011" "MariaDb" "mariadb:10.11"
        write_target_identity "mariadb114" "MariaDb" "mariadb:11.4"
        write_target_identity "mariadb118" "MariaDb" "mariadb:11.8"
        write_target_identity "mariadb123" "MariaDb" "mariadb:12.3"
    } >> "${target_identities_file}"

    jq -s \
        --arg generatedUtc "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
        '{
          schemaVersion: 1,
          generatedUtc: $generatedUtc,
          lifecycleState: "cleanup-pending",
          targets: .
        }' \
        "${target_identities_file}" > "${database_evidence_file}"
    rm -f "${target_identities_file}"
    database_evidence_written=1
}

require_docker_compose
mkdir -p "${evidence_dir}"

bash "${repo_root}/eng/quality/check-migration-model.sh"

dotnet tool run dotnet-ef -- migrations bundle \
    --project "${migration_project}" \
    --startup-project "${migration_project}" \
    --context "${migration_context}" \
    --configuration Release \
    --no-build \
    --no-color \
    --force \
    --output "${bundle_path}"

export DOKA_MYSQL84_PORT="${DOKA_MYSQL84_PORT:-0}"
export DOKA_MYSQL97_PORT="${DOKA_MYSQL97_PORT:-0}"
export DOKA_MARIADB1011_PORT="${DOKA_MARIADB1011_PORT:-0}"
export DOKA_MARIADB114_PORT="${DOKA_MARIADB114_PORT:-0}"
export DOKA_MARIADB118_PORT="${DOKA_MARIADB118_PORT:-0}"
export DOKA_MARIADB123_PORT="${DOKA_MARIADB123_PORT:-0}"

should_stop_compose=1
"${compose_command[@]}" up \
    -d \
    --wait \
    --wait-timeout 120 \
    mysql84 \
    mysql97 \
    mariadb1011 \
    mariadb114 \
    mariadb118 \
    mariadb123

run_bundle_lifecycle "mysql84" "$(published_port mysql84)" "mysql:8.4"
run_bundle_lifecycle "mysql97" "$(published_port mysql97)" "mysql:9.7"
run_bundle_lifecycle "mariadb1011" "$(published_port mariadb1011)" "mariadb:10.11"
run_bundle_lifecycle "mariadb114" "$(published_port mariadb114)" "mariadb:11.4"
run_bundle_lifecycle "mariadb118" "$(published_port mariadb118)" "mariadb:11.8"
run_bundle_lifecycle "mariadb123" "$(published_port mariadb123)" "mariadb:12.3"

write_evidence

echo "Migration deployment gate passed."
echo "Evidence: ${evidence_file}"
