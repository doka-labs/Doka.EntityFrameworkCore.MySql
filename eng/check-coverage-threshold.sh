#!/usr/bin/env bash

# Aggregates line + branch coverage from every coverage.cobertura.xml report
# below the given directory and enforces the project's coverage threshold.
#
# Inputs:
#   $1                                Coverage root directory (e.g. artifacts/coverage).
#   DOKA_COVERAGE_LINE_THRESHOLD      Minimum overall line rate (percent), default 75.
#   DOKA_COVERAGE_BRANCH_THRESHOLD    Minimum overall branch rate (percent), default 70.
#   DOKA_COVERAGE_PROJECT_FILTER      Optional regex matched against <package name="...">.
#                                     When set, only matching packages contribute to the
#                                     aggregate.
#
# The aggregate computes one rate per dimension from the union of every
# matching report's lines-valid / lines-covered / branches-valid / branches-covered
# attributes on the root <coverage> element. The script exits 0 when both
# aggregates meet their threshold and 1 otherwise.

set -euo pipefail

coverage_root="${1:-}"

if [[ -z "${coverage_root}" ]]; then
    echo "Usage: $(basename "$0") <coverage-root>" >&2
    exit 2
fi

if [[ ! -d "${coverage_root}" ]]; then
    echo "Coverage root '${coverage_root}' does not exist or is not a directory." >&2
    exit 2
fi

line_threshold="${DOKA_COVERAGE_LINE_THRESHOLD:-75}"
branch_threshold="${DOKA_COVERAGE_BRANCH_THRESHOLD:-70}"
project_filter="${DOKA_COVERAGE_PROJECT_FILTER:-^Doka\.EntityFrameworkCore\.MySql$}"

# Resolve cobertura reports without depending on bash 4 features (mapfile);
# macOS ships bash 3.2 by default. The newline-delimited list is consumed
# below via the IFS-controlled for-loop.
reports="$(rg --files --no-ignore --hidden --type-add 'xml:*.xml' --type xml "${coverage_root}" \
    | rg '/coverage\.cobertura\.xml$' \
    | sort -u)"

if [[ -z "${reports}" ]]; then
    echo "No coverage.cobertura.xml report found below '${coverage_root}'." >&2
    exit 2
fi

total_lines_valid=0
total_lines_covered=0
total_branches_valid=0
total_branches_covered=0
matched_packages=0

while IFS= read -r report; do
    if [[ -z "${report}" ]]; then
        continue
    fi

    while IFS=$'\t' read -r package_name lines_valid lines_covered branches_valid branches_covered; do
        if [[ -z "${package_name}" ]]; then
            continue
        fi

        if [[ ! "${package_name}" =~ ${project_filter} ]]; then
            continue
        fi

        total_lines_valid=$(( total_lines_valid + lines_valid ))
        total_lines_covered=$(( total_lines_covered + lines_covered ))
        total_branches_valid=$(( total_branches_valid + branches_valid ))
        total_branches_covered=$(( total_branches_covered + branches_covered ))
        matched_packages=$(( matched_packages + 1 ))
    done < <(python3 -c '
import sys
import xml.etree.ElementTree as ET

tree = ET.parse(sys.argv[1])
root = tree.getroot()

for package in root.iter("package"):
    name = package.get("name", "")
    lines_valid = 0
    lines_covered = 0
    branches_valid = 0
    branches_covered = 0

    for cls in package.iter("class"):
        for line in cls.iter("line"):
            lines_valid += 1
            hits = int(line.get("hits", "0"))
            if hits > 0:
                lines_covered += 1
            condition_coverage = line.get("condition-coverage", "")
            if condition_coverage and "(" in condition_coverage:
                inside = condition_coverage.split("(", 1)[1].rstrip(")")
                if "/" in inside:
                    covered_str, valid_str = inside.split("/", 1)
                    branches_covered += int(covered_str)
                    branches_valid += int(valid_str)

    print(f"{name}\t{lines_valid}\t{lines_covered}\t{branches_valid}\t{branches_covered}")
' "${report}")
done <<< "${reports}"

if [[ "${matched_packages}" -eq 0 ]]; then
    echo "No package matched filter '${project_filter}' below '${coverage_root}'." >&2
    exit 2
fi

if [[ "${total_lines_valid}" -eq 0 ]]; then
    echo "Aggregate lines-valid is zero; coverage instrumentation produced no data." >&2
    exit 2
fi

line_rate_percent="$(awk -v c="${total_lines_covered}" -v v="${total_lines_valid}" 'BEGIN { printf "%.2f", (c / v) * 100 }')"

if [[ "${total_branches_valid}" -gt 0 ]]; then
    branch_rate_percent="$(awk -v c="${total_branches_covered}" -v v="${total_branches_valid}" 'BEGIN { printf "%.2f", (c / v) * 100 }')"
else
    branch_rate_percent="0.00"
fi

echo "Coverage aggregate over ${matched_packages} matching package report(s):"
echo "  Lines:    ${total_lines_covered}/${total_lines_valid} (${line_rate_percent}%)  threshold ${line_threshold}%"
echo "  Branches: ${total_branches_covered}/${total_branches_valid} (${branch_rate_percent}%)  threshold ${branch_threshold}%"

line_pass="$(awk -v r="${line_rate_percent}" -v t="${line_threshold}" 'BEGIN { print (r + 0 >= t + 0) ? 1 : 0 }')"
branch_pass="$(awk -v r="${branch_rate_percent}" -v t="${branch_threshold}" 'BEGIN { print (r + 0 >= t + 0) ? 1 : 0 }')"

if [[ "${line_pass}" -eq 1 && "${branch_pass}" -eq 1 ]]; then
    echo "Coverage threshold met."
    exit 0
fi

echo "Coverage threshold not met." >&2
exit 1
