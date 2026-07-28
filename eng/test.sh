#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unit_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.Tests/Doka.EntityFrameworkCore.MySql.Tests.csproj"
functional_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
required_assets=(
    "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.Tests/project.assets.json"
    "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.FunctionalTests/project.assets.json"
    "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql/project.assets.json"
    "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.NetTopologySuite/project.assets.json"
    "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.AdrValidator/project.assets.json"
    "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.SpecificationContract/project.assets.json"
    "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.TestUtilities/project.assets.json"
    "${repo_root}/artifacts/obj/SpecificationAdapters/project.assets.json"
)

"${repo_root}/eng/verify-dotnet.sh"
"${repo_root}/eng/validate-adrs.sh"
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
    --start-directory "${repo_root}/eng/tests" \
    --pattern "test_*.py"
restore_required=0
for assets_file in "${required_assets[@]}"; do
    if [[ ! -f "${assets_file}" ]]; then
        restore_required=1
        break
    fi
done

if [[ "${restore_required}" -eq 1 ]]; then
    dotnet restore "${unit_test_project}" --tl:off --ignore-failed-sources --disable-parallel
    dotnet restore "${functional_test_project}" --tl:off --ignore-failed-sources --disable-parallel
fi

coverage_results_dir="${DOKA_COVERAGE_RESULTS_DIR:-${repo_root}/artifacts/coverage}"
mkdir -p "${coverage_results_dir}"

dotnet build "${unit_test_project}" --configuration Release --no-restore --tl:off -m:1
dotnet build "${functional_test_project}" --configuration Release --no-restore --tl:off -m:1
bash "${repo_root}/eng/check-spec-contract.sh"
bash "${repo_root}/eng/check-spec-discovery.sh"
dotnet test "${unit_test_project}" --configuration Release --no-build --no-restore --tl:off \
    --collect:"XPlat Code Coverage" \
    --results-directory "${coverage_results_dir}" \
    --logger trx
# Specification-suite tests (Category=Spec) and any standalone live-database tests
# (Category=Live, e.g. MySqlGuidFormatTests) require a live MySQL / MariaDB and run
# in the spec-test / container-matrix CI jobs against test-owned containers; they
# are excluded from the repo-tests path, which intentionally does not start Docker.
dotnet test "${functional_test_project}" --configuration Release --no-build --no-restore --tl:off \
    --filter "Category!=Spec&Category!=Live" \
    --collect:"XPlat Code Coverage" \
    --results-directory "${coverage_results_dir}" \
    --logger trx
