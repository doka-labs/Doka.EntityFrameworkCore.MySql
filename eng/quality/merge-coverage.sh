#!/usr/bin/env bash

# Produces one per-line-deduplicated Cobertura union from the supplied coverage
# root. The output directory may be below the input root; it is excluded from
# discovery so a repeated run cannot consume stale merged evidence.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
coverage_root="${1:-}"
output_directory="${2:-}"

if [[ -z "${coverage_root}" || -z "${output_directory}" ]]; then
    echo "Usage: $(basename "$0") <coverage-input-root> <merged-output-directory>" >&2
    exit 2
fi

if [[ ! -d "${coverage_root}" ]]; then
    echo "Coverage input root '${coverage_root}' does not exist." >&2
    exit 2
fi

coverage_root="$(cd "${coverage_root}" && pwd)"
mkdir -p "${output_directory}"
output_directory="$(cd "${output_directory}" && pwd)"

reports="$(
    find "${coverage_root}" \
        -type f \
        -name 'coverage.cobertura.xml' \
        ! -path "${output_directory}/*" \
        | sort -u
)"
if [[ -z "${reports}" ]]; then
    echo "No coverage.cobertura.xml inputs found below '${coverage_root}'." >&2
    exit 2
fi

report_paths=()
while IFS= read -r report; do
    if [[ -n "${report}" ]]; then
        report_paths+=("${report}")
    fi
done <<< "${reports}"

coverage_gate_project="${repo_root}/eng/tools/Doka.EntityFrameworkCore.MySql.CoverageGate/Doka.EntityFrameworkCore.MySql.CoverageGate.csproj"
dotnet run \
    --project "${coverage_gate_project}" \
    --configuration Release \
    -- \
    freshness \
    "${repo_root}/eng/coverage-policy.json" \
    "${report_paths[@]}"

report_argument="$(printf '%s\n' "${reports}" | paste -sd ';' -)"

cd "${repo_root}"
if command -v reportgenerator >/dev/null 2>&1; then
    reportgenerator_command=(reportgenerator)
else
    dotnet tool restore
    reportgenerator_command=(dotnet tool run reportgenerator)
fi

"${reportgenerator_command[@]}" \
    "-reports:${report_argument}" \
    "-targetdir:${output_directory}" \
    "-reporttypes:Cobertura"

generated_report="${output_directory}/Cobertura.xml"
if [[ ! -f "${generated_report}" ]]; then
    echo "ReportGenerator did not create '${generated_report}'." >&2
    exit 1
fi

mv -f "${generated_report}" "${output_directory}/coverage.cobertura.xml"
echo "Merged coverage report written to '${output_directory}/coverage.cobertura.xml'."
