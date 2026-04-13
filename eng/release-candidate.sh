#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
spatial_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
audit_parser="${repo_root}/eng/check-vulnerability-audit.sh"
release_candidate_run_id="${DOKA_RELEASE_CANDIDATE_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
release_candidate_dir="${repo_root}/artifacts/release-candidate/${release_candidate_run_id}"
packages_dir="${release_candidate_dir}/packages"
audit_dir="${release_candidate_dir}/audit"
sbom_dir="${release_candidate_dir}/sbom"
summary_file="${release_candidate_dir}/release-candidate-summary.md"
evidence_file="${release_candidate_dir}/release-candidate-evidence.json"
changelog_file="${release_candidate_dir}/release-candidate-changelog.md"

require_command() {
    local command_name="$1"

    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Required command '${command_name}' is not available." >&2
        exit 1
    fi
}

# Resolve a portable timeout command (GNU timeout on Linux, gtimeout via
# Homebrew coreutils on macOS).  Falls back to running without a timeout
# when neither is available, so the script remains functional on minimal
# environments.
resolve_timeout_cmd() {
    if command -v timeout >/dev/null 2>&1; then
        echo "timeout"
    elif command -v gtimeout >/dev/null 2>&1; then
        echo "gtimeout"
    else
        echo ""
    fi
}

package_version_from_file() {
    local package_name="$1"
    local package_path

    package_path="$(find "${packages_dir}" -maxdepth 1 -type f -name "${package_name}.*.nupkg" ! -name "*.symbols.nupkg" | head -n 1)"

    if [[ -z "${package_path}" ]]; then
        echo "Unable to locate package '${package_name}' under ${packages_dir}." >&2
        exit 1
    fi

    basename "${package_path}" | sed -E "s/^${package_name//./\\.}\.([0-9A-Za-z.-]+)\.nupkg$/\1/"
}

run_pack() {
    mkdir -p "${packages_dir}"

    dotnet restore "${runtime_project}" --tl:off
    dotnet restore "${spatial_project}" --tl:off
    dotnet build "${runtime_project}" --configuration Release --no-restore --tl:off -m:1
    dotnet build "${spatial_project}" --configuration Release --no-restore --tl:off -m:1
    dotnet pack "${runtime_project}" --configuration Release --no-build --no-restore --output "${packages_dir}" --tl:off
    dotnet pack "${spatial_project}" --configuration Release --no-build --no-restore --output "${packages_dir}" --tl:off
}

run_vulnerability_audit() {
    local project_path="$1"
    local output_file="$2"

    mkdir -p "${audit_dir}"

    dotnet package list \
        --project "${project_path}" \
        --vulnerable \
        --include-transitive \
        --format json > "${output_file}"

    if [[ "$(bash "${audit_parser}" "${output_file}")" -ne 0 ]]; then
        echo "Vulnerability audit failed for ${project_path}; see ${output_file}." >&2
        exit 1
    fi
}

run_sbom() {
    local release_version="$1"
    local sbom_timeout="${DOKA_SBOM_TIMEOUT:-300}"
    local timeout_cmd
    timeout_cmd="$(resolve_timeout_cmd)"

    mkdir -p "${sbom_dir}"

    dotnet tool restore

    local sbom_cmd=(dotnet tool run sbom-tool Generate \
        -b "${packages_dir}" \
        -bc "${repo_root}" \
        -m "${sbom_dir}" \
        -pn "Doka.EntityFrameworkCore.MySql.ReleaseCandidate" \
        -pv "${release_version}" \
        -ps "Doka" \
        -nsb "https://github.com/doka/Doka.EntityFrameworkCore.MySql/releases/sbom" \
        -D true)

    if [[ -n "${timeout_cmd}" ]]; then
        echo "Running SBOM generation (timeout: ${sbom_timeout}s via ${timeout_cmd})..."
        if ! "${timeout_cmd}" "${sbom_timeout}" "${sbom_cmd[@]}"; then
            echo "SBOM generation failed or timed out after ${sbom_timeout}s." >&2
            exit 1
        fi
    else
        echo "Running SBOM generation (no timeout command available — running without watchdog)..."
        if ! "${sbom_cmd[@]}"; then
            echo "SBOM generation failed." >&2
            exit 1
        fi
    fi
}

write_changelog() {
    local release_version="$1"

    {
        echo "# Release candidate changelog"
        echo
        echo "## Release metadata"
        echo
        echo "- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
        echo "- releaseCandidateRunId: ${release_candidate_run_id}"
        echo "- releaseVersion: ${release_version}"
        echo
        echo "## Included packages"
        echo
        echo "- Doka.EntityFrameworkCore.MySql ${release_version}"
        echo "- Doka.EntityFrameworkCore.MySql.NetTopologySuite ${release_version}"
        echo
        echo "## Repo-local release-hardening note"
        echo
        echo "This changelog is the repo-local release-candidate record for Phase 4 pack, audit, and SBOM evidence only."
        echo "It does not imply signing, provenance, publication, or externally hosted compatibility closure."
    } > "${changelog_file}"
}

write_summary() {
    local release_version="$1"
    local package_count="$2"

    {
        echo "# Release candidate summary"
        echo
        echo "- generatedUtc: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
        echo "- releaseCandidateRunId: ${release_candidate_run_id}"
        echo "- releaseVersion: ${release_version}"
        echo "- packagesDirectory: ${packages_dir}"
        echo "- auditDirectory: ${audit_dir}"
        echo "- sbomDirectory: ${sbom_dir}"
        echo "- changelogFile: ${changelog_file}"
        echo "- packageCount: ${package_count}"
        echo
        echo "This repo-local release-candidate path retains package, audit, and SBOM evidence without implying signing, provenance, or publication readiness."
    } > "${summary_file}"
}

write_evidence() {
    local release_version="$1"
    local package_count="$2"
    local sbom_file_count="$3"

    cat > "${evidence_file}" <<EOF
{
  "generatedUtc": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "releaseCandidateRunId": "${release_candidate_run_id}",
  "releaseVersion": "${release_version}",
  "packagesDirectory": "${packages_dir}",
  "auditDirectory": "${audit_dir}",
  "sbomDirectory": "${sbom_dir}",
  "changelogFile": "${changelog_file}",
  "packageCount": ${package_count},
  "sbomFileCount": ${sbom_file_count}
}
EOF
}

require_command jq
cd "${repo_root}"

"${repo_root}/eng/verify-dotnet.sh"
run_pack

run_vulnerability_audit "${runtime_project}" "${audit_dir}/Doka.EntityFrameworkCore.MySql.vulnerabilities.json"
run_vulnerability_audit "${spatial_project}" "${audit_dir}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.vulnerabilities.json"

release_version="$(package_version_from_file "Doka.EntityFrameworkCore.MySql")"
run_sbom "${release_version}"
write_changelog "${release_version}"

package_count="$(find "${packages_dir}" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.symbols.nupkg' | wc -l | tr -d ' ')"
sbom_file_count="$(find "${sbom_dir}" -type f | wc -l | tr -d ' ')"

if [[ "${package_count}" -lt 2 ]]; then
    echo "Expected release-candidate packaging to produce both provider packages." >&2
    exit 1
fi

if [[ "${sbom_file_count}" -eq 0 ]]; then
    echo "No SBOM files were generated under ${sbom_dir}." >&2
    exit 1
fi

write_summary "${release_version}" "${package_count}"
write_evidence "${release_version}" "${package_count}" "${sbom_file_count}"
