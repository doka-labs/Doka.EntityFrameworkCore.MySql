#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
contract_project="${repo_root}/eng/Doka.EntityFrameworkCore.MySql.SpecificationContract/Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj"
target="${1:-}"
trx_path="${2:-}"

if [[ -z "${target}" || -z "${trx_path}" ]]; then
    echo "Usage: $(basename "$0") <mysql84|mariadb114|mariadb118> <trx-file-or-directory>" >&2
    exit 2
fi

dotnet run \
    --project "${contract_project}" \
    --configuration Release \
    --no-build \
    -- \
    trx \
    --root "${repo_root}" \
    --trx "${trx_path}" \
    --target "${target}"
