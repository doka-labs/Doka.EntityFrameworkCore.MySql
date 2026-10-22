#!/usr/bin/env bash

# Compares every provider specification test with the exact per-engine contract.
# The structural namespace remains the primary boundary; Category=Spec includes
# EF Core adapters that must share an upstream namespace for runtime compilation.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
functional_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
contract_project="${repo_root}/eng/tools/Doka.EntityFrameworkCore.MySql.SpecificationContract/Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj"
discovery_targets=(
    "mysql84"
    "mysql97"
    "mariadb1011"
    "mariadb114"
    "mariadb118"
    "mariadb123"
)
discovery_directory="$(mktemp -d "${TMPDIR:-/tmp}/doka-spec-discovery.XXXXXX")"

cleanup() {
    rm -rf "${discovery_directory}"
}
trap cleanup EXIT

for discovery_target in "${discovery_targets[@]}"; do
    discovery_output="${discovery_directory}/${discovery_target}.txt"

    if ! DOKA_SPEC_TEST_TARGET="${discovery_target}" dotnet test --project "${functional_test_project}" \
        --configuration Release --no-build --no-restore --tl:off \
        --no-progress --output Normal \
        --filter "FullyQualifiedName~Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.|Category=Spec" \
        --list-tests text > "${discovery_output}" 2>&1; then
        cat "${discovery_output}" >&2
        echo "Specification test discovery failed for ${discovery_target}." >&2
        exit 1
    fi

    dotnet run \
        --project "${contract_project}" \
        --configuration Release \
        --no-build \
        -- \
        discovery-validate \
        --root "${repo_root}" \
        --actual "${discovery_output}" \
        --target "${discovery_target}"
done

echo "Specification discovery matches every exact target contract."
