#!/usr/bin/env bash

# Measure a reference and a candidate provider revision under one benchmark
# driver, alternating them block by block on a single allocated machine.
#
# The reference side is built by packing the provider from its accepted
# reference commit and binding the candidate driver to that package. Building
# the reference side from its own commit would rebuild the driver as well, and
# the comparison would silently be between driver-and-provider pairs rather
# than between providers.

set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
performance_contract="${DOKA_BENCHMARK_CONTRACT_PATH:-${repo_root}/benchmarks/performance-contract.json}"
baseline_manifest="${repo_root}/benchmarks/baselines/doka-benchmark-baseline.json"
benchmark_project="${repo_root}/benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks"
benchmark_project+="/Doka.EntityFrameworkCore.MySql.Benchmarks.csproj"
INVALID_EVIDENCE_EXIT_CODE=78

driver_compatibility_only=0
if (( $# > 1 )); then
    echo "Usage: paired-benchmark.sh [--verify-driver-compatibility]" >&2
    exit "${INVALID_EVIDENCE_EXIT_CODE}"
fi
if (( $# == 1 )); then
    if [[ "$1" != "--verify-driver-compatibility" ]]; then
        echo "Unknown paired benchmark option '$1'." >&2
        exit "${INVALID_EVIDENCE_EXIT_CODE}"
    fi

    driver_compatibility_only=1
fi

if (( driver_compatibility_only == 1 )); then
    benchmark_target="${DOKA_BENCHMARK_TARGET:-mysql84}"
    run_id="${DOKA_BENCHMARK_RUN_ID:-paired-driver-compatibility-$$}"
else
    benchmark_target="${DOKA_BENCHMARK_TARGET:?DOKA_BENCHMARK_TARGET is required}"
    run_id="${DOKA_BENCHMARK_RUN_ID:?DOKA_BENCHMARK_RUN_ID is required}"
fi
candidate_commit="${DOKA_BENCHMARK_COMMIT:-$(git -C "${repo_root}" rev-parse HEAD)}"
reference_commit="${DOKA_PAIRED_REFERENCE_COMMIT:-}"
# The attempt machinery binds a receipt to the evidence by identity, so the
# same identity the caller established has to reach the evidence document.
source_hash="${DOKA_BENCHMARK_SOURCE_HASH:-}"
runner_class="${DOKA_BENCHMARK_RUNNER_CLASS:-}"
if (( driver_compatibility_only == 0 )); then
    source_hash="${source_hash:?DOKA_BENCHMARK_SOURCE_HASH is required}"
    runner_class="${runner_class:?DOKA_BENCHMARK_RUNNER_CLASS is required}"
fi

# shellcheck source=eng/performance/host-preflight.sh
source "${repo_root}/eng/performance/host-preflight.sh"

command -v jq >/dev/null 2>&1 || {
    echo "jq is required to orchestrate a paired comparison." >&2
    exit "${INVALID_EVIDENCE_EXIT_CODE}"
}

# Both identity components are validated before they are used to build a path
# that is then deleted. A target or run identifier carrying a slash or a
# traversal segment would otherwise let the cleanup below reach outside the
# artifact tree.
for identity in "benchmark_target:${benchmark_target}" "run_id:${run_id}"; do
    name="${identity%%:*}"
    value="${identity#*:}"
    if [[ ! "${value}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
        echo "${name} '${value}' is not a safe path segment." >&2
        exit "${INVALID_EVIDENCE_EXIT_CODE}"
    fi
done

# Scratch and evidence are separated on purpose. The reference worktree, the
# local feed, and the two published drivers are build inputs that no consumer
# should have to skip past; the evidence lands in the report directory the
# attempt machinery already collects, so a paired run is uploaded, selected,
# and imported by exactly the same path a historical one is.
work_root="${repo_root}/artifacts/paired/${benchmark_target}/${run_id}"
report_dir="${repo_root}/artifacts/benchmarks/${benchmark_target}/reports/${run_id}"
feed_dir="${work_root}/reference-feed"
worktree_dir="${work_root}/reference-source"
# The block measurements and their identity files are the raw evidence behind
# every ratio. They live in the report directory rather than in the scratch
# tree so they travel with the selected attempt and reach the release
# candidate; a digest can only bind files that were retained.
blocks_dir="${report_dir}/blocks"
soak_file="${report_dir}/paired-soak.json"
evidence_file="${report_dir}/paired-evidence.json"
evaluation_file="${report_dir}/paired-evaluation.json"

# The policy is the single source for how many blocks are measured and which
# profile one block uses. Reading it here keeps the orchestration and the
# evaluation from drifting apart.
block_profile="$(jq -er '.pairedPolicy.blocks.profile' "${performance_contract}")"
# The execution order comes from the contract rather than from a loop written
# here. A registered plan nobody executes is a claim, not a control.
# Built with a read loop rather than mapfile so this behaves identically on the
# macOS system Bash 3.2 that contributors run locally.
block_patterns=()
while IFS= read -r block_pattern; do
    block_patterns+=("${block_pattern}")
done < <(
    jq -er '.pairedPolicy.executionOrder.blockPatterns[]' "${performance_contract}"
)
registered_blocks="$(jq -er '.pairedPolicy.blocks.completeBlocks' "${performance_contract}")"
block_count="${DOKA_PAIRED_BLOCKS:-${registered_blocks}}"

if [[ ! "${block_count}" =~ ^[0-9]+$ ]] || (( block_count < 1 )); then
    echo "Block count '${block_count}' is not a positive number." >&2
    exit "${INVALID_EVIDENCE_EXIT_CODE}"
fi
if (( block_count > registered_blocks )); then
    echo "Block count '${block_count}' is above the registered fixed count" \
        "${registered_blocks}." >&2
    exit "${INVALID_EVIDENCE_EXIT_CODE}"
fi
if (( block_count < registered_blocks )); then
    # Deliberately permitted so the orchestration can be exercised without
    # paying for a full comparison. The evaluation still requires the exact
    # registered count, so a short run can prove the plumbing and can never
    # produce a release verdict.
    echo "Measuring ${block_count} of the ${registered_blocks} blocks the policy" \
        "registers. This run cannot qualify a release." >&2
fi

if [[ -z "${reference_commit}" ]]; then
    reference_commit="$(jq -er --arg target "${benchmark_target}" '
        .baselines[] | select(.target == $target) | .referenceCommit // .commit
    ' "${baseline_manifest}")"
fi

if ! git -C "${repo_root}" merge-base --is-ancestor \
        "${reference_commit}" "${candidate_commit}"; then
    echo "Reference commit ${reference_commit} is not an ancestor of the candidate." >&2
    exit 77
fi

# The engine image belongs to the paired identity: both sides must meet the
# same server build, and the environment validator compares the recorded image
# across the two sides. Leaving it unset would let the driver record an empty
# image on both sides, and a comparison across engine builds would pass a
# check that was never actually made.
server_image="${DOKA_BENCHMARK_SERVER_IMAGE:-}"
if (( driver_compatibility_only == 0 )); then
    server_image="${server_image:?DOKA_BENCHMARK_SERVER_IMAGE is required}"
    if [[ ! "${server_image}" =~ @sha256:[0-9a-f]{64}$ ]]; then
        echo "Server image '${server_image}' is not pinned by digest." >&2
        exit "${INVALID_EVIDENCE_EXIT_CODE}"
    fi

    export DOKA_BENCHMARK_SERVER_IMAGE="${server_image}"
fi

# Exit 75 is the registered measurement-quality code: the run reached no
# verdict about the provider and one bounded retry may follow.
MEASUREMENT_QUALITY_EXIT_CODE=75
# The deadline helper's own timeout signal.
DEADLINE_EXIT_CODE=124

run_started_at="$(date +%s)"
paired_run_seconds="$(
    jq -er '.pairedPolicy.durations.maximumPairedRunSeconds' "${performance_contract}"
)"
side_watchdog_seconds="$(
    jq -er --arg profile "${block_profile}" \
        '.profiles[$profile].maximumTotalDurationSeconds' "${performance_contract}"
)"
side_durations=()
# Reserved for the work that follows the last block: the candidate soak, the
# evidence assembly, and the evaluation. Registered in the contract rather than
# taken from the environment, so the value a run used is reviewable and cannot
# be lowered from outside to admit a block that will not close out.
closing_reserve_seconds="$(
    jq -er '.pairedPolicy.durations.closingReserveSeconds' "${performance_contract}"
)"
# The share of the closing reserve that belongs to assembling and evaluating
# the evidence rather than to the sustained-use run.
finalization_reserve_seconds="$(
    jq -er '.pairedPolicy.durations.finalizationReserveSeconds' "${performance_contract}"
)"

cleanup() {
    git -C "${repo_root}" worktree remove --force "${worktree_dir}" 2>/dev/null || true

    if (( driver_compatibility_only == 1 )); then
        rm -rf "${work_root}" "${report_dir}"
    fi
}
trap cleanup EXIT

classify_unhandled_error() {
    local status=$?

    if (( status == 1 )); then
        trap - ERR
        echo "Paired benchmark infrastructure failed before producing a verdict." >&2
        exit "${INVALID_EVIDENCE_EXIT_CODE}"
    fi

    return "${status}"
}
trap classify_unhandled_error ERR

rm -rf "${work_root}"
mkdir -p "${feed_dir}" "${blocks_dir}" "${report_dir}"

# The reference provider is packed from its own commit; only the provider is
# taken from there, never the driver or the contract.
git -C "${repo_root}" worktree add --detach --quiet "${worktree_dir}" "${reference_commit}"
# The version carries the reference commit. A fixed identifier would let the
# global package cache hand a later run the provider from an earlier reference
# revision, and nothing downstream could tell the two apart.
reference_version="0.0.0-paired-${reference_commit:0:12}"
for project in Doka.EntityFrameworkCore.MySql Doka.EntityFrameworkCore.MySql.NetTopologySuite; do
    dotnet pack "${worktree_dir}/src/${project}/${project}.csproj" \
        --configuration Release \
        --tl:off \
        -p:Version="${reference_version}" \
        -p:ContinuousIntegrationBuild=true \
        --output "${feed_dir}"
done

candidate_output="${work_root}/driver-candidate"
reference_output="${work_root}/driver-reference"

# Each side builds under its own artifacts root. Sharing the repository's root
# lets the reference restore leave PackageReferences behind that a later
# ordinary build imports alongside the project references, failing with CS1704
# until someone restores again; the repository build must not depend on whether
# a paired comparison ran first.
#
# ArtifactsPath rather than BaseIntermediateOutputPath: the latter is a global
# property, so it applies to the benchmark project AND every project reference
# it pulls in, collapsing the provider, the spatial package, and the driver
# into one intermediate directory where their restore output overwrites each
# other. This repository already sets IncludeProjectNameInArtifactsPaths, so an
# artifacts root keeps the per-project separation the build needs.
candidate_artifacts="${work_root}/artifacts-candidate"
reference_artifacts="${work_root}/artifacts-reference"
mkdir -p "${candidate_artifacts}" "${reference_artifacts}"

dotnet publish "${benchmark_project}" \
    --configuration Release --tl:off --output "${candidate_output}" \
    -p:DokaBenchmarkCrossVersionDriver=true \
    -p:ArtifactsPath="${candidate_artifacts}"

# Same driver source, same contract, provider swapped for the packaged
# reference. The local feed is added to the configured sources rather than
# replacing them, so the check below is what actually proves the provider came
# from the packages this run just built.
dotnet publish "${benchmark_project}" \
    --configuration Release --tl:off --output "${reference_output}" \
    -p:DokaBenchmarkProviderVersion="${reference_version}" \
    -p:RestoreAdditionalProjectSources="${feed_dir}" \
    -p:ArtifactsPath="${reference_artifacts}"

# Prove the reference side is actually the reference provider. A restore that
# silently resolved the candidate version would produce a comparison of a
# revision against itself, which reads as a perfectly healthy result.
for assembly in Doka.EntityFrameworkCore.MySql Doka.EntityFrameworkCore.MySql.NetTopologySuite; do
    published="${reference_output}/${assembly}.dll"
    if [[ ! -f "${published}" ]]; then
        echo "The reference side published no ${assembly}.dll." >&2
        exit "${INVALID_EVIDENCE_EXIT_CODE}"
    fi
    packed="${feed_dir}/${assembly}.${reference_version}.nupkg"
    if [[ ! -f "${packed}" ]]; then
        echo "No packed reference for ${assembly} at ${reference_version}." >&2
        exit "${INVALID_EVIDENCE_EXIT_CODE}"
    fi
    if ! cmp -s \
        <(unzip -p "${packed}" "lib/*/${assembly}.dll") \
        "${published}"; then
        echo "The reference side did not publish the ${assembly} this run packed." >&2
        exit "${INVALID_EVIDENCE_EXIT_CODE}"
    fi
done

if (( driver_compatibility_only == 1 )); then
    echo "Paired benchmark driver is compatible with reference ${reference_commit}."
    exit 0
fi

# Both sides are published from the candidate project on purpose: only the
# provider is swapped. Each side nevertheless records the working tree it was
# actually published from, so the fact is proven rather than assumed. A change
# that published the reference side out of the reference worktree would move
# its recorded driver tree and be rejected as invalid evidence instead of
# quietly comparing driver-and-provider pairs.
candidate_project="${benchmark_project}"
reference_project="${benchmark_project}"

benchmarks_tree_hash() {
    # The committed tree is only the right answer when it is what was built.
    # A working tree with uncommitted benchmark changes publishes something
    # else, and recording the commit's tree for it would put a value in the
    # evidence that describes a driver nobody ran.
    local project="$1" root dirt
    root="$(git -C "$(dirname "${project}")" rev-parse --show-toplevel)"
    dirt="$(git -C "${root}" status --porcelain --untracked-files=all -- benchmarks)"
    if [[ -z "${dirt}" ]]; then
        git -C "${root}" rev-parse HEAD:benchmarks
        return 0
    fi
    # Tracked changes reach the digest through the diff. Untracked files do
    # not appear in a diff at all, so `git status` alone would fold in their
    # names and never their contents: two runs with the same new file name and
    # different contents would claim the same driver.
    # A full SHA-256, in the same shape a committed tree identifier has. The
    # earlier `worktree-<16 hex>` form was rejected by this repository's own
    # evidence validator, so a local paired run would have measured for an hour
    # and then discarded the evidence it had just produced. The full digest is
    # also the stronger identity: sixteen hex characters are sixty-four bits.
    {
        git -C "${root}" rev-parse HEAD:benchmarks
        git -C "${root}" diff HEAD -- benchmarks
        while IFS= read -r untracked; do
            [[ -z "${untracked}" ]] && continue
            printf '%s ' "${untracked}"
            shasum -a 256 "${root}/${untracked}" | cut -d' ' -f1
        done < <(
            git -C "${root}" ls-files --others --exclude-standard -- benchmarks
        )
    } | shasum -a 256 | cut -d' ' -f1 | tr -d '\n'
}

contract_digest_of() {
    # The digest is of the contract this run actually loaded, not of the path
    # the release happens to ship. Hashing the shipped file while measuring
    # under an overridden policy would let the evidence claim a policy nobody
    # applied.
    shasum -a 256 "${performance_contract}" | cut -d' ' -f1
}

driver_source_hash="$(benchmarks_tree_hash "${candidate_project}")"
contract_digest="$(contract_digest_of)"

# After the builds and before the first measurement, exactly as the historical
# run does it: build activity stays outside the host-admission boundary, and
# the driver cannot assemble a report without this identity.
capture_host_preflight \
    "${report_dir}/host-preflight.json" "${performance_contract}"

# The watchdogs are hierarchical and none of them is a reserved budget. A side
# run stops at whichever comes first: its own hang deadline, or what is left of
# the whole comparison. That distinction matters because a side that stayed
# inside its local deadline is still not a valid run once the comparison has
# spent its budget.
remaining_budget() {
    local elapsed=$(( $(date +%s) - run_started_at ))
    local remaining=$(( paired_run_seconds - elapsed ))
    (( remaining < 0 )) && remaining=0
    printf '%s' "${remaining}"
}

run_side() {
    local side="$1" output="$2" block="$3" project="$4"
    local measurement="${blocks_dir}/block-${block}-${side}.json"
    local remaining effective started

    # The reserve is withheld from the measuring phase rather than only
    # consulted before it. A side that consumed the whole remaining budget
    # would leave nothing for the closing work, which is the same failure the
    # forecast prevents between blocks.
    remaining=$(( $(remaining_budget) - closing_reserve_seconds ))
    (( remaining < 0 )) && remaining=0
    effective="${side_watchdog_seconds}"
    (( remaining < effective )) && effective="${remaining}"
    if (( effective <= 0 )); then
        echo "The paired run budget of ${paired_run_seconds}s is exhausted." >&2
        exit "${MEASUREMENT_QUALITY_EXIT_CODE}"
    fi

    started="$(date +%s)"
    # The repository's own deadline helper owns a process group, so cutting a
    # side off takes BenchmarkDotNet and its database clients with it rather
    # than leaving them behind holding the engine.
    DOKA_BENCHMARK_PROFILE="${block_profile}" \
    DOKA_BENCHMARK_TARGET="${benchmark_target}" \
        python3 -m eng.common.deadline \
            --seconds "${effective}" \
            --label "paired ${side} side of block ${block}" \
            -- dotnet "${output}/Doka.EntityFrameworkCore.MySql.Benchmarks.dll" \
                --workloads "${measurement}" || {
        local status=$?
        # A run cut off by a watchdog produced no verdict about the provider,
        # so it leaves as a measurement condition and stays retryable.
        if (( status == DEADLINE_EXIT_CODE )); then
            echo "Side ${side} of block ${block} exceeded ${effective}s." >&2
            exit "${MEASUREMENT_QUALITY_EXIT_CODE}"
        fi
        if (( status == 1 )); then
            echo "Side ${side} of block ${block} failed before producing a verdict." >&2
            exit "${INVALID_EVIDENCE_EXIT_CODE}"
        fi
        exit "${status}"
    }
    side_durations+=("$(( $(date +%s) - started ))")

    jq -n \
        --arg driver "$(benchmarks_tree_hash "${project}")" \
        --arg contract "$(contract_digest_of "${project}")" \
        '{benchmarkDriverSourceHash: $driver, contractDigest: $contract}' \
        > "${blocks_dir}/block-${block}-${side}.identity.json"
}

# `A` is the reference and `B` the candidate. The pattern for a block is taken
# from the registered list in order, so the starting side alternates exactly as
# the contract declares and any warm-up advantage the first side enjoys cancels
# across the run instead of accruing to one provider.
side_for() {
    case "$1" in
        A) printf 'reference' ;;
        B) printf 'candidate' ;;
        *)
            echo "Unknown execution-order side '$1'." >&2
            exit "${INVALID_EVIDENCE_EXIT_CODE}"
            ;;
    esac
}

output_for() {
    case "$1" in
        reference) printf '%s' "${reference_output}" ;;
        candidate) printf '%s' "${candidate_output}" ;;
    esac
}

project_for() {
    case "$1" in
        reference) printf '%s' "${reference_project}" ;;
        candidate) printf '%s' "${candidate_project}" ;;
    esac
}

# Recorded as it happens, one entry per block, from the side that was actually
# measured. Writing the planned pattern instead would produce an artifact that
# describes the plan rather than the run -- which is what it did.
executed_patterns=()
for ((block = 1; block <= block_count; block++)); do
    # Before spending a runner on another block, ask whether it can finish.
    # The forecast comes from the blocks already measured rather than from a
    # ceiling, so it describes this machine on this day. Stopping here yields a
    # measurement condition and a retry; running into the outer deadline would
    # yield a killed job and no evidence at all.
    if (( ${#side_durations[@]} > 0 )); then
        total=0
        for duration in "${side_durations[@]}"; do
            total=$(( total + duration ))
        done
        projected=$(( total * ${#order[@]} / ${#side_durations[@]} ))
        # The blocks are not the last thing the budget has to cover: the
        # candidate soak, assembling the evidence, and evaluating it all run
        # afterwards and all sit inside the same deadline. Admitting a final
        # block that leaves no room for them would spend the budget and still
        # produce nothing.
        if (( projected + closing_reserve_seconds > $(remaining_budget) )); then
            echo "Block ${block} needs about ${projected}s plus" \
                "${closing_reserve_seconds}s to close out, and $(remaining_budget)s" \
                "remain of the ${paired_run_seconds}s paired budget." >&2
            echo "Completed ${#executed_patterns[@]} of ${block_count} blocks." >&2
            exit "${MEASUREMENT_QUALITY_EXIT_CODE}"
        fi
    fi

    pattern="${block_patterns[$(( (block - 1) % ${#block_patterns[@]} ))]}"
    IFS='-' read -r -a order <<< "${pattern}"
    executed_sides=()
    for token in "${order[@]}"; do
        side="$(side_for "${token}")"
        run_side "${side}" "$(output_for "${side}")" "${block}" "$(project_for "${side}")"
        executed_sides+=("${token}")
    done
    executed_patterns+=("$(IFS='-'; printf '%s' "${executed_sides[*]}")")
done

# The order that ran is written next to the measurements and travels with them,
# so the evidence can be checked against the contract instead of being trusted
# to match it.
printf '%s\n' "${executed_patterns[@]}" \
    | jq -R . \
    | jq -s --arg profile "${block_profile}" \
        '{executedBlockPatterns: ., blockProfile: $profile}' \
        > "${report_dir}/execution-order.json"

# Sustained use is measured once on the candidate rather than once per block.
# A leak appears over thousands of iterations, so repeating it per block would
# multiply the cost without adding a signal the single run does not carry.
# The soak gets the remaining budget minus the finalization share: assembling
# the evidence and evaluating it come after it and are inside the same
# deadline. Handing the soak everything that is left would let it consume the
# reserve the forecast withheld for exactly this work.
soak_deadline=$(( $(remaining_budget) - finalization_reserve_seconds ))
if (( soak_deadline <= 0 )); then
    echo "No time remains for the sustained-use run once the" \
        "${finalization_reserve_seconds}s finalization reserve is withheld." >&2
    exit "${MEASUREMENT_QUALITY_EXIT_CODE}"
fi

DOKA_BENCHMARK_PROFILE="${block_profile}" \
DOKA_BENCHMARK_TARGET="${benchmark_target}" \
    python3 -m eng.common.deadline \
        --seconds "${soak_deadline}" \
        --label "paired sustained-use run" \
        -- dotnet "${candidate_output}/Doka.EntityFrameworkCore.MySql.Benchmarks.dll" \
            --soak "${soak_file}" || {
    status=$?
    if (( status == DEADLINE_EXIT_CODE )); then
        echo "The sustained-use run exceeded the remaining ${soak_deadline}s." >&2
        exit "${MEASUREMENT_QUALITY_EXIT_CODE}"
    fi
    if (( status == 1 )); then
        echo "The sustained-use run failed before producing a verdict." >&2
        exit "${INVALID_EVIDENCE_EXIT_CODE}"
    fi
    exit "${status}"
}

python3 -m eng.performance.cli assemble-paired-evidence \
    --blocks "${blocks_dir}" \
    --contract "${performance_contract}" \
    --target "${benchmark_target}" \
    --run-id "${run_id}" \
    --candidate-commit "${candidate_commit}" \
    --reference-commit "${reference_commit}" \
    --driver-source-hash "${driver_source_hash}" \
    --contract-digest "${contract_digest}" \
    --profile "${block_profile}" \
    --execution-order "${report_dir}/execution-order.json" \
    --source-hash "${source_hash}" \
    --runner-class "${runner_class}" \
    --soak "${soak_file}" \
    --output "${evidence_file}"

if python3 -m eng.performance.cli evaluate-paired \
        --contract "${performance_contract}" \
        --evidence "${evidence_file}" \
        --output "${evaluation_file}"; then
    :
else
    # This is the only command in the orchestration allowed to return the
    # semantic regression exit. Running it as an if-condition bypasses the ERR
    # trap that maps ordinary process failures to invalid evidence.
    status=$?
    if (( status == 1 )) \
        && ! jq -e '.qualification == "regression"' "${evaluation_file}" >/dev/null 2>&1; then
        echo "The paired evaluator returned regression without a regression document." >&2
        exit "${INVALID_EVIDENCE_EXIT_CODE}"
    fi
    exit "${status}"
fi

echo "Paired comparison for ${benchmark_target} written to ${evaluation_file}."
