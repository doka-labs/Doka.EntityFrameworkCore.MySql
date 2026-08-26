#!/usr/bin/env bash

# Verifies the exact dependency closure used to build all shipped packages.
# This gate must run before any ordinary solution restore: NuGet may otherwise
# rewrite a stale lock file and hide the drift until release-candidate assembly.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
runtime_lock="${repo_root}/src/Doka.EntityFrameworkCore.MySql/packages.lock.json"
spatial_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
spatial_lock="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/packages.lock.json"
cache_project="${repo_root}/src/Doka.Caching.MySql/Doka.Caching.MySql.csproj"
cache_lock="${repo_root}/src/Doka.Caching.MySql/packages.lock.json"

for lock_file in "${runtime_lock}" "${spatial_lock}" "${cache_lock}"; do
    if [[ ! -f "${lock_file}" || -L "${lock_file}" ]]; then
        echo "Release package lock is missing or non-regular: ${lock_file}" >&2
        exit 1
    fi
done

dotnet restore "${runtime_project}" --locked-mode --tl:off
dotnet restore "${spatial_project}" --locked-mode --tl:off
dotnet restore "${cache_project}" --locked-mode --tl:off

# A prior ordinary restore may already have repaired a stale tracked lock in
# the working tree. Reject that uncommitted repair instead of accepting a
# release graph that differs from the reviewed commit.
if ! git -C "${repo_root}" diff --quiet -- \
    "${runtime_lock}" \
    "${spatial_lock}" \
    "${cache_lock}"; then
    echo "Release package locks contain uncommitted restore changes." >&2
    echo "Review and commit both dependency manifests and lock files together." >&2
    exit 1
fi

echo "Release package dependency locks are current."
