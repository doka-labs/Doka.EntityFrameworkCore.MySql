#!/usr/bin/env bash

# Validates one fresh ReportGenerator-merged Cobertura union against the
# assembly- and critical-class floors in eng/coverage-policy.json.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
coverage_root="${1:-}"
policy_path="${2:-${repo_root}/eng/coverage-policy.json}"

if [[ -z "${coverage_root}" ]]; then
    echo "Usage: $(basename "$0") <merged-coverage-root> [coverage-policy.json]" >&2
    exit 2
fi

if [[ ! -d "${coverage_root}" ]]; then
    echo "Coverage root '${coverage_root}' does not exist or is not a directory." >&2
    exit 2
fi

if [[ ! -f "${policy_path}" ]]; then
    echo "Coverage policy '${policy_path}' does not exist." >&2
    exit 2
fi

# A single ReportGenerator union prevents duplicate source lines from being
# counted once per test assembly or target engine.
reports="$(find "${coverage_root}" -type f -name 'coverage.cobertura.xml' | sort -u)"
report_count="$(printf '%s\n' "${reports}" | awk 'NF { count++ } END { print count + 0 }')"
if [[ "${report_count}" -ne 1 ]]; then
    echo "Expected exactly one merged coverage.cobertura.xml; found ${report_count}." >&2
    exit 2
fi

dotnet run \
    --project "${repo_root}/eng/tools/Doka.EntityFrameworkCore.MySql.CoverageGate/Doka.EntityFrameworkCore.MySql.CoverageGate.csproj" \
    --configuration Release \
    -- \
    "${reports}" \
    "${policy_path}"
