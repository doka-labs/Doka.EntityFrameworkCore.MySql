#!/usr/bin/env bash

# Runs deterministic engineering, unit, and non-live functional tests. Restore
# is conditional so an offline developer loop reuses valid assets while a fresh
# checkout can still hydrate every referenced test graph.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unit_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.Tests/Doka.EntityFrameworkCore.MySql.Tests.csproj"
functional_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
restore_projects=(
    "Doka.EntityFrameworkCore.MySql.Tests"
    "Doka.EntityFrameworkCore.MySql.FunctionalTests"
    "Doka.EntityFrameworkCore.MySql"
    "Doka.EntityFrameworkCore.MySql.NetTopologySuite"
    "Doka.EntityFrameworkCore.MySql.AdrValidator"
    "Doka.EntityFrameworkCore.MySql.SpecificationContract"
    "Doka.EntityFrameworkCore.MySql.TestUtilities"
    "SpecificationAdapters"
)

"${repo_root}/eng/verify-dotnet.sh"
"${repo_root}/eng/validate-adrs.sh"
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
    --start-directory "${repo_root}/eng/tests" \
    --pattern "test_*.py"
restore_required=0
# NuGet writes the resolved graph and the MSBuild imports as one restore unit.
# Checking only project.assets.json can accept a partial obj tree in which
# buildTransitive targets, including PublicApiAnalyzers, are silently absent.
for project_name in "${restore_projects[@]}"; do
    project_obj="${repo_root}/artifacts/obj/${project_name}"
    required_restore_artifacts=(
        "${project_obj}/project.assets.json"
        "${project_obj}/${project_name}.csproj.nuget.g.props"
        "${project_obj}/${project_name}.csproj.nuget.g.targets"
    )

    for restore_artifact in "${required_restore_artifacts[@]}"; do
        if [[ -f "${restore_artifact}" ]]; then
            continue
        fi

        restore_required=1
        break
    done

    if [[ "${restore_required}" -eq 1 ]]; then
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
