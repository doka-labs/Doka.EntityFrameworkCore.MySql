#!/usr/bin/env bash

set -euo pipefail

export DOTNET_CLI_USE_MSBUILD_SERVER=0

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
release_version_override="${1:-}"
output_root="${2:-}"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
spatial_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite"
spatial_project="${spatial_project}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
cache_project="${repo_root}/src/Doka.Caching.MySql/Doka.Caching.MySql.csproj"

if [[ "$#" != "2" || -z "${release_version_override}" || -z "${output_root}" ]]; then
    echo "Usage: $0 VERSION OUTPUT_ROOT" >&2
    exit 2
fi

cd "${repo_root}"
python3 -m eng.release.nuget validate-version --version "${release_version_override}"

# WHY: The frozen graphs must remain inside the checkout for SBOM materialization.
output_root="$(python3 - "${repo_root}" "${output_root}" <<'VALIDATE_OUTPUT'
import os
import sys
from pathlib import Path

repository = Path(sys.argv[1])
raw = sys.argv[2]
if not raw.isascii() or any(ord(character) < 32 or ord(character) == 127 for character in raw):
    raise SystemExit("OUTPUT_ROOT must be a non-empty printable ASCII path.")
candidate = Path(raw)
if not candidate.is_absolute():
    candidate = repository / candidate
candidate = Path(os.path.abspath(candidate))
if any(path.is_symlink() for path in (candidate, *candidate.parents)):
    raise SystemExit("OUTPUT_ROOT must not contain symbolic links.")
candidate = candidate.resolve()
if repository not in candidate.parents or candidate.relative_to(repository).parts[0] in (
    ".git", ".agents", ".codex"
):
    raise SystemExit("OUTPUT_ROOT must remain strictly below the repository root outside metadata.")
if candidate.exists() and not candidate.is_dir():
    raise SystemExit("OUTPUT_ROOT must be a directory.")
print(candidate)
VALIDATE_OUTPUT
)"

packages_dir="${output_root}/packages"
local_package_consumer_dir="${output_root}/local-package-consumer"
sbom_components_dir="${output_root}/sbom-components"
dependency_graph_file="${output_root}/resolved-packages.json"
expected_release_tag="v${release_version_override}"

# WHY: Other stages can share the root, but these outputs must belong to this invocation.
for directory in "${packages_dir}" "${local_package_consumer_dir}" "${sbom_components_dir}"; do
    if [[ -L "${directory}" \
        || ( -e "${directory}" && ! -d "${directory}" ) \
        || ( -d "${directory}" && -n "$(find "${directory}" -mindepth 1 -print -quit)" ) ]]; then
        echo "Package qualification output already exists or is non-regular: ${directory}" >&2
        exit 1
    fi
done
if [[ -e "${dependency_graph_file}" || -L "${dependency_graph_file}" ]]; then
    echo "Package qualification output already exists: ${dependency_graph_file}" >&2
    exit 1
fi

run_with_release_version() {
    local command_arguments=("$@")

    command_arguments+=("-p:PackageVersion=${release_version_override}")
    "${command_arguments[@]}"
}

require_evidence_file() {
    local path="$1"

    if [[ ! -s "${path}" || ! -f "${path}" || -L "${path}" ]]; then
        echo "Required package qualification evidence is missing, empty, or non-regular: ${path}" >&2
        exit 1
    fi
}

mkdir -p "${packages_dir}"
mkdir -p "${sbom_components_dir}/runtime" "${sbom_components_dir}/spatial" "${sbom_components_dir}/cache"

# WHY: Verify the reviewed closure before any candidate bytes or graph copies exist.
"${repo_root}/eng/quality/verify-release-package-locks.sh"

for component in Doka.EntityFrameworkCore.MySql Doka.EntityFrameworkCore.MySql.NetTopologySuite Doka.Caching.MySql; do
    require_evidence_file "${repo_root}/artifacts/obj/${component}/project.assets.json"
done

# WHY: Separate jobs must consume the qualified graphs, which later builds can replace in obj.
cp \
    "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql/project.assets.json" \
    "${sbom_components_dir}/runtime/project.assets.json"
cp \
    "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.NetTopologySuite/project.assets.json" \
    "${sbom_components_dir}/spatial/project.assets.json"
cp \
    "${repo_root}/artifacts/obj/Doka.Caching.MySql/project.assets.json" \
    "${sbom_components_dir}/cache/project.assets.json"

run_with_release_version \
    dotnet build "${runtime_project}" --configuration Release --no-restore --tl:off -m:1
run_with_release_version \
    dotnet build "${spatial_project}" --configuration Release --no-restore --tl:off -m:1
run_with_release_version \
    dotnet build "${cache_project}" --configuration Release --no-restore --tl:off -m:1
run_with_release_version \
    dotnet pack "${runtime_project}" --configuration Release --no-build --no-restore \
    --output "${packages_dir}" --tl:off
run_with_release_version \
    dotnet pack "${spatial_project}" --configuration Release --no-build --no-restore \
    --output "${packages_dir}" --tl:off
run_with_release_version \
    dotnet pack "${cache_project}" --configuration Release --no-build --no-restore \
    --output "${packages_dir}" --tl:off

for package_id in Doka.EntityFrameworkCore.MySql Doka.EntityFrameworkCore.MySql.NetTopologySuite Doka.Caching.MySql; do
    require_evidence_file "${packages_dir}/${package_id}.${release_version_override}.nupkg"
done
package_count="$(find "${packages_dir}" -maxdepth 1 -name '*.nupkg' | wc -l | tr -d ' ')"
if [[ "${package_count}" != "3" ]]; then
    echo "Expected package qualification to produce exactly three packages." >&2
    exit 1
fi

bash "${repo_root}/eng/testing/test-local-package-consumer.sh" \
    "${release_version_override}" \
    "${packages_dir}" \
    "${local_package_consumer_dir}"
require_evidence_file "${local_package_consumer_dir}/local-package-consumer.json"

mysql84_image="$(
    docker compose -f "${repo_root}/docker/compose.yml" config --format json \
        | jq -er '.services.mysql84.image'
)"
bash "${repo_root}/eng/testing/test-nuget-readback.sh" \
    "${release_version_override}" \
    "${expected_release_tag}" \
    "$(git rev-parse HEAD)" \
    "${mysql84_image}" \
    "${local_package_consumer_dir}" \
    "${packages_dir}"
require_evidence_file "${local_package_consumer_dir}/local-package-runtime.json"

# WHY: Capture NuGet's actual graph rather than infer dependencies from declarations.
dotnet package list \
    --project "${runtime_project}" \
    --include-transitive \
    --format json \
    --no-restore > "${dependency_graph_file}"
require_evidence_file "${dependency_graph_file}"
jq -e '.projects | type == "array" and length > 0' "${dependency_graph_file}" >/dev/null
