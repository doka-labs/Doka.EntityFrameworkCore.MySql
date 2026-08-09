#!/usr/bin/env bash

# A release tag is immutable: once pushed it can never be moved, replaced, or
# reused. Every defect the hosted candidate finds therefore costs a version
# number, and the qualification path is long enough that defects surface late.
# This rehearsal runs the same orchestrator against the working commit without
# a tag, so a defect costs a local run instead.
#
# It is not an approval. A green rehearsal says the gates it ran pass on this
# commit with these tools; the hosted candidate still runs on its own runners.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
baseline_file="${repo_root}/benchmarks/baselines/doka-benchmark-baseline.json"

usage() {
    cat >&2 <<'USAGE'
Usage: eng/rehearse-release.sh <version> [--stage <stage>]

  <version>   The package version the real candidate will carry, without the
              leading "v". Example: 10.0.0-rc.6

  --stage     Rehearse one stage instead of the whole path. Stages:
              quality, repository-tests, specification, integration,
              migration-deployment, runtime, coverage, package, sbom.

The performance stages compare against an accepted baseline recorded for their
own runner class. A workstation is a different runner class than the hosted
matrix, so a full rehearsal reaches the comparison and stops there. Rehearse
the remaining stages individually when that happens.

Environment:
  Only these are read from the environment. Every other DOKA_RELEASE_* and
  DOKA_BENCHMARK_* variable is removed before the orchestrator runs, so state
  left over from an earlier run cannot change what a rehearsal answers.

  DOKA_RELEASE_CANDIDATE_RUN_ID   Share one evidence directory across stages.
  DOKA_RELEASE_CANDIDATE_RESUME   Continue into that directory (set to 1).
  DOKA_BENCHMARK_RUNNER_CLASS     Override the detected runner class.
USAGE
}

if [[ "$#" -lt 1 ]]; then
    usage
    exit 2
fi

case "$1" in
    -h | --help)
        usage
        exit 0
        ;;
esac

release_version="$1"
shift

if [[ ! "${release_version}" =~ ^[0-9]+[.][0-9]+[.][0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
    echo "Version '${release_version}' is not a semantic version." >&2
    echo "Pass it without the leading 'v', for example 10.0.0-rc.6." >&2
    exit 2
fi

requested_stage=""
if [[ "${1:-}" == "--stage" ]]; then
    requested_stage="${2:-}"
fi

if [[ -n "$(git -C "${repo_root}" status --porcelain --untracked-files=all)" ]]; then
    echo "A rehearsal needs a clean worktree so it qualifies the commit a tag" >&2
    echo "would point at, not uncommitted changes." >&2
    exit 1
fi

rehearsal_commit="$(git -C "${repo_root}" rev-parse --short HEAD)"
scope="${requested_stage:-the full path}"

echo "Rehearsing release candidate ${release_version} on ${rehearsal_commit} (${scope})."
echo "No tag is created and nothing is published."

# Say up front what the accepted baseline can and cannot answer here, because
# the comparison runs after every workload has been measured. Learning it at
# that point costs the whole measurement.
runner_class="${DOKA_BENCHMARK_RUNNER_CLASS:-local-$(uname -s | tr '[:upper:]' '[:lower:]')-$(uname -m)}"
if [[ -z "${requested_stage}" ]] && [[ -f "${baseline_file}" ]]; then
    if ! grep -q "\"runnerClass\": \"${runner_class}\"" "${baseline_file}"; then
        cat <<EOF

The accepted baseline holds no entry for runner class '${runner_class}', so the
performance stages will measure every workload and then stop at the comparison.
That part of the path can only be qualified by the hosted candidate. To cover
the rest, rehearse the other stages individually:

  eng/rehearse-release.sh ${release_version} --stage quality
EOF
    fi
fi
echo

# A rehearsal answers one question: do the gates accept this commit. Anything
# the orchestrator reads from the environment can change that answer, and a
# shell that ran an earlier rehearsal still holds its state: a run identifier,
# a resume flag, a foreign baseline path, or the deadline marker that keeps the
# orchestrator from arming its own timeout. Those are not inputs a rehearsal
# accepts; they are leftovers. Only the variables below survive, and every
# other release or benchmark variable is removed before the orchestrator runs.
rehearsal_inputs=(
    DOKA_RELEASE_CANDIDATE_RUN_ID
    DOKA_RELEASE_CANDIDATE_RESUME
    DOKA_BENCHMARK_RUNNER_CLASS
)

while IFS='=' read -r inherited_name _; do
    for supported_name in "${rehearsal_inputs[@]}"; do
        if [[ "${inherited_name}" == "${supported_name}" ]]; then
            continue 2
        fi
    done
    unset "${inherited_name}"
done < <(env | grep -E '^DOKA_(RELEASE|BENCHMARK)_' || true)

# The orchestrator derives its version from the tag at HEAD. Without one it
# needs the version explicitly, or it would pack the bare VersionPrefix and
# qualify a package the real candidate never produces.
export DOKA_RELEASE_REQUIRE_TAG=0
export DOKA_RELEASE_VERSION="${release_version}"
export DOKA_RELEASE_RUNNER_IDENTITY=local-rehearsal

status=0
"${repo_root}/eng/release/release-candidate.sh" "$@" || status=$?

echo
if [[ "${status}" -ne 0 ]]; then
    cat <<EOF
Rehearsal failed for ${release_version} on ${rehearsal_commit} (${scope}, exit ${status}).

Fix the reported gate and rehearse again. No tag was spent on this attempt.
EOF
elif [[ -n "${requested_stage}" ]]; then
    cat <<EOF
Stage '${requested_stage}' passed for ${release_version} on ${rehearsal_commit}.

This covers one stage. The remaining stages, and the performance comparison
against the hosted baseline, are still unproven for this commit.
EOF
else
    cat <<EOF
Rehearsal passed for ${release_version} on ${rehearsal_commit}.

Every gate this run covers accepts this commit. Creating the tag is the next
step; the hosted candidate then repeats these gates on its own runners.
EOF
fi

exit "${status}"
