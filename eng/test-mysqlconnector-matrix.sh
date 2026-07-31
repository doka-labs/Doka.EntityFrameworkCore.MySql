#!/usr/bin/env bash

# This gate validates both dependency resolution and runtime behavior for one
# MySqlConnector matrix row. Environment inputs are explicit so CI can exercise
# the supported floor and the latest compatible 2.x version with identical
# contracts and independently retained evidence.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unit_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.Tests/"
unit_project+="Doka.EntityFrameworkCore.MySql.Tests.csproj"
integration_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/"
integration_project+="Doka.EntityFrameworkCore.MySql.IntegrationTests.csproj"
requested_version="${DOKA_MYSQLCONNECTOR_VERSION:?DOKA_MYSQLCONNECTOR_VERSION is required}"
resolved_pattern="${DOKA_MYSQLCONNECTOR_RESOLVED_PATTERN:?DOKA_MYSQLCONNECTOR_RESOLVED_PATTERN is required}"
artifact_suffix="${DOKA_MYSQLCONNECTOR_ARTIFACT_SUFFIX:?DOKA_MYSQLCONNECTOR_ARTIFACT_SUFFIX is required}"
targets="${DOKA_INTEGRATION_TARGETS:-mysql84,mariadb118}"
evidence_dir="${repo_root}/artifacts/mysqlconnector-patch-matrix/${artifact_suffix}"
resolved_packages_file="${evidence_dir}/resolved-packages.json"
database_evidence_file="${evidence_dir}/test-database-evidence.json"
summary_file="${evidence_dir}/driver-contract-evidence.json"

command -v jq >/dev/null 2>&1 || {
    echo "jq is required to verify MySqlConnector matrix evidence." >&2
    exit 1
}

export DokaMySqlConnectorVersion="${requested_version}"
export CentralPackageFloatingVersionsEnabled=true
export DOKA_INTEGRATION_TARGETS="${targets}"
export DOKA_TEST_DATABASE_EVIDENCE_FILE="${database_evidence_file}"

mkdir -p "${evidence_dir}/unit" "${evidence_dir}/live"
cd "${repo_root}"

"${repo_root}/eng/verify-dotnet.sh"
dotnet restore "${unit_project}" --tl:off
dotnet restore "${integration_project}" --tl:off

# Read back the resolved graph instead of treating the requested central
# package expression as proof of the version that NuGet selected.
dotnet package list \
    --project "${integration_project}" \
    --include-transitive \
    --format json \
    --no-restore > "${resolved_packages_file}"

resolved_version="$(jq -er --arg pattern "${resolved_pattern}" '
    [
      .projects[].frameworks[]
      | ((.topLevelPackages // []) + (.transitivePackages // []))[]
      | select(.id == "MySqlConnector")
      | .resolvedVersion
    ]
    | unique
    | select(length == 1 and all(test($pattern)))
    | .[0]
' "${resolved_packages_file}")"

# Unit tests cover driver-facing classification and version logic without a
# server. Live contracts then prove pooling, transactions, faults, and telemetry
# against both supported engine families.
dotnet test "${unit_project}" \
    --configuration Release \
    --no-restore \
    --tl:off \
    --filter "FullyQualifiedName~MySqlExecutionStrategyTests\
|FullyQualifiedName~MySqlTransientExceptionDetectorTests\
|FullyQualifiedName~MySqlServerVersionTests" \
    --logger "trx;LogFileName=driver-contract-unit.trx" \
    --results-directory "${evidence_dir}/unit"

dotnet test "${integration_project}" \
    --configuration Release \
    --no-restore \
    --tl:off \
    --filter "Category=DriverContract" \
    --logger "trx;LogFileName=driver-contract-live.trx" \
    --results-directory "${evidence_dir}/live"

# The matrix row is not complete unless both containers were identified and
# cleaned up. This readback also prevents a green test result from hiding a
# lifecycle-evidence failure.
jq -e \
    --argjson expectedTargets '["mariadb118", "mysql84"]' \
    '.lifecycleState == "cleanup-completed"
      and ([.targets[].targetId] | sort) == $expectedTargets' \
    "${database_evidence_file}" > /dev/null

# Keep the contract list machine-readable so release review can distinguish a
# complete driver row from an arbitrary filtered test run.
jq -n \
    --arg generatedUtc "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
    --arg requestedVersion "${requested_version}" \
    --arg resolvedVersion "${resolved_version}" \
    --arg targets "${targets}" \
    '{
      schemaVersion: 1,
      generatedUtc: $generatedUtc,
      requestedVersion: $requestedVersion,
      resolvedVersion: $resolvedVersion,
      targets: ($targets | split(",") | sort),
      contracts: [
        "pooling-and-recovery",
        "cancellation",
        "command-timeout",
        "commit-unknown-reconciliation",
        "cross-layer-observability",
        "network-fault-recovery",
        "transaction-and-savepoint",
        "retry-and-transient-classification",
        "server-version-detection",
        "telemetry-privacy-and-cardinality"
      ],
      results: {
        unit: "unit/driver-contract-unit.trx",
        live: "live/driver-contract-live.trx",
        database: "test-database-evidence.json",
        dependencies: "resolved-packages.json"
      }
    }' > "${summary_file}"

echo "MySqlConnector ${resolved_version} driver contract passed for ${targets}."
