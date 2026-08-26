#!/usr/bin/env bash

# Restores the release-candidate packages into an isolated consumer before
# publication. The restored package bytes are compared with the candidate
# files, so a project reference, stale cache entry, or remote package cannot
# satisfy this gate accidentally.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
runtime_smoke_root="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.RuntimeSmoke"
cache_smoke_root="${repo_root}/tests/Doka.Caching.MySql.RuntimeSmoke"
project_template="${repo_root}/eng/templates/nuget-readback.csproj"
release_version="${1:-}"
packages_dir="${2:-}"
evidence_dir="${3:-}"
temporary_root=""

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/testing/test-local-package-consumer.sh VERSION PACKAGES_DIR EVIDENCE_DIR

Restores the exact locally packed provider artifacts into an isolated
package-only consumer, executes the public handler conformance contract, and
records package-byte evidence.
EOF
}

if [[ -z "${release_version}" \
    || -z "${packages_dir}" \
    || -z "${evidence_dir}" ]]; then
    print_usage >&2
    exit 2
fi

packages_dir="$(cd "${packages_dir}" && pwd)"
mkdir -p "${evidence_dir}"
evidence_dir="$(cd "${evidence_dir}" && pwd)"

bash "${repo_root}/eng/common/verify-dotnet.sh"

provider_id="Doka.EntityFrameworkCore.MySql"
spatial_id="Doka.EntityFrameworkCore.MySql.NetTopologySuite"
provider_cache_id="doka.entityframeworkcore.mysql"
spatial_cache_id="doka.entityframeworkcore.mysql.nettopologysuite"
provider_package="${packages_dir}/${provider_id}.${release_version}.nupkg"
spatial_package="${packages_dir}/${spatial_id}.${release_version}.nupkg"
cache_package="${packages_dir}/Doka.Caching.MySql.${release_version}.nupkg"
evidence_file="${evidence_dir}/local-package-consumer.json"

for package_path in "${provider_package}" "${spatial_package}" "${cache_package}"; do
    if [[ ! -f "${package_path}" ]]; then
        echo "Candidate package does not exist: ${package_path}" >&2
        exit 1
    fi
done

cleanup() {
    local exit_code="$1"

    if [[ -n "${temporary_root}" && -d "${temporary_root}" ]]; then
        rm -rf -- "${temporary_root}"
    fi

    exit "${exit_code}"
}

trap 'cleanup "$?"' EXIT

sha256_file() {
    local path="$1"

    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "${path}" | awk '{print $1}'
        return
    fi

    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "${path}" | awk '{print $1}'
        return
    fi

    echo "sha256sum or shasum is required to bind package-consumer evidence." >&2
    exit 1
}

find_restored_package() {
    local package_id="$1"
    local package_root="$2"
    local matches

    if [[ ! -d "${package_root}/${package_id}" ]]; then
        echo "No isolated package-cache directory exists for ${package_id}." >&2
        exit 1
    fi

    matches="$(
        find "${package_root}/${package_id}" \
            -type f \
            -name '*.nupkg' \
            2>/dev/null \
            | sort
    )"

    if [[ -z "${matches}" \
        || "$(printf '%s\n' "${matches}" | wc -l | tr -d ' ')" != "1" ]]; then
        echo "Expected one restored package for ${package_id}, observed:" >&2
        printf '%s\n' "${matches:-  <none>}" >&2
        exit 1
    fi

    printf '%s\n' "${matches}"
}

temporary_root="$(mktemp -d)"
consumer_root="${temporary_root}/consumer"
cache_consumer_root="${temporary_root}/cache-consumer"
package_cache="${temporary_root}/packages"
http_cache="${temporary_root}/http-cache"
cli_home="${temporary_root}/dotnet-home"
mkdir -p "${consumer_root}" "${cache_consumer_root}" "${package_cache}" "${http_cache}" "${cli_home}"

cp "${project_template}" "${consumer_root}/LocalPackageConsumer.csproj"

# Reuse the public runtime consumer source, but place it outside the repository
# so central build files and ProjectReference substitution cannot enter the
# qualification boundary.
cp "${runtime_smoke_root}/Imports.cs" "${consumer_root}/Imports.cs"
cp "${runtime_smoke_root}/Program.cs" "${consumer_root}/Program.cs"
cp "${runtime_smoke_root}/CompiledModelAccessor.cs" "${consumer_root}/CompiledModelAccessor.cs"
cp -R "${runtime_smoke_root}/CompiledModels" "${consumer_root}/CompiledModels"
cp "${repo_root}/eng/templates/cache-readback.csproj" "${cache_consumer_root}/CacheConsumer.csproj"
cp "${cache_smoke_root}/Imports.cs" "${cache_consumer_root}/Imports.cs"
cp "${cache_smoke_root}/Program.cs" "${cache_consumer_root}/Program.cs"

export DOTNET_CLI_HOME="${cli_home}"
export DOTNET_CLI_TELEMETRY_OPTOUT=true
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
export DOTNET_NOLOGO=true
export NUGET_HTTP_CACHE_PATH="${http_cache}"
export NUGET_PACKAGES="${package_cache}"

consumer_project="${consumer_root}/LocalPackageConsumer.csproj"
dotnet restore "${consumer_project}" \
    --source "${packages_dir}" \
    --source "https://api.nuget.org/v3/index.json" \
    --packages "${package_cache}" \
    --force \
    --no-cache \
    --tl:off \
    -p:DokaPackageVersion="${release_version}"

dotnet build "${consumer_project}" \
    --configuration Release \
    --no-restore \
    --tl:off \
    -p:DokaPackageVersion="${release_version}"

# Execute the handlers through EF Core's provider service graph. The dedicated
# mode needs no database, but proves both handler and provider registration
# orders, exact-type and unknown-type dispatch, provider baseline rendering,
# command boundaries, context expiry, and both registration-conflict classes
# from restored package bytes.
dotnet run \
    --project "${consumer_project}" \
    --configuration Release \
    --no-build \
    --no-restore \
    -- \
    --migration-handler-only

cache_consumer_project="${cache_consumer_root}/CacheConsumer.csproj"
dotnet restore "${cache_consumer_project}" \
    --source "${packages_dir}" \
    --source "https://api.nuget.org/v3/index.json" \
    --packages "${package_cache}" \
    --force --no-cache --tl:off \
    -p:DokaPackageVersion="${release_version}"
dotnet build "${cache_consumer_project}" \
    --configuration Release --no-restore --tl:off \
    -p:DokaPackageVersion="${release_version}"
dotnet run --project "${cache_consumer_project}" \
    --configuration Release --no-build --no-restore -- --registration-only

jq -e --arg cacheKey "Doka.Caching.MySql/${release_version}" '
    .libraries[$cacheKey].type == "package"
    and all(.libraries | to_entries[];
      .value.type != "project"
      and (.key | ascii_downcase | startswith("microsoft.entityframeworkcore") or startswith("doka.entityframeworkcore")
        or startswith("pomelo.") | not))
    ' "${cache_consumer_root}/obj/project.assets.json" >/dev/null

assets_file="${consumer_root}/obj/project.assets.json"
provider_key="${provider_id}/${release_version}"
spatial_key="${spatial_id}/${release_version}"

# Package keys are case-insensitive, while project references have a distinct
# assets type. Checking both properties proves the consumer compiled only
# against the two expected package identities.
jq -e \
    --arg providerKey "${provider_key}" \
    --arg spatialKey "${spatial_key}" \
    '
      def package($key):
        [.libraries | to_entries[]
          | select((.key | ascii_downcase) == ($key | ascii_downcase))]
        | length == 1 and .[0].value.type == "package";
      package($providerKey)
      and package($spatialKey)
      and all(.libraries[]; .type != "project")
    ' \
    "${assets_file}" >/dev/null

restored_provider="$(find_restored_package "${provider_cache_id}" "${package_cache}")"
restored_spatial="$(find_restored_package "${spatial_cache_id}" "${package_cache}")"
restored_cache="$(find_restored_package "doka.caching.mysql" "${package_cache}")"
provider_sha256="$(sha256_file "${provider_package}")"
spatial_sha256="$(sha256_file "${spatial_package}")"
cache_sha256="$(sha256_file "${cache_package}")"
restored_provider_sha256="$(sha256_file "${restored_provider}")"
restored_spatial_sha256="$(sha256_file "${restored_spatial}")"
restored_cache_sha256="$(sha256_file "${restored_cache}")"

if [[ "${provider_sha256}" != "${restored_provider_sha256}" \
    || "${spatial_sha256}" != "${restored_spatial_sha256}" \
    || "${cache_sha256}" != "${restored_cache_sha256}" ]]; then
    echo "The isolated consumer did not restore the exact candidate package bytes." >&2
    exit 1
fi

jq -n \
    --arg generatedUtc "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
    --arg releaseVersion "${release_version}" \
    --arg providerSha256 "${provider_sha256}" \
    --arg spatialSha256 "${spatial_sha256}" \
    --arg cacheSha256 "${cache_sha256}" \
    '{
      schemaVersion: 3,
      generatedUtc: $generatedUtc,
      qualification: "pass",
      consumerBoundary: "isolated-local-package",
      qualificationSurface: "provider-migration-operation-conformance",
      cacheRegistration: "pass",
      cacheEfCoreDependencies: 0,
      migrationOperationHandlerConformance: {
        baselineRendering: "pass",
        commandBoundaries: "pass",
        registrationOrderIndependence: "pass",
        exactTypeDispatch: "pass",
        unknownOperationFailure: "pass",
        contextLifetime: "pass",
        duplicateHandlerIdFailure: "pass",
        duplicateOperationOwnershipFailure: "pass"
      },
      projectReferences: 0,
      releaseVersion: $releaseVersion,
      packages: [
        {
          id: "Doka.EntityFrameworkCore.MySql",
          sha256: $providerSha256
        },
        {
          id: "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
          sha256: $spatialSha256
        },
        {
          id: "Doka.Caching.MySql",
          sha256: $cacheSha256
        }
      ]
    }' > "${evidence_file}"

echo "Local package-only consumer conformance passed."
echo "Evidence: ${evidence_file}"
