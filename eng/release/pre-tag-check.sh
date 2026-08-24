#!/usr/bin/env bash

# Answer, without allocating anything, whether the current commit is ready to
# enter untagged hosted qualification and later receive the release tag.
#
# Under D-026 the hosted candidate runs before the semantic tag exists. This
# lookup proves the source has an exact-tree pull-request qualification the
# candidate imports and that the operator's configured key is registered for
# the tag that may be created only after qualification succeeds.
#
# This replaces the local rehearsal. Rehearsing the whole candidate was an
# attempt to buy certainty before spending a version number, but it could never
# cover the one gate that kept failing, and it cost more than the tag it was
# meant to protect.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
policy_file="${repo_root}/eng/release/evidence-policy.json"
protected_ref="${DOKA_RELEASE_PROTECTED_REF:-refs/remotes/origin/main}"

usage() {
    cat >&2 <<'USAGE'
Usage: eng/pre-tag-check.sh [--commit <sha>]

Reports whether the given commit, or HEAD, is ready to start hosted candidate
qualification and later receive its signed tag. Allocates no runner, changes no
file, and creates no tag.
USAGE
}

case "${1:-}" in
    -h | --help)
        usage
        exit 0
        ;;
esac

commit="$(git -C "${repo_root}" rev-parse HEAD)"
if [[ "${1:-}" == "--commit" ]]; then
    commit="$(git -C "${repo_root}" rev-parse "${2:?--commit needs a value}")"
fi

command -v jq >/dev/null 2>&1 || {
    echo "jq is required to read the evidence policy." >&2
    exit 1
}

# The trust root performs this many remote checks. A run that prints fewer has
# not answered the question, whatever its exit status says.
remote_check_count=2

failures=0
report() {
    local state="$1" subject="$2" detail="${3:-}"
    printf '  %-5s %-34s %s\n' "${state}" "${subject}" "${detail}"
    [[ "${state}" == "FAIL" ]] && failures=$((failures + 1))
    return 0
}

echo "Release preparation check for ${commit}"
echo

# A tag can name any commit. Reachability is what ties the candidate back to the
# branch whose protection produced the evidence qualification imports.
if git -C "${repo_root}" merge-base --is-ancestor "${commit}" "${protected_ref}" 2>/dev/null; then
    report OK "reachable from protected branch" "${protected_ref}"
else
    report FAIL "reachable from protected branch" \
        "push the commit to ${protected_ref} first"
fi

if [[ -n "$(git -C "${repo_root}" status --porcelain --untracked-files=all)" ]]; then
    report FAIL "worktree clean" "a tag would not describe the working tree"
else
    report OK "worktree clean"
fi

# Signing material is checked before a tag is created, because an unsigned or
# unregistered signature is rejected by the trust root after the tag exists,
# which is exactly when it is expensive.
signers="$(jq -er '.trustedTagSigners.signers | length' "${policy_file}")"
if (( signers > 0 )); then
    report OK "trusted signers registered" "${signers}"
else
    report FAIL "trusted signers registered" "eng/release/evidence-policy.json"
fi

allowed_signers_rel="$(jq -er '.trustedTagSigners.allowedSignersFile' "${policy_file}")"
if [[ -f "${repo_root}/${allowed_signers_rel}" ]]; then
    report OK "allowed-signers file present" "${allowed_signers_rel}"
else
    report FAIL "allowed-signers file present" \
        "create ${allowed_signers_rel} with the release key"
fi

# The remote half of the question shares the release trust root's
# implementation. A second, weaker copy here is how this check ended up
# accepting preparation while the later tag itself would be rejected:
# it looked at the first check run with a matching name and never at the
# event, the branch, the workflow, or the attempt behind it.
if ! command -v gh >/dev/null 2>&1; then
    report FAIL "remote qualification" \
        "gh is required; without it this check cannot answer the question"
elif ! repository="$(gh repo view --json nameWithOwner --jq .nameWithOwner 2>/dev/null)"; then
    report FAIL "remote qualification" "gh is not authenticated for this repository"
else
    # The exit status is captured separately from the output. A crash, invalid
    # output, or an unhandled exception would otherwise end the command with
    # no check lines at all, and a wrapper that only counts printed failures
    # would report that a tag qualifies because nothing said otherwise.
    trust_output=""
    trust_status=0
    trust_output="$(
        python3 -m eng.release.trust pre-tag \
            --repo "${repo_root}" \
            --commit "${commit}" \
            --repository "${repository}" \
            --policy "${policy_file}" 2>&1
    )" || trust_status=$?

    # Only well-formed records count. A traceback or a warning is not a check
    # result, and counting arbitrary output as one is how a crashed run passed
    # the completeness guard.
    printed=0
    while IFS=$'\t' read -r marker state subject detail; do
        [[ "${marker}" != "PRE-TAG" ]] && continue
        report "${state}" "${subject}" "${detail}"
        printed=$((printed + 1))
    done <<< "${trust_output}"

    if (( printed == 0 )) && [[ -n "${trust_output}" ]]; then
        printf '%s\n' "${trust_output}" | sed 's/^/    /' >&2
    fi

    # Two independent conditions, because either alone can be satisfied by a
    # run that never completed: a non-zero exit without failure lines, and a
    # clean exit that produced fewer checks than the trust root performs.
    if (( trust_status > 1 )); then
        report FAIL "remote qualification" \
            "the trust check exited ${trust_status} without completing"
    fi
    if (( printed < remote_check_count )); then
        report FAIL "remote qualification" \
            "expected ${remote_check_count} remote checks, saw ${printed}"
    fi
fi

echo
if (( failures > 0 )); then
    echo "Commit ${commit} is not ready for release qualification: ${failures} check(s) failed." >&2
    exit 1
fi

echo "Commit ${commit} is ready for untagged hosted qualification."
echo "Create the signed tag only after the candidate and attestations are green."
