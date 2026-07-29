#!/usr/bin/env bash

# Scans BenchmarkDotNet -report-full.json files under the given root and asserts
# the release performance gates:
#
#   IdentifierQuoting  DelimitStringPlain          mean  <= 0.5   (>= 2x faster vs naive)
#   BulkInsert         MultiRowAddRangeSaveChanges mean  <= 0.333 (>= 3x faster vs per-row)
#   JsonComparer       JsonElementEqualsLoop       alloc <= 0.2   (>= 80% alloc reduction)
#   QueryTranslation   TranslateRepresentativeCorpus alloc <= 163840 bytes
#
# Ratio gates compare a gated benchmark method against the [Benchmark(Baseline=true)]
# method declared in the same class. The query-translation gate uses an absolute
# allocation ceiling because a synthetic slower translation is not a representative
# control. Mean is parsed from Statistics.Mean (nanoseconds); alloc is parsed from
# Memory.BytesAllocatedPerOperation.
#
# Inputs:
#   $1                                Benchmark artifacts root (default artifacts/benchmarks).
#   DOKA_BENCHMARK_GATE_STRICT        When set to 1, missing benchmark data for any gate
#                                     fails the script. Default 0 (warn, skip missing).
#   DOKA_BENCHMARK_GATE_RUN_ID        When set, only reports below a matching
#                                     reports/<run-id>/results directory are evaluated.
#
# Exit codes:
#   0   All gates met OR all gates skipped with strict=0.
#   1   At least one gate failed its threshold.
#   2   Misconfiguration (root missing, no reports, strict=1 + missing data).

set -euo pipefail

benchmarks_root="${1:-artifacts/benchmarks}"
strict="${DOKA_BENCHMARK_GATE_STRICT:-0}"
gate_run_id="${DOKA_BENCHMARK_GATE_RUN_ID:-}"

if [[ ! -d "${benchmarks_root}" ]]; then
    echo "Benchmark artifacts root '${benchmarks_root}' does not exist or is not a directory." >&2
    exit 2
fi

if [[ -n "${gate_run_id}" && ! "${gate_run_id}" =~ ^[0-9A-Za-z._-]+$ ]]; then
    echo "Benchmark gate run ID '${gate_run_id}' contains unsupported characters." >&2
    exit 2
fi

# POSIX find is used over rg / fd because the GitHub Actions ubuntu runner does not
# preinstall either; find is part of coreutils and always available. The same rationale
# applies in eng/check-coverage-threshold.sh.
if [[ -n "${gate_run_id}" ]]; then
    reports="$(
        find "${benchmarks_root}" \
            -type f \
            -path "*/reports/${gate_run_id}/results/*-report-full.json" \
            | sort -u
    )"
else
    reports="$(find "${benchmarks_root}" -type f -name '*-report-full.json' | sort -u)"
fi

if [[ -z "${reports}" ]]; then
    if [[ -n "${gate_run_id}" ]]; then
        echo "No BenchmarkDotNet -report-full.json files found for run '${gate_run_id}' below '${benchmarks_root}'." >&2
    else
        echo "No BenchmarkDotNet -report-full.json files found below '${benchmarks_root}'." >&2
    fi

    exit 2
fi

# (class, baseline_method, gated_method, metric, threshold_max)
# metric: mean = Statistics.Mean (ns), alloc = Memory.BytesAllocatedPerOperation (bytes).
# threshold_max: gate passes when ratio = gated/baseline <= threshold_max.
gates=(
    "IdentifierQuotingBenchmark|NaiveDelimitStringPlain|DelimitStringPlain|mean|0.5"
    "BulkInsertBenchmark|PerRowSaveChanges|MultiRowAddRangeSaveChanges|mean|0.333"
    "JsonComparerBenchmark|NaiveJsonElementEqualsLoop|JsonElementEqualsLoop|alloc|0.2"
)

# (class, gated_method, metric, threshold_max)
# Absolute gates pass when the measured value is <= threshold_max.
absolute_gates=(
    "QueryTranslationBenchmarks|TranslateRepresentativeCorpus|alloc|163840"
)

required_targets=(
    "mysql84"
    "mariadb118"
)

overall_failures=0
overall_skips=0
overall_passes=0

while IFS= read -r report; do
    [[ -z "${report}" ]] && continue

    # Per-report parse: emit one TSV line per (Type, Method) with mean + alloc + 1.0 marker.
    parsed="$(python3 -c '
import sys
import json

with open(sys.argv[1], "r", encoding="utf-8") as fh:
    payload = json.load(fh)

for benchmark in payload.get("Benchmarks", []):
    type_name = benchmark.get("Type", "")
    method = benchmark.get("Method", "")
    stats = benchmark.get("Statistics", {}) or {}
    mem = benchmark.get("Memory", {}) or {}
    mean = stats.get("Mean", 0.0)
    alloc = mem.get("BytesAllocatedPerOperation", 0)
    print(f"{type_name}\t{method}\t{mean}\t{alloc}")
' "${report}")"

    for entry in "${gates[@]}"; do
        IFS='|' read -r class baseline gated metric threshold <<< "${entry}"

        baseline_value=""
        gated_value=""
        while IFS=$'\t' read -r type_name method mean_value alloc_value; do
            if [[ "${type_name}" != *".${class}" && "${type_name}" != "${class}" ]]; then
                continue
            fi
            if [[ "${metric}" == "mean" ]]; then
                value="${mean_value}"
            else
                value="${alloc_value}"
            fi
            if [[ "${method}" == "${baseline}" ]]; then
                baseline_value="${value}"
            elif [[ "${method}" == "${gated}" ]]; then
                gated_value="${value}"
            fi
        done <<< "${parsed}"

        if [[ -z "${baseline_value}" || -z "${gated_value}" ]]; then
            continue
        fi

        report_label="${report#${benchmarks_root%/}/}"

        if awk -v b="${baseline_value}" 'BEGIN { exit (b + 0 == 0) ? 0 : 1 }'; then
            echo "[${class}] ${report_label}: baseline ${baseline} reports zero ${metric}; cannot compute ratio." >&2
            overall_failures=$(( overall_failures + 1 ))
            continue
        fi

        ratio="$(awk -v g="${gated_value}" -v b="${baseline_value}" 'BEGIN { printf "%.4f", g / b }')"
        pass="$(awk -v r="${ratio}" -v t="${threshold}" 'BEGIN { print (r + 0 <= t + 0) ? 1 : 0 }')"

        if [[ "${pass}" -eq 1 ]]; then
            echo "PASS [${class}] ${report_label}: ${gated} / ${baseline} ${metric}-ratio = ${ratio} <= ${threshold}"
            overall_passes=$(( overall_passes + 1 ))
        else
            echo "FAIL [${class}] ${report_label}: ${gated} / ${baseline} ${metric}-ratio = ${ratio} > ${threshold}" >&2
            overall_failures=$(( overall_failures + 1 ))
        fi
    done

    for entry in "${absolute_gates[@]}"; do
        IFS='|' read -r class gated metric threshold <<< "${entry}"

        gated_value=""
        while IFS=$'\t' read -r type_name method mean_value alloc_value; do
            if [[ "${type_name}" != *".${class}" && "${type_name}" != "${class}" ]]; then
                continue
            fi
            if [[ "${metric}" == "mean" ]]; then
                value="${mean_value}"
            else
                value="${alloc_value}"
            fi
            if [[ "${method}" == "${gated}" ]]; then
                gated_value="${value}"
            fi
        done <<< "${parsed}"

        if [[ -z "${gated_value}" ]]; then
            continue
        fi

        report_label="${report#${benchmarks_root%/}/}"
        pass="$(awk -v g="${gated_value}" -v t="${threshold}" 'BEGIN { print (g + 0 <= t + 0) ? 1 : 0 }')"

        if [[ "${pass}" -eq 1 ]]; then
            echo "PASS [${class}] ${report_label}: ${gated} ${metric} = ${gated_value} <= ${threshold}"
            overall_passes=$(( overall_passes + 1 ))
        else
            echo "FAIL [${class}] ${report_label}: ${gated} ${metric} = ${gated_value} > ${threshold}" >&2
            overall_failures=$(( overall_failures + 1 ))
        fi
    done
done <<< "${reports}"

report_contains_methods() {
    local report="$1"
    local class="$2"
    local baseline="$3"
    local gated="$4"

    python3 -c '
import sys, json
with open(sys.argv[1], "r", encoding="utf-8") as fh:
    payload = json.load(fh)
target = sys.argv[2]
required = [method for method in sys.argv[3:] if method]
methods = {
    benchmark.get("Method")
    for benchmark in payload.get("Benchmarks", [])
    if benchmark.get("Type", "").endswith("." + target)
    or benchmark.get("Type") == target
}
sys.exit(0 if all(method in methods for method in required) else 1)
' "${report}" "${class}" "${baseline}" "${gated}" 2>/dev/null
}

# Strict evidence is target-scoped. A complete report from one engine must never
# conceal a missing scenario on the other supported benchmark target.
presence_gates=("${gates[@]}")
for entry in "${absolute_gates[@]}"; do
    IFS='|' read -r class gated metric threshold <<< "${entry}"
    presence_gates+=("${class}||${gated}|${metric}|${threshold}")
done

gates_total=$(( ${#presence_gates[@]} * ${#required_targets[@]} ))
for benchmark_target in "${required_targets[@]}"; do
    for entry in "${presence_gates[@]}"; do
        IFS='|' read -r class baseline gated metric threshold <<< "${entry}"
        found=0

        while IFS= read -r report; do
            [[ -z "${report}" ]] && continue
            relative_report="${report#${benchmarks_root%/}/}"
            [[ "${relative_report}" != "${benchmark_target}/"* ]] && continue

            if report_contains_methods "${report}" "${class}" "${baseline}" "${gated}"; then
                found=1
                break
            fi
        done <<< "${reports}"

        if [[ "${found}" -ne 1 ]]; then
            overall_skips=$(( overall_skips + 1 ))
            echo "SKIP [${class}] ${benchmark_target}: required method data is missing." >&2
        fi
    done
done

echo
printf 'Benchmark gate summary: %s pass, %s fail, %s gate(s) without data (of %s configured).\n' \
    "${overall_passes}" \
    "${overall_failures}" \
    "${overall_skips}" \
    "${gates_total}"

if [[ "${overall_failures}" -gt 0 ]]; then
    exit 1
fi

if [[ "${overall_skips}" -gt 0 && "${strict}" == "1" ]]; then
    echo "Strict mode: missing data is a failure." >&2
    exit 2
fi

exit 0
