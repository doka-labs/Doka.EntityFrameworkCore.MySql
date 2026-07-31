#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
spatial_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
functional_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
specification_contract_project="${repo_root}/eng/Doka.EntityFrameworkCore.MySql.SpecificationContract/Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj"
audit_parser="${repo_root}/eng/check-vulnerability-audit.sh"
release_candidate_run_id="${DOKA_RELEASE_CANDIDATE_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
release_candidate_dir="${repo_root}/artifacts/release-candidate/${release_candidate_run_id}"
packages_dir="${release_candidate_dir}/packages"
audit_dir="${release_candidate_dir}/audit"
sbom_dir="${release_candidate_dir}/sbom"
sbom_components_dir="${release_candidate_dir}/sbom-components"
summary_file="${release_candidate_dir}/release-candidate-summary.md"
evidence_file="${release_candidate_dir}/release-candidate-evidence.json"
changelog_file="${release_candidate_dir}/release-candidate-changelog.md"
specification_dir="${release_candidate_dir}/specification"
coverage_input_dir="${release_candidate_dir}/coverage-input"
coverage_merged_dir="${release_candidate_dir}/coverage-merged"
migration_deployment_root="${release_candidate_dir}/migration-deployment"
migration_deployment_dir="${migration_deployment_root}/${release_candidate_run_id}"
performance_dir="${release_candidate_dir}/performance"

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

run_specification_gate() {
    mkdir -p "${specification_dir}"

    dotnet restore "${specification_contract_project}" --tl:off
    dotnet restore "${functional_test_project}" --tl:off
    dotnet build "${specification_contract_project}" --configuration Release --no-restore --tl:off -m:1
    dotnet build "${functional_test_project}" --configuration Release --no-restore --tl:off -m:1
    bash "${repo_root}/eng/check-spec-contract.sh"
    dotnet test "${functional_test_project}" \
        --configuration Release --no-build --no-restore --tl:off \
        --filter "FullyQualifiedName~SpecDispositionContractTests" \
        --logger trx \
        --results-directory "${specification_dir}/contract"
    bash "${repo_root}/eng/check-spec-discovery.sh"

    local targets=("mysql84" "mariadb114" "mariadb118")
    local target
    for target in "${targets[@]}"; do
        echo "Running release specification suite against ${target}..."
        DOKA_SPEC_TEST_TARGET="${target}" \
        DOKA_TEST_DATABASE_EVIDENCE_FILE="${specification_dir}/${target}/test-database-evidence.json" \
            dotnet test "${functional_test_project}" \
                --configuration Release --no-build --no-restore --tl:off \
                --filter "Category=Spec" \
                --collect:"XPlat Code Coverage" \
                --logger trx \
                --results-directory "${specification_dir}/${target}"
        bash "${repo_root}/eng/check-spec-results.sh" \
            "${target}" \
            "${specification_dir}/${target}"
    done
}

run_repository_test_gate() {
    DOKA_COVERAGE_RESULTS_DIR="${coverage_input_dir}/repo-tests" \
        bash "${repo_root}/eng/test.sh"
}

run_integration_gate() {
    DOKA_COVERAGE_RESULTS_DIR="${coverage_input_dir}/integration" \
    DOKA_INTEGRATION_RUN_ID="${release_candidate_run_id}" \
    DOKA_INTEGRATION_TARGETS="mysql84,mariadb118" \
        bash "${repo_root}/eng/test-integration.sh"
}

run_migration_deployment_gate() {
    DOKA_MIGRATION_DEPLOYMENT_RUN_ID="${release_candidate_run_id}" \
    DOKA_MIGRATION_DEPLOYMENT_EVIDENCE_ROOT="${migration_deployment_root}" \
        bash "${repo_root}/eng/test-migration-deployment.sh"
}

run_coverage_gate() {
    bash "${repo_root}/eng/merge-coverage.sh" \
        "${release_candidate_dir}" \
        "${coverage_merged_dir}"
    bash "${repo_root}/eng/check-coverage-threshold.sh" "${coverage_merged_dir}"
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
    local runtime_assets="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql/project.assets.json"
    local spatial_assets="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.NetTopologySuite/project.assets.json"
    local timeout_cmd
    timeout_cmd="$(resolve_timeout_cmd)"

    mkdir -p "${sbom_dir}"
    mkdir -p "${sbom_components_dir}/runtime" "${sbom_components_dir}/spatial"

    if [[ ! -f "${runtime_assets}" || ! -f "${spatial_assets}" ]]; then
        echo "Release package dependency assets are missing; run_pack must complete before SBOM generation." >&2
        exit 1
    fi

    # Component detection consumes the exact restored graphs of the two
    # released packages. This excludes stale, test, and benchmark graphs while
    # retaining all direct and transitive package dependencies.
    cp "${runtime_assets}" "${sbom_components_dir}/runtime/project.assets.json"
    cp "${spatial_assets}" "${sbom_components_dir}/spatial/project.assets.json"

    dotnet tool restore

    # The pinned SBOM tool targets an older supported .NET runtime. The CLI
    # switch keeps the local tool executable when only a newer runtime is
    # installed, without weakening the repository's SDK pin.
    local sbom_cmd=(dotnet tool run sbom-tool --allow-roll-forward -- Generate \
        -b "${packages_dir}" \
        -bc "${sbom_components_dir}" \
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
        echo "Running SBOM generation (no timeout command available -- running without watchdog)..."
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
        echo "This changelog records specification, migration deployment, package, audit, benchmark, and SBOM evidence."
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
        echo "- specificationDirectory: ${specification_dir}"
        echo "- migrationDeploymentDirectory: ${migration_deployment_dir}"
        echo "- coverageDirectory: ${coverage_merged_dir}"
        echo "- performanceDirectory: ${performance_dir}"
        echo "- changelogFile: ${changelog_file}"
        echo "- packageCount: ${package_count}"
        echo
        echo "This repo-local release-candidate path retains package, audit, and SBOM evidence without implying signing, provenance, or publication readiness."
    } > "${summary_file}"
}

run_benchmark_and_gate() {
    local skip="${DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS:-0}"

    if [[ "${skip}" == "1" ]]; then
        echo "Benchmark gate skipped via DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS=1."
        echo "This bypass is for dev-loop iteration only; the resulting evidence is not release-eligible." >&2
        return 0
    fi

    local engines=("mysql84" "mariadb118")
    for engine in "${engines[@]}"; do
        echo "Running benchmark scorecard and soak evidence against ${engine}..."
            DOKA_BENCHMARK_PROFILE=scorecard \
            DOKA_BENCHMARK_TARGET="${engine}" \
            DOKA_BENCHMARK_RUN_ID="${release_candidate_run_id}" \
            "${repo_root}/eng/benchmark.sh" --up-run-down
    done

    echo "Re-evaluating the complete performance and memory gate..."
    DOKA_BENCHMARK_PROFILE=scorecard \
        DOKA_BENCHMARK_GATE_STRICT=1 \
        DOKA_BENCHMARK_GATE_RUN_ID="${release_candidate_run_id}" \
        bash "${repo_root}/eng/check-benchmark-ratios.sh" \
            "${repo_root}/artifacts/benchmarks"

    mkdir -p "${performance_dir}"
    for engine in "${engines[@]}"; do
        cp -R \
            "${repo_root}/artifacts/benchmarks/${engine}/reports/${release_candidate_run_id}" \
            "${performance_dir}/${engine}"
    done
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
  "specificationDirectory": "${specification_dir}",
  "migrationDeploymentDirectory": "${migration_deployment_dir}",
  "coverageDirectory": "${coverage_merged_dir}",
  "performanceDirectory": "${performance_dir}",
  "changelogFile": "${changelog_file}",
  "packageCount": ${package_count},
  "sbomFileCount": ${sbom_file_count}
}
EOF
}

require_command jq
cd "${repo_root}"

"${repo_root}/eng/verify-dotnet.sh"
"${repo_root}/eng/validate-adrs.sh"
run_repository_test_gate
run_specification_gate
run_integration_gate
run_migration_deployment_gate
run_coverage_gate
run_pack

run_vulnerability_audit "${runtime_project}" "${audit_dir}/Doka.EntityFrameworkCore.MySql.vulnerabilities.json"
run_vulnerability_audit "${spatial_project}" "${audit_dir}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.vulnerabilities.json"

release_version="$(package_version_from_file "Doka.EntityFrameworkCore.MySql")"
run_sbom "${release_version}"
write_changelog "${release_version}"

run_benchmark_and_gate

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
bash "${repo_root}/eng/check-publication-readiness.sh"
