#!/usr/bin/env bash

# Builds the repository's migration bundle while keeping EF Core's implicit
# RID restore away from repository-owned package lock files.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
migration_project="${repo_root}/examples/MigrationsWorkflow/MigrationsWorkflow.csproj"
migration_context="Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow.MigrationWorkflowContext"
bundle_path="${1:-}"

if [[ -z "${bundle_path}" ]]; then
    echo "Usage: $0 <bundle-output-path>" >&2
    exit 2
fi

bundle_restore_root="$(mktemp -d "${TMPDIR:-/tmp}/doka-migration-bundle.XXXXXX")"
source_status_before="$(git -C "${repo_root}" status --porcelain --untracked-files=all)"

trap 'rm -rf -- "${bundle_restore_root}"' EXIT
mkdir -p "${bundle_restore_root}/locks"

set +e
DokaIsolatedNuGetLockRoot="${bundle_restore_root}/locks" \
    dotnet tool run dotnet-ef -- migrations bundle \
        --project "${migration_project}" \
        --startup-project "${migration_project}" \
        --context "${migration_context}" \
        --configuration Release \
        --no-build \
        --no-color \
        --force \
        --output "${bundle_path}"
bundle_exit_code=$?
set -e

source_status_after="$(git -C "${repo_root}" status --porcelain --untracked-files=all)"
if [[ "${source_status_after}" != "${source_status_before}" ]]; then
    echo "Migration bundle generation changed the source tree." >&2
    echo "Before:" >&2
    printf '%s\n' "${source_status_before:-<clean>}" >&2
    echo "After:" >&2
    printf '%s\n' "${source_status_after:-<clean>}" >&2
    exit 1
fi

exit "${bundle_exit_code}"
