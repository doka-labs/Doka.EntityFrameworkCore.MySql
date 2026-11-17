#!/usr/bin/env bash

# Validates current native module measurements or one fresh merged Cobertura
# union against the assembly and critical-class floors in coverage-policy.json.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
coverage_gate_project="${repo_root}/eng/tools/Doka.EntityFrameworkCore.MySql.CoverageGate/Doka.EntityFrameworkCore.MySql.CoverageGate.csproj"

if [[ "${1:-}" == "--modules" ]]; then
    if (( $# < 4 )); then
        echo "Usage: $0 --modules <report-directory> <run-prefix> <required-assembly>..." >&2
        exit 2
    fi

    report_directory="$2"
    run_prefix="$3"
    shift 3
    if [[ ! -d "${report_directory}" || ! "${run_prefix}" =~ ^[A-Za-z0-9-]+$ ]]; then
        echo "Native coverage requires a report directory and an exact path-safe run prefix." >&2
        exit 2
    fi

    # WHY: A process-owned prefix rejects stale reports from a previous invocation.
    module_reports=("${report_directory}/${run_prefix}.coverage.cobertura."*.xml)
    if [[ "${#module_reports[@]}" -ne 1 || ! -f "${module_reports[0]}" ]]; then
        echo "Expected exactly one current native coverage report for '${run_prefix}'." >&2
        exit 1
    fi

    dotnet run --project "${coverage_gate_project}" --configuration Release -- \
        modules "${module_reports[0]}" "$@"
    exit "$?"
fi

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
    --project "${coverage_gate_project}" \
    --configuration Release \
    -- \
    "${reports}" \
    "${policy_path}"
