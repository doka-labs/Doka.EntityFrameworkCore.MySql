#!/usr/bin/env bash

# Runs every supported specification target in an independent test host. The
# target list comes from the reviewed disposition contract so a support-line
# change cannot leave this operator command on an older matrix.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
functional_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/"
functional_project+="Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
contract_project="${repo_root}/eng/tools/"
contract_project+="Doka.EntityFrameworkCore.MySql.SpecificationContract/"
contract_project+="Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj"
target_contract="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/"
target_contract+="Specification/SpecDispositions.json"
run_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
results_root="${repo_root}/artifacts/spec-tests/${run_id}"

if [[ "$#" -ne 0 ]]; then
    echo "Usage: ./eng/test-spec-matrix.sh" >&2
    exit 2
fi

if [[ -n "${DOKA_SPEC_TEST_CONNECTION_STRING:-}" \
    || -n "${DOKA_SPEC_TEST_SERVER_VERSION:-}" ]]; then
    echo "The full specification matrix uses one test-owned container per target." >&2
    echo "Unset DOKA_SPEC_TEST_CONNECTION_STRING and DOKA_SPEC_TEST_SERVER_VERSION." >&2
    echo "Use an explicit DOKA_SPEC_TEST_TARGET with dotnet test for one external endpoint." >&2
    exit 2
fi

"${repo_root}/eng/common/verify-dotnet.sh"

supported_targets="$(
    PYTHONDONTWRITEBYTECODE=1 python3 \
        "${repo_root}/eng/testing/spec_matrix.py" \
        "${target_contract}"
)"

supported_target_list=()
while IFS= read -r target; do
    if [[ -n "${target}" ]]; then
        supported_target_list+=("${target}")
    fi
done <<< "${supported_targets}"

if [[ "${#supported_target_list[@]}" -eq 0 ]]; then
    echo "The specification target contract did not provide any supported targets." >&2
    exit 1
fi

mkdir -p "${results_root}"
dotnet build "${functional_project}" --configuration Release --tl:off -m:1
dotnet build "${contract_project}" --configuration Release --tl:off -m:1

for target in "${supported_target_list[@]}"; do
    target_results="${results_root}/${target}"
    mkdir -p "${target_results}"

    echo "Running specification and live functional tests for ${target}..."
    DOKA_SPEC_TEST_TARGET="${target}" \
    DOKA_TEST_DATABASE_EVIDENCE_FILE="${target_results}/test-database-evidence.json" \
        dotnet test "${functional_project}" \
            --configuration Release \
            --no-build \
            --no-restore \
            --tl:off \
            --filter "Category=Spec|Category=Live" \
            --logger "trx;LogFileName=spec-tests.trx" \
            --results-directory "${target_results}"

    bash "${repo_root}/eng/testing/check-spec-results.sh" \
        "${target}" \
        "${target_results}"
done

echo "Specification and live functional tests passed for ${#supported_target_list[@]} supported targets."
echo "Results: ${results_root}"
