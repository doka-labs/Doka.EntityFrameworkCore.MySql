#!/usr/bin/env bash

# Fails when the executable migration example and its checked-in model snapshot
# no longer describe the same EF Core model.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
migration_project="${repo_root}/examples/MigrationsWorkflow/MigrationsWorkflow.csproj"
migration_context="Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow.MigrationWorkflowContext"

"${repo_root}/eng/verify-dotnet.sh"
dotnet tool restore
dotnet restore "${migration_project}" --tl:off
dotnet build \
    "${migration_project}" \
    --configuration Release \
    --no-restore \
    --tl:off \
    -m:1
dotnet tool run dotnet-ef -- migrations has-pending-model-changes \
    --project "${migration_project}" \
    --startup-project "${migration_project}" \
    --context "${migration_context}" \
    --configuration Release \
    --no-build \
    --no-color

echo "Migration model matches the checked-in snapshot."
