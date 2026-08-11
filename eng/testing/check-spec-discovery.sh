#!/usr/bin/env bash

# Compares discovered specification tests with the exact per-engine contract.
# Both classified and unfiltered discovery are required so missing categories
# cannot hide tests from conformance accounting.

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
    all_specification_output="${discovery_directory}/${discovery_target}-all.txt"

    if ! DOKA_SPEC_TEST_TARGET="${discovery_target}" dotnet test "${functional_test_project}" \
        --configuration Release --no-build --no-restore --tl:off \
        --filter "Category=Spec" \
        --list-tests \
        --logger "console;verbosity=normal" > "${discovery_output}" 2>&1; then
        cat "${discovery_output}" >&2
        echo "Specification test discovery failed for ${discovery_target}." >&2
        exit 1
    fi

    if ! DOKA_SPEC_TEST_TARGET="${discovery_target}" dotnet test "${functional_test_project}" \
        --configuration Release --no-build --no-restore --tl:off \
        --filter "FullyQualifiedName~Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification." \
        --list-tests \
        --logger "console;verbosity=normal" > "${all_specification_output}" 2>&1; then
        cat "${all_specification_output}" >&2
        echo "Unfiltered specification test discovery failed for ${discovery_target}." >&2
        exit 1
    fi

    dotnet run \
        --project "${contract_project}" \
        --configuration Release \
        --no-build \
        -- \
        classification-validate \
        --all "${all_specification_output}" \
        --classified "${discovery_output}"

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
