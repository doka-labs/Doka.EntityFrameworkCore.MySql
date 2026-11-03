#!/usr/bin/env bash

set -euo pipefail

export DOTNET_CLI_USE_MSBUILD_SERVER=0

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
release_version="${1:-}"
output_root="${2:-}"
deadline_module="eng.common.deadline"
sbom_timeout="${DOKA_SBOM_TIMEOUT:-300}"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
spatial_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite"
spatial_project="${spatial_project}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
cache_project="${repo_root}/src/Doka.Caching.MySql/Doka.Caching.MySql.csproj"

if [[ "$#" != "2" || -z "${release_version}" || -z "${output_root}" ]]; then
    echo "Usage: $0 VERSION OUTPUT_ROOT" >&2
    exit 2
fi
if [[ ! "${sbom_timeout}" =~ ^[1-9][0-9]*$ ]]; then
    echo "DOKA_SBOM_TIMEOUT must be a positive integer." >&2
    exit 2
fi

cd "${repo_root}"
python3 -m eng.release.nuget validate-version --version "${release_version}"

# WHY: Materialization can only write frozen graphs at validated paths inside this checkout.
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
if not candidate.is_dir():
    raise SystemExit("OUTPUT_ROOT must be an existing package qualification directory.")
print(candidate)
VALIDATE_OUTPUT
)"

packages_dir="${output_root}/packages"
sbom_components_dir="${output_root}/sbom-components"
sbom_dir="${output_root}/sbom"
runtime_assets="${sbom_components_dir}/runtime/project.assets.json"
spatial_assets="${sbom_components_dir}/spatial/project.assets.json"
cache_assets="${sbom_components_dir}/cache/project.assets.json"

if [[ -L "${sbom_dir}" \
    || ( -e "${sbom_dir}" && ! -d "${sbom_dir}" ) \
    || ( -d "${sbom_dir}" && -n "$(find "${sbom_dir}" -mindepth 1 -print -quit)" ) ]]; then
    echo "SBOM output already exists or is non-regular: ${sbom_dir}" >&2
    exit 1
fi

if [[ ! -d "${packages_dir}" || -L "${packages_dir}" \
    || ! -d "${sbom_components_dir}" || -L "${sbom_components_dir}" ]]; then
    echo "Release packages or frozen dependency assets are missing or non-regular." >&2
    exit 1
fi
if [[ -n "$(find "${sbom_components_dir}" -type l -print -quit)" ]]; then
    echo "Frozen dependency assets must not contain symbolic links." >&2
    exit 1
fi
for assets in "${runtime_assets}" "${spatial_assets}" "${cache_assets}"; do
    if [[ ! -f "${assets}" || ! -s "${assets}" || -L "${assets}" ]]; then
        echo "Release package dependency assets are missing, empty, or non-regular: ${assets}" >&2
        exit 1
    fi
done
assets_count="$(find "${sbom_components_dir}" -name project.assets.json | wc -l | tr -d ' ')"
if [[ "${assets_count}" != "3" ]]; then
    echo "SBOM generation requires exactly three frozen package dependency graphs." >&2
    exit 1
fi
for package_id in Doka.EntityFrameworkCore.MySql Doka.EntityFrameworkCore.MySql.NetTopologySuite Doka.Caching.MySql; do
    package_path="${packages_dir}/${package_id}.${release_version}.nupkg"
    if [[ ! -f "${package_path}" || ! -s "${package_path}" || -L "${package_path}" ]]; then
        echo "Required package is missing, empty, or non-regular: ${package_path}" >&2
        exit 1
    fi
done
package_count="$(find "${packages_dir}" -maxdepth 1 -name '*.nupkg' ! -name '*.symbols.nupkg' | wc -l | tr -d ' ')"
if [[ "${package_count}" != "3" ]]; then
    echo "SBOM generation requires exactly three packages at the requested version." >&2
    exit 1
fi

# WHY: A fresh production restore could silently replace the graph that package qualification proved.
python3 -m eng.release.sbom \
    --repository-root "${repo_root}" \
    --assets "${runtime_assets}" \
    --project "${runtime_project}" \
    --output-directory "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql"
python3 -m eng.release.sbom \
    --repository-root "${repo_root}" \
    --assets "${spatial_assets}" \
    --project "${spatial_project}" \
    --output-directory "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.NetTopologySuite"
python3 -m eng.release.sbom \
    --repository-root "${repo_root}" \
    --assets "${cache_assets}" \
    --project "${cache_project}" \
    --output-directory "${repo_root}/artifacts/obj/Doka.Caching.MySql"

dotnet tool restore --tool-manifest "${repo_root}/dotnet-tools.json"
mkdir -p "${sbom_dir}"

# WHY: Roll forward the pinned tool's runtime without changing the repository SDK pin.
sbom_cmd=(dotnet tool run sbom-tool --allow-roll-forward -- Generate \
    -b "${packages_dir}" \
    -bc "${sbom_components_dir}" \
    -m "${sbom_dir}" \
    -pn "Doka.EntityFrameworkCore.MySql.ReleaseCandidate" \
    -pv "${release_version}" \
    -ps "Doka" \
    -nsb "https://github.com/doka/Doka.EntityFrameworkCore.MySql/releases/sbom" \
    -D true)

echo "Running SBOM generation with a ${sbom_timeout}-second deadline..."
if ! python3 -m "${deadline_module}" \
    --seconds "${sbom_timeout}" \
    --label "SBOM generation" \
    -- "${sbom_cmd[@]}"; then
    echo "SBOM generation failed or exceeded ${sbom_timeout} seconds." >&2
    exit 1
fi
if [[ -z "$(find "${sbom_dir}" -type f -size +0c -print -quit)" \
    || -n "$(find "${sbom_dir}" -type l -print -quit)" ]]; then
    echo "No regular non-empty SBOM evidence was generated under ${sbom_dir}." >&2
    exit 1
fi
