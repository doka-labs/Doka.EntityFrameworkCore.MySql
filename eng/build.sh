#!/usr/bin/env bash

# Restores and builds every shipped provider and test project in Release mode
# after validating the repository SDK and architecture-decision corpus.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
spatial_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
unit_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.Tests/Doka.EntityFrameworkCore.MySql.Tests.csproj"
functional_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
integration_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Doka.EntityFrameworkCore.MySql.IntegrationTests.csproj"

"${repo_root}/eng/verify-dotnet.sh"
"${repo_root}/eng/validate-adrs.sh"
dotnet restore "${runtime_project}"
dotnet restore "${spatial_project}"
dotnet restore "${unit_test_project}"
dotnet restore "${functional_test_project}"
dotnet restore "${integration_test_project}"
dotnet build "${runtime_project}" --configuration Release --no-restore
dotnet build "${spatial_project}" --configuration Release --no-restore
dotnet build "${unit_test_project}" --configuration Release --no-restore
dotnet build "${functional_test_project}" --configuration Release --no-restore
dotnet build "${integration_test_project}" --configuration Release --no-restore
