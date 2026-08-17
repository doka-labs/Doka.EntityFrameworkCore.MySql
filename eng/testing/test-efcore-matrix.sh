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
validation_scope="${DOKA_EF_CORE_VALIDATION_SCOPE:-full}"
artifacts_root="${repo_root}/artifacts"
evidence_root="${artifacts_root}/efcore-patch-matrix"
evidence_dir="${evidence_root}/${artifact_suffix}"
resolved_packages_file="${evidence_dir}/resolved-packages.json"
summary_file="${evidence_dir}/efcore-contract-evidence.json"
integration_evidence_dir="${evidence_dir}/integration"

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

case "${validation_scope}" in
    dependency-graph | full)
        ;;
    *)
        echo "Unsupported EF Core validation scope: ${validation_scope}" >&2
        exit 2
        ;;
esac

if [[ ! "${artifact_suffix}" =~ ^[a-z0-9]+(-[a-z0-9]+)*$ ]]; then
    echo "Invalid EF Core matrix artifact suffix: ${artifact_suffix}" >&2
    exit 2
fi

if [[ -L "${artifacts_root}" \
    || -L "${evidence_root}" \
    || -L "${evidence_dir}" ]]; then
    echo "EF Core matrix evidence path must not contain a symlink: ${evidence_dir}" >&2
    exit 1
fi

export DokaEfCoreVersion="${requested_version}"
export CentralPackageFloatingVersionsEnabled=true

rm -rf -- "${evidence_dir}"
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

bash "${repo_root}/eng/testing/check-spec-version-contract.sh" "${resolved_version}"

write_summary() {
    local contracts="$1"
    local qualification_source="$2"
    local recorded_spec_targets="$3"
    local recorded_integration_targets="$4"
    local results="$5"

    jq -n \
        --arg generatedUtc "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
        --arg requestedVersion "${requested_version}" \
        --arg resolvedVersion "${resolved_version}" \
        --arg validationScope "${validation_scope}" \
        --arg qualificationSource "${qualification_source}" \
        --arg specTargets "${recorded_spec_targets}" \
        --arg integrationTargets "${recorded_integration_targets}" \
        --argjson contracts "${contracts}" \
        --argjson results "${results}" \
        '{
          schemaVersion: 2,
          generatedUtc: $generatedUtc,
          requestedVersion: $requestedVersion,
          resolvedVersion: $resolvedVersion,
          validationScope: $validationScope,
          qualificationSource: (
            if $qualificationSource == "" then null else $qualificationSource end
          ),
          specificationTargets: (
            if $specTargets == "" then [] else ($specTargets | split(",") | sort) end
          ),
          integrationTargets: (
            if $integrationTargets == "" then [] else ($integrationTargets | split(",") | sort) end
          ),
          contracts: $contracts,
          results: $results
        }' > "${summary_file}"
}

if [[ "${validation_scope}" == "dependency-graph" ]]; then
    write_summary \
        '["resolved-package-graph", "version-contract-preflight"]' \
        "repository-qualification" \
        "" \
        "" \
        '{"dependencies":"resolved-packages.json"}'
    echo "EF Core ${resolved_version} dependency-floor graph passed."
    exit 0
fi

DOKA_PUBLICATION_EF_CORE_VERSION="${resolved_version}" \
DOKA_PUBLICATION_MYSQLCONNECTOR_VERSION="2.5.0" \
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

DOKA_INTEGRATION_ARTIFACTS_DIR="${integration_evidence_dir}" \
DOKA_INTEGRATION_TARGETS="${integration_targets}" \
    bash "${repo_root}/eng/testing/test-integration.sh"

write_summary \
    '[
      "integration-matrix",
      "live-suite",
      "repository-test-path",
      "resolved-package-graph",
      "specification-suite",
      "version-contract-preflight"
    ]' \
    "" \
    "${spec_targets}" \
    "${integration_targets}" \
    '{
      "dependencies": "resolved-packages.json",
      "integration": "integration/compatibility-matrix-evidence.json"
    }'

echo "EF Core ${resolved_version} matrix row passed for ${spec_targets}."
