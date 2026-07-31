#!/usr/bin/env bash

# Re-evaluates the complete current-run performance contract for every required
# benchmark target. The historical filename remains as a compatibility entrypoint
# for release scripts; the gate now covers BDN controls, absolute and historical
# budgets, workload matrix completeness, and sustained-use evidence.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
benchmarks_root="${1:-${repo_root}/artifacts/benchmarks}"
strict="${DOKA_BENCHMARK_GATE_STRICT:-0}"
run_id="${DOKA_BENCHMARK_GATE_RUN_ID:-}"
profile="${DOKA_BENCHMARK_PROFILE:-scorecard}"
contract="${repo_root}/benchmarks/performance-contract.json"
baseline="${DOKA_BENCHMARK_BASELINE_PATH:-${repo_root}/benchmarks/baselines/doka-benchmark-baseline.json}"
evidence_tool="${repo_root}/eng/performance_evidence.py"
required_targets=(
    "mysql84"
    "mariadb118"
)
passes=0
failures=0
skips=0

if [[ ! -d "${benchmarks_root}" ]]; then
    echo "Benchmark artifacts root '${benchmarks_root}' does not exist." >&2
    exit 2
fi

if [[ -z "${run_id}" || ! "${run_id}" =~ ^[0-9A-Za-z._-]+$ ]]; then
    echo "DOKA_BENCHMARK_GATE_RUN_ID must identify one current run." >&2
    exit 2
fi

for target in "${required_targets[@]}"; do
    report_dir="${benchmarks_root}/${target}/reports/${run_id}"
    evidence_dir="${report_dir}/evidence"
    workload_evidence="${evidence_dir}/workload-evidence.json"
    soak_evidence="${evidence_dir}/soak-evidence.json"
    gate_bdn_evidence="${evidence_dir}/gate-benchmarkdotnet-evidence.json"
    gate_evaluation="${evidence_dir}/gate-performance-evaluation.json"

    if [[ ! -f "${workload_evidence}" ]]; then
        echo "SKIP [${target}] current-run workload evidence is missing." >&2
        skips=$(( skips + 1 ))
        continue
    fi

    if ! python3 "${evidence_tool}" validate-bdn \
        --contract "${contract}" \
        --reports "${report_dir}" \
        --run-id "${run_id}" \
        --target "${target}" \
        --profile "${profile}" \
        --output "${gate_bdn_evidence}"; then
        echo "FAIL [${target}] BenchmarkDotNet evidence validation failed." >&2
        failures=$(( failures + 1 ))
        continue
    fi

    command=(
        python3 "${evidence_tool}" evaluate
        --contract "${contract}"
        --baseline "${baseline}"
        --workloads "${workload_evidence}"
        --bdn "${gate_bdn_evidence}"
        --run-id "${run_id}"
        --target "${target}"
        --profile "${profile}"
        --mode compare
        --output "${gate_evaluation}"
    )

    if [[ -f "${soak_evidence}" ]]; then
        command+=(--soak "${soak_evidence}")
    fi

    if "${command[@]}"; then
        echo "PASS [${target}] complete current-run performance and memory evidence."
        passes=$(( passes + 1 ))
    else
        echo "FAIL [${target}] performance or memory gate failed." >&2
        failures=$(( failures + 1 ))
    fi
done

echo
printf 'Performance gate summary: %s pass, %s fail, %s target(s) without current-run evidence.\n' \
    "${passes}" \
    "${failures}" \
    "${skips}"

if [[ "${failures}" -gt 0 ]]; then
    exit 1
fi

if [[ "${skips}" -gt 0 && "${strict}" == "1" ]]; then
    echo "Strict mode: missing current-run target evidence is a failure." >&2
    exit 2
fi

exit 0
