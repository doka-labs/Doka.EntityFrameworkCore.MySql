#!/usr/bin/env bash

# Re-evaluates raw BenchmarkDotNet reports for one named current run. The
# historical filename remains a stable operator entry point.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
root="${1:-${repo_root}/artifacts/benchmarks}"
run_id="${DOKA_BENCHMARK_GATE_RUN_ID:-}"
profile="${DOKA_BENCHMARK_PROFILE:-scorecard}"
allow_missing="${DOKA_BENCHMARK_GATE_ALLOW_MISSING:-0}"
contract="${repo_root}/benchmarks/performance-contract.json"
project="${repo_root}/benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks.csproj"
passed=0
missing=0

if [[ ! "${run_id}" =~ ^[0-9A-Za-z._-]+$ ]]; then
    echo "DOKA_BENCHMARK_GATE_RUN_ID must identify one current run." >&2
    exit 78
fi

while IFS= read -r target; do
    reports="${root}/${target}/reports/${run_id}"
    soak="${reports}/soak.json"
    if [[ ! -d "${reports}" ]]; then
        echo "MISSING [${target}] ${reports}" >&2
        missing=$(( missing + 1 ))
        continue
    fi

    arguments=(--evaluate "${contract}" "${reports}" "${target}" "${profile}")
    if [[ -f "${soak}" ]]; then
        arguments+=("${soak}")
    fi
    dotnet run --project "${project}" --configuration Release -- "${arguments[@]}"
    passed=$(( passed + 1 ))
done < <(jq -er '.requiredTargets | keys[]' "${contract}")

echo "Performance gate summary: ${passed} passed, ${missing} missing."
if (( passed == 0 )); then
    exit 78
fi
if (( missing > 0 )) && [[ "${allow_missing}" != "1" ]]; then
    exit 78
fi
