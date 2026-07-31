#!/usr/bin/env bash

# Validates the MADR decision corpus and optionally regenerates its derived
# index through the same typed validator used by CI.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
validator_project="${repo_root}/eng/Doka.EntityFrameworkCore.MySql.AdrValidator/Doka.EntityFrameworkCore.MySql.AdrValidator.csproj"
validator_assets="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.AdrValidator/project.assets.json"
validator_args=(--root "${repo_root}")

if (( $# > 1 )); then
    echo "Usage: ./eng/validate-adrs.sh [--write-index]" >&2
    exit 2
fi

if (( $# == 1 )); then
    if [[ "$1" != "--write-index" ]]; then
        echo "Unknown option '$1'." >&2
        echo "Usage: ./eng/validate-adrs.sh [--write-index]" >&2
        exit 2
    fi

    validator_args+=(--write-index)
fi

"${repo_root}/eng/verify-dotnet.sh"

if [[ ! -f "${validator_assets}" ]]; then
    dotnet restore "${validator_project}" --tl:off
fi

dotnet run \
    --project "${validator_project}" \
    --configuration Release \
    --no-restore \
    -- "${validator_args[@]}"
