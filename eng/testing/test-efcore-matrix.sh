#!/usr/bin/env bash

# This gate validates dependency resolution and provider behavior for one EF
# Core patch matrix row. Environment inputs are explicit so scheduled CI and
# release qualification can exercise the supported floor and the latest
# compatible patch through one implementation with identical contracts and
# independently retained evidence.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
solution="${repo_root}/Doka.EntityFrameworkCore.MySql.slnx"
functional_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/"
functional_project+="Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
requested_version="${DokaEfCoreVersion:?DokaEfCoreVersion is required}"
resolved_pattern="${DOKA_EF_CORE_RESOLVED_PATTERN:?DOKA_EF_CORE_RESOLVED_PATTERN is required}"
artifact_suffix="${DOKA_EF_CORE_ARTIFACT_SUFFIX:?DOKA_EF_CORE_ARTIFACT_SUFFIX is required}"
spec_targets="${DOKA_EF_CORE_SPEC_TARGETS:-mysql84,mariadb118}"
integration_targets="${DOKA_INTEGRATION_TARGETS:-mysql84,mariadb118}"
evidence_dir="${repo_root}/artifacts/efcore-patch-matrix/${artifact_suffix}"
resolved_packages_file="${evidence_dir}/resolved-packages.json"
summary_file="${evidence_dir}/efcore-contract-evidence.json"

# The three packages below are the EF Core surface this provider compiles and
# tests against. They must resolve to one identical patch; a split graph would
# let a passing suite describe two different EF Core versions at once.
required_packages='["Microsoft.EntityFrameworkCore.Design",
  "Microsoft.EntityFrameworkCore.Relational",
  "Microsoft.EntityFrameworkCore.Relational.Specification.Tests"]'

command -v jq >/dev/null 2>&1 || {
    echo "jq is required to verify EF Core matrix evidence." >&2
    exit 1
}

export DokaEfCoreVersion="${requested_version}"
export CentralPackageFloatingVersionsEnabled=true

mkdir -p "${evidence_dir}"
cd "${repo_root}"

"${repo_root}/eng/common/verify-dotnet.sh"
dotnet restore "${solution}" --tl:off

# Read back the resolved graph instead of treating the requested central
# package expression as proof of the version that NuGet selected.
dotnet package list \
    --project "${solution}" \
    --include-transitive \
    --format json \
    --no-restore > "${resolved_packages_file}"

resolved_version="$(jq -er \
    --arg pattern "${resolved_pattern}" \
    --argjson required "${required_packages}" '
    [
      .projects[].frameworks[].topLevelPackages[]?
      | select(.id as $id | $required | index($id))
    ] as $packages
    | ($packages | map(.id) | unique) as $found
    | ($packages | map(.resolvedVersion) | unique) as $versions
    | select(
        ($required - $found) == []
        and ($versions | length) == 1
        and ($versions | all(test($pattern)))
      )
    | $versions[0]
' "${resolved_packages_file}")"

jq -r --argjson required "${required_packages}" '
    .projects[].frameworks[].topLevelPackages[]?
    | select(.id as $id | $required | index($id))
    | "\(.id) requested=\(.requestedVersion) resolved=\(.resolvedVersion)"
' "${resolved_packages_file}" | sort -u

bash "${repo_root}/eng/testing/test.sh"

# Specification and live coverage runs per engine so a patch that changes
# translation or materialization behavior is caught for both families rather
# than only for whichever engine happens to be listed first.
IFS=',' read -r -a spec_target_list <<< "${spec_targets}"
for spec_target in "${spec_target_list[@]}"; do
    target_dir="${evidence_dir}/${spec_target}"
    mkdir -p "${target_dir}"
    DOKA_SPEC_TEST_TARGET="${spec_target}" \
    DOKA_TEST_DATABASE_EVIDENCE_FILE="${target_dir}/test-database-evidence.json" \
        dotnet test "${functional_project}" \
            --configuration Release \
            --no-build \
            --no-restore \
            --tl:off \
            --filter "Category=Spec|Category=Live" \
            --logger trx \
            --results-directory "${target_dir}"
    bash "${repo_root}/eng/testing/check-spec-results.sh" \
        "${spec_target}" \
        "${target_dir}"
done

DOKA_INTEGRATION_TARGETS="${integration_targets}" \
    bash "${repo_root}/eng/testing/test-integration.sh"

# Keep the contract list machine-readable so release review can distinguish a
# complete matrix row from an arbitrary filtered test run.
jq -n \
    --arg generatedUtc "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
    --arg requestedVersion "${requested_version}" \
    --arg resolvedVersion "${resolved_version}" \
    --arg specTargets "${spec_targets}" \
    --arg integrationTargets "${integration_targets}" \
    '{
      schemaVersion: 1,
      generatedUtc: $generatedUtc,
      requestedVersion: $requestedVersion,
      resolvedVersion: $resolvedVersion,
      specificationTargets: ($specTargets | split(",") | sort),
      integrationTargets: ($integrationTargets | split(",") | sort),
      contracts: [
        "repository-test-path",
        "specification-suite",
        "live-suite",
        "integration-matrix",
        "resolved-package-graph"
      ],
      results: {
        dependencies: "resolved-packages.json"
      }
    }' > "${summary_file}"

echo "EF Core ${resolved_version} matrix row passed for ${spec_targets}."
