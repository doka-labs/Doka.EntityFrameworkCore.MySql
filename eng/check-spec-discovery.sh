#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
functional_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
discovery_target="${DOKA_SPEC_DISCOVERY_TARGET:-mysql84}"
discovery_output="$(mktemp "${TMPDIR:-/tmp}/doka-spec-discovery.XXXXXX")"

cleanup() {
    rm -f "${discovery_output}"
}
trap cleanup EXIT

if ! DOKA_SPEC_TEST_TARGET="${discovery_target}" dotnet test "${functional_test_project}" \
    --configuration Release --no-build --no-restore --tl:off \
    --filter "Category=Spec" \
    --list-tests \
    --logger "console;verbosity=normal" > "${discovery_output}" 2>&1; then
    cat "${discovery_output}" >&2
    echo "Specification test discovery failed." >&2
    exit 1
fi

if grep -Fq "duplicate ID" "${discovery_output}"; then
    grep -F "duplicate ID" "${discovery_output}" >&2
    echo "Specification test discovery produced duplicate test IDs." >&2
    exit 1
fi

echo "Specification test discovery contains no duplicate test IDs."
