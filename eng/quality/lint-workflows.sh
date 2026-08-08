#!/usr/bin/env bash

# Lints the workflow and shell surface that no compiler covers. actionlint
# validates workflow syntax, expressions, job references, and the shell inside
# run blocks; zizmor audits the same files for Actions-specific security
# patterns; shellcheck covers the engineering scripts and the Git hooks.
#
# Tool resolution has two modes, and the split is what makes the gate's result
# reproducible. Under DOKA_LINT_AUTO_INSTALL=1, which is how CI runs, only the
# pinned build is acceptable: any PATH copy is ignored, and a cached build whose
# reported version differs from the pin is discarded and refetched, so neither a
# runner image nor a stale cache can quietly replace the analyzed version.
# Downloads are digest-verified -- actionlint against the checksum recorded
# below, zizmor through pip --require-hashes against zizmor-requirements.txt.
#
# Without that variable the contributor's own installation is used, and a
# version that differs from the pin is reported so a local result that CI will
# not reproduce is visible rather than silent.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${repo_root}"

# The commit hook must stay offline and fast, so it runs the shell contract
# only. Push and CI run the complete contract including the workflow auditors.
shell_only=0
if (( $# > 1 )); then
    echo "Usage: $(basename "$0") [--shell-only]" >&2
    exit 2
fi

if (( $# == 1 )); then
    if [[ "$1" != "--shell-only" ]]; then
        echo "Unknown option '$1'." >&2
        echo "Usage: $(basename "$0") [--shell-only]" >&2
        exit 2
    fi

    shell_only=1
fi

actionlint_version="1.7.12"
actionlint_sha256_linux_amd64="8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8"
actionlint_sha256_darwin_arm64="aba9ced2dee8d27fecca3dc7feb1a7f9a52caefa1eb46f3271ea66b6e0e6953f"
zizmor_requirements="${repo_root}/eng/quality/zizmor-requirements.txt"
# Read back from the pinned requirements so the reported version cannot drift
# away from the version actually installed.
zizmor_version="$(
    sed -n 's/^zizmor==\([0-9][0-9A-Za-z.-]*\).*$/\1/p' "${zizmor_requirements}"
)"
auto_install="${DOKA_LINT_AUTO_INSTALL:-0}"
tool_root="${repo_root}/artifacts/lint-tools"
failures=0

log_failure() {
    echo "FAIL: $1" >&2
    failures=$(( failures + 1 ))
}

require_manual_install() {
    local tool="$1"
    local hint="$2"
    local hydratable="${3:-yes}"

    echo "'${tool}' is required but was not found on PATH." >&2
    echo "Install it with: ${hint}" >&2
    if [[ "${hydratable}" == "yes" ]]; then
        echo "Set DOKA_LINT_AUTO_INSTALL=1 to hydrate the pinned version instead." >&2
    fi
    exit 1
}

# The release tarball is fetched over HTTPS and then verified against the
# checksum pinned above, so a compromised release asset fails closed rather
# than executing.
install_actionlint() {
    local platform="$1"
    local expected_sha256="$2"
    local archive="${tool_root}/actionlint_${actionlint_version}_${platform}.tar.gz"
    local url="https://github.com/rhysd/actionlint/releases/download"
    url="${url}/v${actionlint_version}/actionlint_${actionlint_version}_${platform}.tar.gz"

    mkdir -p "${tool_root}"
    curl --fail --location --silent --show-error --output "${archive}" "${url}"

    local observed_sha256
    if command -v sha256sum > /dev/null 2>&1; then
        observed_sha256="$(sha256sum "${archive}" | awk '{print $1}')"
    else
        observed_sha256="$(shasum -a 256 "${archive}" | awk '{print $1}')"
    fi

    if [[ "${observed_sha256}" != "${expected_sha256}" ]]; then
        echo "actionlint archive checksum mismatch." >&2
        echo "Expected: ${expected_sha256}" >&2
        echo "Observed: ${observed_sha256}" >&2
        rm -f -- "${archive}"
        exit 1
    fi

    tar --extract --file "${archive}" --directory "${tool_root}" actionlint
    rm -f -- "${archive}"
}

# Returns the version a tool binary reports, or the empty string when the
# binary cannot be executed. Used to reject a cached or PATH copy whose version
# does not match the pin.
tool_version() {
    local binary="$1"
    local pattern="$2"

    if [[ ! -x "${binary}" ]] && ! command -v "${binary}" > /dev/null 2>&1; then
        return 0
    fi

    "${binary}" --version 2>/dev/null | sed -n "${pattern}" | head -1
}

resolve_actionlint() {
    local pinned="${tool_root}/actionlint"
    local version_pattern='s/^\([0-9][0-9A-Za-z.-]*\)$/\1/p'

    # Under auto-install the pinned build is the only acceptable binary, so a
    # PATH copy is ignored entirely. Otherwise a runner image or a developer
    # installation could silently replace the version this gate claims to run.
    if [[ "${auto_install}" != "1" ]]; then
        if command -v actionlint > /dev/null 2>&1; then
            local found
            found="$(tool_version "$(command -v actionlint)" "${version_pattern}")"
            if [[ "${found}" != "${actionlint_version}" ]]; then
                echo "Note: using actionlint ${found:-unknown} from PATH;" >&2
                echo "CI pins ${actionlint_version}, so results can differ." >&2
            fi
            command -v actionlint
            return
        fi

        require_manual_install "actionlint" "brew install actionlint"
    fi

    if [[ -x "${pinned}" ]]; then
        if [[ "$(tool_version "${pinned}" "${version_pattern}")" == "${actionlint_version}" ]]; then
            echo "${pinned}"
            return
        fi

        # A cached binary from an earlier pin must not survive a version bump.
        rm -f -- "${pinned}"
    fi

    local platform
    local expected_sha256
    case "$(uname -s)/$(uname -m)" in
        Linux/x86_64)
            platform="linux_amd64"
            expected_sha256="${actionlint_sha256_linux_amd64}"
            ;;
        Darwin/arm64)
            platform="darwin_arm64"
            expected_sha256="${actionlint_sha256_darwin_arm64}"
            ;;
        *)
            echo "No pinned actionlint build for $(uname -s)/$(uname -m)." >&2
            exit 1
            ;;
    esac

    install_actionlint "${platform}" "${expected_sha256}" >&2
    echo "${tool_root}/actionlint"
}

resolve_zizmor() {
    local pinned="${tool_root}/venv/bin/zizmor"
    local version_pattern='s/^zizmor \([0-9][0-9A-Za-z.-]*\).*$/\1/p'

    if [[ "${auto_install}" != "1" ]]; then
        if command -v zizmor > /dev/null 2>&1; then
            local found
            found="$(tool_version "$(command -v zizmor)" "${version_pattern}")"
            if [[ "${found}" != "${zizmor_version}" ]]; then
                echo "Note: using zizmor ${found:-unknown} from PATH;" >&2
                echo "CI pins ${zizmor_version}, so results can differ." >&2
            fi
            command -v zizmor
            return
        fi

        require_manual_install "zizmor" "brew install zizmor  (or: pipx install zizmor)"
    fi

    if [[ -x "${pinned}" ]]; then
        if [[ "$(tool_version "${pinned}" "${version_pattern}")" == "${zizmor_version}" ]]; then
            echo "${pinned}"
            return
        fi

        rm -rf -- "${tool_root}/venv"
    fi

    # --require-hashes makes pip reject every artifact whose digest is absent
    # from the requirements file, including transitive ones, so the install is
    # verified rather than merely version-pinned.
    python3 -m venv "${tool_root}/venv" >&2
    "${tool_root}/venv/bin/pip" install --quiet --disable-pip-version-check \
        --require-hashes \
        --only-binary=:all: \
        --requirement "${zizmor_requirements}" >&2
    echo "${tool_root}/venv/bin/zizmor"
}

# Built with a read loop rather than mapfile so the gate behaves identically on
# the macOS system Bash 3.2 that contributors run locally.
shell_scripts=()
while IFS= read -r shell_script; do
    shell_scripts+=("${shell_script}")
done < <(
    {
        find eng -type f -name '*.sh'
        find .githooks -type f
    } | sort
)

if (( ${#shell_scripts[@]} == 0 )); then
    echo "No shell scripts were discovered; the lint contract cannot be empty." >&2
    exit 2
fi

echo "Validating shell syntax for ${#shell_scripts[@]} script(s)..."
for shell_script in "${shell_scripts[@]}"; do
    if ! bash -n "${shell_script}"; then
        log_failure "${shell_script} is not syntactically valid."
    fi
done

# The static analyzer is a required part of the shell contract, exactly like
# actionlint and zizmor are required parts of the workflow contract. Reporting
# a passing contract while one of its checks never ran is the failure mode this
# gate exists to prevent, so a missing tool stops the run instead of warning.
if ! command -v shellcheck > /dev/null 2>&1; then
    if [[ "${CI:-false}" == "true" ]]; then
        echo "shellcheck is unavailable on a runner expected to provide it." >&2
        exit 1
    fi

    # Not hydrated: shellcheck ships with the hosted runner image and is a
    # one-time local install, so pinning a second copy would add supply-chain
    # surface without removing a drift risk.
    require_manual_install "shellcheck" "brew install shellcheck" "no"
fi

echo "Running shellcheck..."
if ! shellcheck --severity=warning --external-sources "${shell_scripts[@]}"; then
    log_failure "shellcheck reported findings."
fi

if (( shell_only == 1 )); then
    if (( failures > 0 )); then
        echo >&2
        echo "Shell lint contract failed with ${failures} error(s)." >&2
        exit 1
    fi

    echo "Shell lint contract passed."
    exit 0
fi

actionlint_binary="$(resolve_actionlint)"
echo "Running actionlint ${actionlint_version}..."
if ! "${actionlint_binary}" -color; then
    log_failure "actionlint reported findings."
fi

zizmor_binary="$(resolve_zizmor)"
echo "Running zizmor ${zizmor_version}..."
# Offline keeps the gate deterministic and free of a GitHub token; the audits
# that need network access are the ones already covered by branch rulesets.
if ! "${zizmor_binary}" --offline --persona=regular .github/workflows; then
    log_failure "zizmor reported findings."
fi

if (( failures > 0 )); then
    echo >&2
    echo "Workflow and shell lint contract failed with ${failures} error(s)." >&2
    exit 1
fi

echo "Workflow and shell lint contract passed."
