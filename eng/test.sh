#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unit_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.Tests/Doka.EntityFrameworkCore.MySql.Tests.csproj"
functional_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
unit_assets_file="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.Tests/project.assets.json"
unit_core_ref_assets_file="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.Tests/refs/Doka.EntityFrameworkCore.MySql/project.assets.json"
unit_spatial_ref_assets_file="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.Tests/refs/Doka.EntityFrameworkCore.MySql.NetTopologySuite/project.assets.json"
functional_assets_file="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.FunctionalTests/project.assets.json"
functional_core_ref_assets_file="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.FunctionalTests/refs/Doka.EntityFrameworkCore.MySql/project.assets.json"
functional_spatial_ref_assets_file="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.FunctionalTests/refs/Doka.EntityFrameworkCore.MySql.NetTopologySuite/project.assets.json"

"${repo_root}/eng/verify-dotnet.sh"
"${repo_root}/eng/validate-adrs.sh"
if [[ ! -f "${unit_assets_file}" ]] \
    || [[ ! -f "${unit_core_ref_assets_file}" ]] \
    || [[ ! -f "${unit_spatial_ref_assets_file}" ]] \
    || [[ ! -f "${functional_assets_file}" ]] \
    || [[ ! -f "${functional_core_ref_assets_file}" ]] \
    || [[ ! -f "${functional_spatial_ref_assets_file}" ]]; then
    dotnet restore "${unit_test_project}" --tl:off --ignore-failed-sources --disable-parallel
    dotnet restore "${functional_test_project}" --tl:off --ignore-failed-sources --disable-parallel
fi

coverage_results_dir="${repo_root}/artifacts/coverage"
mkdir -p "${coverage_results_dir}"

dotnet build "${unit_test_project}" --configuration Release --no-restore --tl:off -m:1
dotnet build "${functional_test_project}" --configuration Release --no-restore --tl:off -m:1
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
