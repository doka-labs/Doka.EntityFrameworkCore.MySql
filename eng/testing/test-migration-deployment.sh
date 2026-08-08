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
repo_fingerprint="$(printf '%s' "${repo_root}" | cksum | awk '{print $1}')"
compose_run_id="$(printf '%s' "${run_id}" | tr '[:upper:]' '[:lower:]')"
compose_project_name="${DOKA_MIGRATION_COMPOSE_PROJECT_NAME:-doka-migration-${repo_fingerprint}-${compose_run_id}}"
compose_command=(docker compose -p "${compose_project_name}" -f "${compose_file}")
should_stop_compose=0

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

write_evidence() {
    # This function is reached only after all target lifecycles complete. The
    # release manifest hashes both forms only after the cleanup trap succeeds.
    cat > "${summary_file}" <<EOF
# Migration deployment summary

- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
- runId: ${run_id}
- modelSnapshot: pass
- bundleGeneration: pass
- mysql84Lifecycle: pass
- mariadb114Lifecycle: pass
- mariadb118Lifecycle: pass

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
    "mariadb114": "pass",
    "mariadb118": "pass"
  }
}
EOF
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
export DOKA_MARIADB114_PORT="${DOKA_MARIADB114_PORT:-0}"
export DOKA_MARIADB118_PORT="${DOKA_MARIADB118_PORT:-0}"

should_stop_compose=1
"${compose_command[@]}" up \
    -d \
    --wait \
    --wait-timeout 120 \
    mysql84 \
    mariadb114 \
    mariadb118

run_bundle_lifecycle "mysql84" "$(published_port mysql84)" "mysql:8.4"
run_bundle_lifecycle "mariadb114" "$(published_port mariadb114)" "mariadb:11.4"
run_bundle_lifecycle "mariadb118" "$(published_port mariadb118)" "mariadb:11.8"

write_evidence

echo "Migration deployment gate passed."
echo "Evidence: ${evidence_file}"
