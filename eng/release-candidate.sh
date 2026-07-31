#!/usr/bin/env bash

# This script is a fail-closed release orchestrator. Its order is intentional:
# validate source identity, produce fresh verification artifacts, validate the
# publication surface, and only then seal the complete directory with a
# manifest and detached checksum.

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
changelog_file="${release_candidate_dir}/release-candidate-changelog.md"
dependency_graph_file="${release_candidate_dir}/resolved-packages.json"
specification_dir="${release_candidate_dir}/specification"
coverage_input_dir="${release_candidate_dir}/coverage-input"
coverage_merged_dir="${release_candidate_dir}/coverage-merged"
integration_dir="${release_candidate_dir}/integration"
migration_deployment_root="${release_candidate_dir}/migration-deployment"
performance_dir="${release_candidate_dir}/performance"
require_release_tag="${DOKA_RELEASE_REQUIRE_TAG:-1}"
release_version_override="${DOKA_RELEASE_VERSION:-}"

# Validate early for fast operator feedback. release_evidence.py repeats these
# checks immediately before sealing the manifest so a long-running candidate
# cannot hide source drift that occurred while its gates were executing.
validate_release_source() {
    if [[ -n "$(git status --porcelain --untracked-files=all)" ]]; then
        echo "Release candidates require a clean Git worktree." >&2
        exit 1
    fi

    if [[ "${require_release_tag}" != "1" ]]; then
        return 0
    fi

    local version_tags
    version_tags="$(git tag --points-at HEAD | grep -E '^v[0-9]+[.][0-9]+[.][0-9]+([-.][0-9A-Za-z.-]+)?$' || true)"
    if [[ "$(printf '%s\n' "${version_tags}" | sed '/^$/d' | wc -l | tr -d ' ')" != "1" ]]; then
        echo "A release candidate must run from exactly one semantic version tag at HEAD." >&2
        exit 1
    fi

    local release_tag
    release_tag="$(printf '%s\n' "${version_tags}" | sed '/^$/d')"
    if [[ -z "${release_version_override}" ]]; then
        release_version_override="${release_tag#v}"
    elif [[ "${release_tag}" != "v${release_version_override}" ]]; then
        echo "Release tag ${release_tag} does not match package version ${release_version_override}." >&2
        exit 1
    fi

    if [[ -n "${DOKA_RELEASE_EXPECTED_REF:-}" \
        && "${DOKA_RELEASE_EXPECTED_REF}" != "refs/tags/${release_tag}" ]]; then
        echo "Expected release ref ${DOKA_RELEASE_EXPECTED_REF} does not match refs/tags/${release_tag}." >&2
        exit 1
    fi
}

# A run identifier owns one immutable evidence directory. Reusing a non-empty
# directory could bind stale artifacts to a new source or workflow identity.
prepare_release_directory() {
    if [[ -d "${release_candidate_dir}" \
        && -n "$(find "${release_candidate_dir}" -mindepth 1 -print -quit)" ]]; then
        echo "Release-candidate directory already contains evidence: ${release_candidate_dir}" >&2
        echo "Use a new DOKA_RELEASE_CANDIDATE_RUN_ID so stale artifacts cannot enter the manifest." >&2
        exit 1
    fi

    mkdir -p "${release_candidate_dir}"
}

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

    local version_arguments=()
    if [[ -n "${release_version_override}" ]]; then
        version_arguments+=("-p:PackageVersion=${release_version_override}")
    fi

    dotnet restore "${runtime_project}" --tl:off
    dotnet restore "${spatial_project}" --tl:off
    dotnet build "${runtime_project}" --configuration Release --no-restore --tl:off -m:1 \
        "${version_arguments[@]}"
    dotnet build "${spatial_project}" --configuration Release --no-restore --tl:off -m:1 \
        "${version_arguments[@]}"
    dotnet pack "${runtime_project}" --configuration Release --no-build --no-restore \
        --output "${packages_dir}" --tl:off "${version_arguments[@]}"
    dotnet pack "${spatial_project}" --configuration Release --no-build --no-restore \
        --output "${packages_dir}" --tl:off "${version_arguments[@]}"

    # Persist the graph NuGet actually resolved. The manifest rejects missing
    # or ambiguous versions for the provider's contract dependencies.
    dotnet package list \
        --project "${runtime_project}" \
        --include-transitive \
        --format json \
        --no-restore > "${dependency_graph_file}"
}

# The advertised engine matrix is explicit here so a newly green default test
# selection cannot silently narrow release conformance.
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
                --filter "Category=Spec|Category=Live" \
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

run_integration_configuration_and_failure_gate() {
    # Route every integration artifact into the candidate root. Files outside
    # this root cannot be hashed into the release manifest. The required-matrix
    # flag also prevents a future caller from narrowing either the engine set or
    # the test categories while still claiming release-candidate coverage.
    DOKA_COVERAGE_RESULTS_DIR="${coverage_input_dir}/integration" \
    DOKA_INTEGRATION_ARTIFACTS_DIR="${integration_dir}" \
    DOKA_INTEGRATION_RUN_ID="${release_candidate_run_id}" \
    DOKA_INTEGRATION_TARGETS="mysql84,mariadb114,mariadb118" \
    DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX=1 \
    DOKA_TEST_DATABASE_EVIDENCE_FILE="${integration_dir}/test-database-evidence.json" \
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
        echo "The evidence manifest binds these files to their exact source and dependency identities."
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
        echo "- packagesDirectory: packages"
        echo "- auditDirectory: audit"
        echo "- sbomDirectory: sbom"
        echo "- specificationDirectory: specification"
        echo "- integrationDirectory: integration"
        echo "- migrationDeploymentDirectory: migration-deployment/${release_candidate_run_id}"
        echo "- coverageDirectory: coverage-merged"
        echo "- performanceDirectory: performance"
        echo "- changelogFile: release-candidate-changelog.md"
        echo "- evidenceFile: release-candidate-evidence.json"
        echo "- evidenceChecksumFile: release-candidate-evidence.sha256"
        echo "- packageCount: ${package_count}"
        echo
        echo "The manifest uses portable paths and verifies every retained artifact before upload."
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
    local arguments=(
        generate
        --repo "${repo_root}"
        --root "${release_candidate_dir}"
        --run-id "${release_candidate_run_id}"
        --release-version "${release_version}"
        --dependency-graph "${dependency_graph_file}"
    )

    if [[ -n "${DOKA_RELEASE_EXPECTED_REF:-}" ]]; then
        arguments+=(--expected-ref "${DOKA_RELEASE_EXPECTED_REF}")
    fi
    if [[ "${require_release_tag}" == "1" ]]; then
        arguments+=(--require-tag)
    fi

    # Generation performs its own readback, and the separate invocation proves
    # the persisted CLI contract that publication automation will use.
    python3 "${repo_root}/eng/release_evidence.py" "${arguments[@]}"

    python3 "${repo_root}/eng/release_evidence.py" verify \
        --root "${release_candidate_dir}" \
        --repo "${repo_root}"
}

require_command jq
require_command python3
cd "${repo_root}"

# Do not reorder these gates without updating the evidence contract. The final
# manifest inventory must observe every retained artifact and must be written
# only after publication readiness has passed.
validate_release_source
prepare_release_directory
"${repo_root}/eng/verify-dotnet.sh"
"${repo_root}/eng/validate-adrs.sh"
run_repository_test_gate
run_specification_gate
run_integration_configuration_and_failure_gate
run_migration_deployment_gate
run_coverage_gate
run_pack

run_vulnerability_audit "${runtime_project}" "${audit_dir}/Doka.EntityFrameworkCore.MySql.vulnerabilities.json"
run_vulnerability_audit "${spatial_project}" "${audit_dir}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.vulnerabilities.json"

release_version="$(package_version_from_file "Doka.EntityFrameworkCore.MySql")"
spatial_release_version="$(package_version_from_file "Doka.EntityFrameworkCore.MySql.NetTopologySuite")"

if [[ "${spatial_release_version}" != "${release_version}" ]]; then
    echo "Provider package version ${release_version} does not match spatial package ${spatial_release_version}." >&2
    exit 1
fi

if [[ -n "${release_version_override}" && "${release_version}" != "${release_version_override}" ]]; then
    echo "Packed version ${release_version} does not match requested version ${release_version_override}." >&2
    exit 1
fi

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
bash "${repo_root}/eng/check-publication-readiness.sh"
write_evidence "${release_version}"
