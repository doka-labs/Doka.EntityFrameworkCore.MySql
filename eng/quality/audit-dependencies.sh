#!/usr/bin/env bash

# Audits this branch's complete source-project graph without building, starting
# databases or installing tools. Raw failures remain evidence, not clean scans.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
audit_root="${repo_root}/artifacts/security-audit"
if (( $# != 0 )); then
    if (( $# != 2 )) || [[ "$1" != "--output-dir" || -z "$2" ]]; then
        echo "Usage: $0 [--output-dir <evidence-base-directory>]" >&2
        exit 2
    fi

    audit_root="$2"
fi

for required_command in dotnet jq find sort mktemp; do
    if ! command -v "${required_command}" > /dev/null 2>&1; then
        echo "Required command '${required_command}' is not available." >&2
        exit 1
    fi
done

mkdir -p "${audit_root}"
audit_root="$(cd "${audit_root}" && pwd)"
run_root="$(mktemp -d "${audit_root}/run.XXXXXXXX")"
result_file="${run_root}/results.jsonl"
project_count=0
processed_count=0
passed_count=0
failed_count=0
sdk_version="unknown"
: > "${result_file}"

finish() {
    local exit_code="$?"
    local result="failed"
    if [[ "${exit_code}" -eq 0 && "${project_count}" -gt 0 \
        && "${passed_count}" -eq "${project_count}" && "${failed_count}" -eq 0 ]]; then
        result="passed"
    fi

    jq -n --arg result "${result}" --arg sdk "${sdk_version}" \
        --argjson projectCount "${project_count}" --argjson passedCount "${passed_count}" \
        --argjson failedCount "${failed_count}" --slurpfile projects "${result_file}" \
        '{schemaVersion:1,result:$result,approvedDotnetSdk:$sdk,projectCount:$projectCount,
          passedCount:$passedCount,failedCount:$failedCount,projects:$projects}' \
        > "${run_root}/summary.json"
    if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
        {
            printf '## Dependency security audit\n\n'
            printf 'Result: **%s**; SDK: %s.\n\n' "${result}" "${sdk_version}"
            printf 'Projects: %s; passed: %s; failed: %s.\n' \
                "${project_count}" "${passed_count}" "${failed_count}"
            printf 'Raw reports and restore/source failures are retained in the audit artifact.\n'
        } >> "${GITHUB_STEP_SUMMARY}"
    fi

    echo "Dependency security audit ${result}: ${passed_count}/${project_count} projects; evidence ${run_root}."
    # WHY: Some shells report an interrupted compound command as zero when an
    # EXIT trap succeeds. Incomplete coverage is failure regardless of that code.
    trap - EXIT
    if [[ "${result}" != "passed" && "${exit_code}" -eq 0 ]]; then
        exit 1
    fi

    exit "${exit_code}"
}

trap finish EXIT
cd "${repo_root}"
bash "${repo_root}/eng/common/verify-dotnet.sh" > "${run_root}/sdk.log" 2>&1
sdk_version="$(dotnet --version)"

# WHY: Advisory cache freshness must not depend on an earlier successful CI or
# contributor restore. Keep the package-content cache; isolate only HTTP data.
export NUGET_HTTP_CACHE_PATH="${run_root}/http-cache"
mkdir -p "${NUGET_HTTP_CACHE_PATH}"
configuration="${run_root}/NuGet.Config"
printf '%s\n' \
    '<?xml version="1.0" encoding="utf-8"?>' \
    '<configuration>' \
    '  <packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>' \
    '  <auditSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></auditSources>' \
    '</configuration>' > "${configuration}"

source_commit="${GITHUB_SHA:-}"
source_ref="${GITHUB_REF:-}"
if [[ -z "${source_commit}" ]] && command -v git > /dev/null 2>&1; then
    source_commit="$(git rev-parse HEAD 2>/dev/null || true)"
    source_ref="$(git symbolic-ref HEAD 2>/dev/null || true)"
fi

jq -n --arg sdk "${sdk_version}" --arg commit "${source_commit}" --arg ref "${source_ref}" \
    --arg observedAt "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" \
    '{schemaVersion:1,approvedDotnetSdk:$sdk,sourceCommit:$commit,sourceRef:$ref,observedAt:$observedAt,
      auditSource:"https://api.nuget.org/v3/index.json",includeTransitive:true}' > "${run_root}/context.json"

projects=()
for source_group in src tests benchmarks eng/tools examples; do
    source_root="${repo_root}/${source_group}"
    if [[ ! -d "${source_root}" || -L "${source_root}" ]]; then
        echo "Required audit source group '${source_group}' is missing or symlinked." >&2
        exit 1
    fi

    group_count=0
    group_inventory="${run_root}/inventory-${source_group//\//-}.nul"
    find "${source_root}" \
        -type d \( -name bin -o -name obj -o -name artifacts \) -prune \
        -o \( -type f -o -type l \) -name '*.csproj' -print0 > "${group_inventory}"
    while IFS= read -r -d '' project; do
        if [[ -L "${project}" || "${project}" == *$'\n'* ]]; then
            echo "Audit project paths must be regular, unambiguous source files." >&2
            exit 1
        fi

        projects+=("${project}")
        group_count=$(( group_count + 1 ))
    done < "${group_inventory}"
    if [[ "${group_count}" -eq 0 ]]; then
        echo "Required audit source group '${source_group}' contains no project." >&2
        exit 1
    fi
done

printf '%s\n' "${projects[@]}" | LC_ALL=C sort > "${run_root}/inventory.txt"
# WHY: Count the planned inventory, not only visited rows. An interrupted loop
# must not turn a successfully audited prefix into complete coverage.
project_count="${#projects[@]}"
while IFS= read -r project; do
    processed_count=$(( processed_count + 1 ))
    project_root="${run_root}/$(printf '%04d' "${processed_count}")"
    mkdir -p "${project_root}"
    status="failed"
    stage="restore"
    restore_arguments=(--force --no-http-cache --configfile "${configuration}")
    if [[ -f "$(dirname "${project}")/packages.lock.json" ]]; then
        restore_arguments+=(--locked-mode)
    fi

    # WHY: package-list can report zero findings after an advisory fetch failed.
    # A fresh audited restore with fatal NU1900-NU1905 is independently required.
    if dotnet restore "${project}" "${restore_arguments[@]}" \
        -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low \
        -p:TreatWarningsAsErrors=true -p:NoWarn= -p:WarningsNotAsErrors= \
        > "${project_root}/restore.log" 2>&1; then
        stage="package-list"
        if dotnet package list --project "${project}" --no-restore --config "${configuration}" \
            --vulnerable --include-transitive --format json --output-version 1 \
            > "${project_root}/audit.json" 2> "${project_root}/audit.log"; then
            stage="validation"
            if bash "${repo_root}/eng/quality/check-vulnerability-audit.sh" \
                "${project_root}/audit.json" "${project}" > "${project_root}/validation.log" 2>&1; then
                status="passed"
                stage="complete"
            fi
        fi
    fi

    jq -n --arg project "${project}" --arg result "${status}" --arg stage "${stage}" \
        '{project:$project,result:$result,stage:$stage}' > "${project_root}/result.json"
    jq -c . "${project_root}/result.json" >> "${result_file}"
    if [[ "${status}" == "passed" ]]; then
        passed_count=$(( passed_count + 1 ))
    else
        failed_count=$(( failed_count + 1 ))
        echo "Dependency audit failed for ${project} at ${stage}; see ${project_root}." >&2
    fi
done < "${run_root}/inventory.txt"

if [[ "${failed_count}" -ne 0 || "${project_count}" -eq 0 ]]; then
    exit 1
fi
