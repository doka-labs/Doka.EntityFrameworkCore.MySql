#!/usr/bin/env bash

# Restores the exact published provider packages from NuGet.org into an empty
# package cache, then runs the existing compiled-model runtime contract against
# the candidate's digest-pinned MySQL 8.4 image. This proves public consumption
# instead of accidentally rebuilding or loading the repository projects.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
runtime_smoke_root="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.RuntimeSmoke"
project_template="${repo_root}/eng/templates/nuget-readback.csproj"
compose_file="${repo_root}/docker/compose.yml"
release_version="${1:-}"
release_tag="${2:-}"
source_commit="${3:-}"
expected_engine_image="${4:-}"
evidence_dir="${5:-}"
run_identity="${GITHUB_RUN_ID:-local-$$}-${GITHUB_RUN_ATTEMPT:-1}"
# The project name scopes Compose resources to this checkout and workflow
# attempt, so cleanup cannot tear down an unrelated developer stack.
repo_fingerprint="$(printf '%s' "${repo_root}" | cksum | awk '{print $1}')"
compose_project="doka-nuget-readback-${repo_fingerprint}-${run_identity}"
compose_project="$(printf '%s' "${compose_project}" | tr '[:upper:]_' '[:lower:]-')"
compose_command=(docker compose -p "${compose_project}" -f "${compose_file}")
temporary_root=""
owned_stack=0

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/testing/test-nuget-readback.sh VERSION TAG COMMIT ENGINE_IMAGE EVIDENCE_DIR

Restores both Doka provider packages from NuGet.org with an empty cache and
runs their compiled-model basic and spatial contracts against MySQL 8.4.
EOF
}

if [[ -z "${release_version}" \
    || -z "${release_tag}" \
    || -z "${source_commit}" \
    || -z "${expected_engine_image}" \
    || -z "${evidence_dir}" ]]; then
    print_usage >&2
    exit 2
fi

if [[ "${release_tag}" != "v${release_version}" \
    || ! "${source_commit}" =~ ^[0-9a-f]{40}$ ]]; then
    echo "NuGet readback identity is invalid." >&2
    exit 2
fi

cleanup() {
    local exit_code="$1"

    # Only a stack successfully started by this process is eligible for
    # teardown. Preserve a teardown failure when the test itself was green.
    if [[ "${owned_stack}" -eq 1 ]]; then
        set +e
        "${compose_command[@]}" down --volumes --remove-orphans
        local compose_exit_code=$?
        set -e

        if [[ "${exit_code}" -eq 0 && "${compose_exit_code}" -ne 0 ]]; then
            exit_code="${compose_exit_code}"
        fi
    fi

    if [[ -n "${temporary_root}" && -d "${temporary_root}" ]]; then
        rm -rf -- "${temporary_root}"
    fi

    exit "${exit_code}"
}

trap 'cleanup "$?"' EXIT

if ! command -v docker >/dev/null 2>&1 || ! docker compose version >/dev/null 2>&1; then
    echo "Docker Compose is required for published-package runtime readback." >&2
    exit 1
fi

temporary_root="$(mktemp -d)"
consumer_root="${temporary_root}/consumer"
package_cache="${temporary_root}/packages"
http_cache="${temporary_root}/http-cache"
cli_home="${temporary_root}/dotnet-home"
mkdir -p "${consumer_root}" "${package_cache}" "${http_cache}" "${cli_home}" "${evidence_dir}"

cp "${project_template}" "${consumer_root}/NuGetReadback.csproj"
# Copy only consumer source into the temporary project. A ProjectReference
# would silently prove the checkout instead of the package restored below.
cp "${runtime_smoke_root}/Imports.cs" "${consumer_root}/Imports.cs"
cp "${runtime_smoke_root}/Program.cs" "${consumer_root}/Program.cs"
cp "${runtime_smoke_root}/CompiledModelAccessor.cs" "${consumer_root}/CompiledModelAccessor.cs"
cp -R "${runtime_smoke_root}/CompiledModels" "${consumer_root}/CompiledModels"

export DOTNET_CLI_HOME="${cli_home}"
export NUGET_HTTP_CACHE_PATH="${http_cache}"
export NUGET_PACKAGES="${package_cache}"

consumer_project="${consumer_root}/NuGetReadback.csproj"
# Explicit source and empty caches keep machine-level NuGet configuration and
# previously restored package bytes outside the evidence boundary.
dotnet restore "${consumer_project}" \
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

export DOKA_MYSQL84_PORT=0
"${compose_command[@]}" up -d mysql84
owned_stack=1

mysql_container_id="$("${compose_command[@]}" ps -q mysql84)"
# The release candidate records an immutable image digest. Comparing the
# running container closes the gap between Compose configuration and reality.
actual_engine_image="$(docker inspect --format '{{.Config.Image}}' "${mysql_container_id}")"
if [[ "${actual_engine_image}" != "${expected_engine_image}" ]]; then
    echo "Readback engine image does not match the release candidate." >&2
    echo "Expected: ${expected_engine_image}" >&2
    echo "Actual:   ${actual_engine_image}" >&2
    exit 1
fi

published_endpoint="$("${compose_command[@]}" port mysql84 3306)"
mysql_port="${published_endpoint##*:}"
export DOKA_RUNTIME_SMOKE_CONNECTION_STRING="Server=127.0.0.1;Port=${mysql_port};\
User ID=root;Password=root_password;"

# Compose health is stronger than a fixed delay and prevents the first client
# connection from racing MySQL's post-start initialization.
wait_deadline=$(( $(date +%s) + 120 ))
while true; do
    health="$(
        docker inspect \
            --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' \
            "${mysql_container_id}"
    )"
    if [[ "${health}" == "healthy" ]]; then
        break
    fi
    if (( $(date +%s) >= wait_deadline )); then
        echo "Timed out waiting for the NuGet readback MySQL service." >&2
        "${compose_command[@]}" logs mysql84 >&2
        exit 1
    fi
    sleep 2
done

dotnet run \
    --project "${consumer_project}" \
    --configuration Release \
    --no-build \
    --no-restore \
    -p:DokaPackageVersion="${release_version}"

python3 -m eng.release.nuget verify-restore \
    --assets "${consumer_root}/obj/project.assets.json" \
    --package-cache "${package_cache}" \
    --version "${release_version}" \
    --release-tag "${release_tag}" \
    --source-commit "${source_commit}" \
    --dotnet-sdk "$(dotnet --version)" \
    --engine-image "${actual_engine_image}" \
    --output "${evidence_dir}/consumer-runtime-readback.json"

echo "Published NuGet package runtime readback passed."
