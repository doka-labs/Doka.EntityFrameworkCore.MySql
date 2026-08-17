#!/usr/bin/env bash

# Builds the publication contract in an isolated output tree and executes it
# against the matching provider test assembly. The isolation is intentional:
# a prior developer or CI build must never satisfy this release boundary.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
contract_project="${repo_root}/eng/tools/Doka.EntityFrameworkCore.MySql.SpecificationContract/Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj"
functional_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
ef_core_version=""
mysqlconnector_version=""

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/check-publication-readiness.sh \
    --ef-core-version <major.minor.patch> \
    --mysqlconnector-version <major.minor.patch>
EOF
}

while (( $# > 0 )); do
    case "$1" in
        --ef-core-version)
            if (( $# < 2 )); then
                echo "--ef-core-version requires a value." >&2
                print_usage >&2
                exit 2
            fi
            ef_core_version="$2"
            shift 2
            ;;
        --mysqlconnector-version)
            if (( $# < 2 )); then
                echo "--mysqlconnector-version requires a value." >&2
                print_usage >&2
                exit 2
            fi
            mysqlconnector_version="$2"
            shift 2
            ;;
        --help|-h)
            print_usage
            exit 0
            ;;
        *)
            echo "Unknown option '$1'." >&2
            print_usage >&2
            exit 2
            ;;
    esac
done

if [[ ! "${ef_core_version}" =~ ^10[.]0[.][0-9]+$ ]]; then
    echo "--ef-core-version must identify one exact EF Core 10.0 patch." >&2
    exit 2
fi

if [[ ! "${mysqlconnector_version}" =~ ^2[.][0-9]+[.][0-9]+$ ]]; then
    echo "--mysqlconnector-version must identify one exact MySqlConnector 2.x patch." >&2
    exit 2
fi

"${repo_root}/eng/common/verify-dotnet.sh"

build_root="$(mktemp -d "${TMPDIR:-/tmp}/doka-publication-readiness.XXXXXX")"
cleanup() {
    rm -rf -- "${build_root}"
}
trap cleanup EXIT
mkdir -p "${build_root}/locks"

build_properties=(
    "-p:ArtifactsPath=${build_root}"
    "-p:DokaEfCoreVersion=${ef_core_version}"
    "-p:DokaMySqlConnectorVersion=${mysqlconnector_version}"
    "-p:NuGetLockFilePath=${build_root}/locks/\$(MSBuildProjectName).packages.lock.json"
)

dotnet restore "${contract_project}" "${build_properties[@]}" --tl:off
dotnet restore "${functional_project}" "${build_properties[@]}" --tl:off
dotnet build \
    "${contract_project}" \
    --configuration Release \
    --no-restore \
    --tl:off \
    -m:1 \
    "${build_properties[@]}"
dotnet build \
    "${functional_project}" \
    --configuration Release \
    --no-restore \
    --tl:off \
    -m:1 \
    "${build_properties[@]}"

contract_assembly="${build_root}/bin/Doka.EntityFrameworkCore.MySql.SpecificationContract/release/Doka.EntityFrameworkCore.MySql.SpecificationContract.dll"
provider_assembly="${build_root}/bin/Doka.EntityFrameworkCore.MySql.FunctionalTests/release/Doka.EntityFrameworkCore.MySql.FunctionalTests.dll"

if [[ ! -f "${provider_assembly}" ]]; then
    echo "Functional-test assembly not found at '${provider_assembly}'." >&2
    exit 2
fi

if [[ ! -f "${contract_assembly}" ]]; then
    echo "Specification-contract assembly not found at '${contract_assembly}'." >&2
    exit 2
fi

dotnet "${contract_assembly}" \
    publication \
    --root "${repo_root}" \
    --provider "${provider_assembly}"
