#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
contract_project="${repo_root}/eng/Doka.EntityFrameworkCore.MySql.SpecificationContract/Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj"
provider_assembly="${repo_root}/artifacts/bin/Doka.EntityFrameworkCore.MySql.FunctionalTests/release/Doka.EntityFrameworkCore.MySql.FunctionalTests.dll"

if [[ ! -f "${provider_assembly}" ]]; then
    echo "Functional-test assembly not found at '${provider_assembly}'." >&2
    echo "Build the Release functional-test project before validating the specification contract." >&2
    exit 2
fi

dotnet run \
    --project "${contract_project}" \
    --configuration Release \
    --no-build \
    -- \
    validate \
    --root "${repo_root}" \
    --provider "${provider_assembly}"
