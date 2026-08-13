#!/usr/bin/env bash

# Resolves every authored .NET project that contributes to the repository's
# dependency graph. Most examples deliberately stay outside the product
# solution, so an ephemeral all-project solution provides one atomic restore
# without maintaining a second checked-in project list.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
snapshot_root="${repo_root}/artifacts/dependency-snapshot"
snapshot_solution="${snapshot_root}/dependency-snapshot.slnx"
projects=()

mkdir -p "${snapshot_root}"
dotnet new sln \
    --name dependency-snapshot \
    --format slnx \
    --output "${snapshot_root}" \
    --force > /dev/null

# Templates contain release-time placeholders and are not authored build
# projects. Generated artifacts are excluded so a prior run cannot feed its own
# temporary solution back into discovery.
while IFS= read -r project; do
    projects+=("${project}")
done < <(
    find "${repo_root}" \
        -type f \
        -name '*.csproj' \
        ! -path '*/artifacts/*' \
        ! -path "${repo_root}/eng/templates/*" \
        -print \
        | LC_ALL=C sort
)

if (( ${#projects[@]} == 0 )); then
    echo "No authored .NET projects were found for dependency submission." >&2
    exit 1
fi

dotnet sln "${snapshot_solution}" add --in-root "${projects[@]}" > /dev/null
dotnet restore "${snapshot_solution}" --tl:off --disable-parallel
