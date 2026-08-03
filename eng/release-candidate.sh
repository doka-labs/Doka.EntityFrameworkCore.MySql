#!/usr/bin/env bash

# This script is a fail-closed release orchestrator. Its order is intentional:
# validate source identity, produce fresh verification artifacts, validate the
# publication surface, and only then seal the complete directory with a
# manifest and detached checksum.

set -euo pipefail

export DOTNET_CLI_USE_MSBUILD_SERVER=0

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
deadline_tool="${repo_root}/eng/run_with_deadline.py"
stage_checkpoint_tool="${repo_root}/eng/release_stage_checkpoint.py"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
spatial_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite"
spatial_project="${spatial_project}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
functional_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests"
functional_test_project="${functional_test_project}/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
specification_contract_project="${repo_root}/eng/Doka.EntityFrameworkCore.MySql.SpecificationContract"
specification_contract_project="${specification_contract_project}/Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj"
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
runtime_dir="${release_candidate_dir}/runtime"
runtime_publish_dir="${repo_root}/artifacts/runtime-smoke/${release_candidate_run_id}/trimmed"
performance_dir="${release_candidate_dir}/performance"
reconciliation_file="${release_candidate_dir}/release-candidate-reconciliation.json"
stage_checkpoint_dir="${repo_root}/artifacts/release-candidate-checkpoints/${release_candidate_run_id}"
require_release_tag="${DOKA_RELEASE_REQUIRE_TAG:-1}"
release_version_override="${DOKA_RELEASE_VERSION:-}"
performance_reuse_source="${DOKA_RELEASE_CANDIDATE_REUSE_PERFORMANCE_FROM:-}"
resume_mode="${DOKA_RELEASE_CANDIDATE_RESUME:-0}"
maximum_release_duration_seconds="${DOKA_RELEASE_CANDIDATE_MAXIMUM_DURATION_SECONDS:-7200}"

if [[ ! "${maximum_release_duration_seconds}" =~ ^[1-9][0-9]*$ ]]; then
    echo "DOKA_RELEASE_CANDIDATE_MAXIMUM_DURATION_SECONDS must be a positive integer." >&2
    exit 1
fi

if [[ "${resume_mode}" != "0" && "${resume_mode}" != "1" ]]; then
    echo "DOKA_RELEASE_CANDIDATE_RESUME must be 0 or 1." >&2
    exit 1
fi

# Own the entire orchestrator below one portable deadline. Individual gates
# may have their own tighter limits, but none may extend the candidate beyond
# this operator-visible upper bound or leave descendant processes behind.
if [[ "${DOKA_RELEASE_CANDIDATE_DEADLINE_ACTIVE:-0}" != "1" ]]; then
    export DOKA_RELEASE_CANDIDATE_DEADLINE_ACTIVE=1
    export DOKA_RELEASE_CANDIDATE_RUN_ID="${release_candidate_run_id}"
    exec python3 "${deadline_tool}" \
        --seconds "${maximum_release_duration_seconds}" \
        --label "release-candidate qualification" \
        --termination-grace-seconds 15 \
        -- bash "$0" "$@"
fi

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
        if [[ "${resume_mode}" != "1" ]]; then
            echo "Release-candidate directory already contains evidence: ${release_candidate_dir}" >&2
            echo "Use a new run ID, or explicitly resume it with DOKA_RELEASE_CANDIDATE_RESUME=1." >&2
            exit 1
        fi

        echo "Resuming release-candidate run ${release_candidate_run_id}."
    elif [[ "${resume_mode}" != "1" \
        && -d "${stage_checkpoint_dir}" \
        && -n "$(find "${stage_checkpoint_dir}" -mindepth 1 -print -quit)" ]]; then
        echo "Release-stage checkpoints already exist for run ${release_candidate_run_id}." >&2
        echo "Use a new run ID, or explicitly resume it." >&2
        exit 1
    fi

    mkdir -p "${release_candidate_dir}"
    mkdir -p "${stage_checkpoint_dir}"
}

require_command() {
    local command_name="$1"

    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Required command '${command_name}' is not available." >&2
        exit 1
    fi
}

stage_is_complete() {
    local stage="$1"
    local checkpoint="${stage_checkpoint_dir}/${stage}.json"

    if [[ "${resume_mode}" != "1" || ! -f "${checkpoint}" ]]; then
        return 1
    fi

    if ! python3 "${stage_checkpoint_tool}" verify \
        --repo "${repo_root}" \
        --root "${release_candidate_dir}" \
        --checkpoint-directory "${stage_checkpoint_dir}" \
        --run-id "${release_candidate_run_id}" \
        --stage "${stage}"; then
        echo "Release-stage checkpoint '${stage}' is invalid; refusing an unsafe resume." >&2
        exit 1
    fi

    echo "Reusing verified release stage ${stage}."
    return 0
}

write_stage_checkpoint() {
    local stage="$1"
    shift
    local command=(
        python3 "${stage_checkpoint_tool}" write
        --repo "${repo_root}"
        --root "${release_candidate_dir}"
        --checkpoint-directory "${stage_checkpoint_dir}"
        --run-id "${release_candidate_run_id}"
        --stage "${stage}"
    )
    local artifact

    for artifact in "$@"; do
        command+=(--artifact "${artifact}")
    done

    "${command[@]}"
}

archive_incomplete_stage() {
    local stage="$1"
    shift

    if [[ "${resume_mode}" != "1" ]]; then
        return 0
    fi

    local archive_directory="${stage_checkpoint_dir}/incomplete/${stage}/$(date -u +%Y%m%dT%H%M%SZ)-$$"
    local artifact
    local index=0

    for artifact in "$@"; do
        if [[ ! -e "${artifact}" ]]; then
            continue
        fi

        mkdir -p "${archive_directory}"
        mv "${artifact}" "${archive_directory}/${index}-$(basename "${artifact}")"
        index=$(( index + 1 ))
    done

    if (( index > 0 )); then
        echo "Archived incomplete ${stage} artifacts under ${archive_directory}."
    fi
}

package_version_from_file() {
    local package_name="$1"
    local package_path

    package_path="$(
        find "${packages_dir}" \
            -maxdepth 1 \
            -type f \
            -name "${package_name}.*.nupkg" \
            ! -name "*.symbols.nupkg" \
            | head -n 1
    )"

    if [[ -z "${package_path}" ]]; then
        echo "Unable to locate package '${package_name}' under ${packages_dir}." >&2
        exit 1
    fi

    basename "${package_path}" | sed -E "s/^${package_name//./\\.}\.([0-9A-Za-z.-]+)\.nupkg$/\1/"
}

run_with_release_version() {
    local command_arguments=("$@")

    # Bash 3.2 treats an empty-array expansion as an unbound variable under
    # nounset. Keep the command array non-empty and append the optional MSBuild
    # property only when a release version was explicitly supplied.
    if [[ -n "${release_version_override}" ]]; then
        command_arguments+=("-p:PackageVersion=${release_version_override}")
    fi

    "${command_arguments[@]}"
}

run_pack() {
    mkdir -p "${packages_dir}"

    dotnet restore "${runtime_project}" --tl:off
    dotnet restore "${spatial_project}" --tl:off
    run_with_release_version \
        dotnet build "${runtime_project}" --configuration Release --no-restore --tl:off -m:1
    run_with_release_version \
        dotnet build "${spatial_project}" --configuration Release --no-restore --tl:off -m:1
    run_with_release_version \
        dotnet pack "${runtime_project}" --configuration Release --no-build --no-restore \
        --output "${packages_dir}" --tl:off
    run_with_release_version \
        dotnet pack "${spatial_project}" --configuration Release --no-build --no-restore \
        --output "${packages_dir}" --tl:off

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

run_repository_quality_gate() {
    # Reuse the same complete contract as CI and pre-push while retaining its
    # audit output inside this candidate rather than in mutable shared storage.
    DOKA_QUALITY_AUDIT_DIR="${audit_dir}" \
        bash "${repo_root}/eng/quality-gates.sh"
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

run_runtime_posture_gate() {
    # The smoke executable is host-specific and is not a release payload. Its
    # digest, publish contract, execution result, source identity, and engine
    # identity are retained inside the portable candidate evidence instead.
    DOKA_RUNTIME_POSTURE_RUN_ID="${release_candidate_run_id}" \
    DOKA_RUNTIME_POSTURE_EVIDENCE_DIR="${runtime_dir}" \
    DOKA_RUNTIME_POSTURE_PUBLISH_DIR="${runtime_publish_dir}" \
    DOKA_RUNTIME_MYSQL_PORT=0 \
        bash "${repo_root}/eng/test-runtime-posture.sh" --up-test-down
}

run_coverage_gate() {
    bash "${repo_root}/eng/merge-coverage.sh" \
        "${release_candidate_dir}" \
        "${coverage_merged_dir}"
    bash "${repo_root}/eng/check-coverage-threshold.sh" "${coverage_merged_dir}"
}

run_sbom() {
    local release_version="$1"
    local sbom_timeout="${DOKA_SBOM_TIMEOUT:-300}"
    local runtime_assets="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql/project.assets.json"
    local spatial_assets="${repo_root}/artifacts/obj"
    spatial_assets="${spatial_assets}/Doka.EntityFrameworkCore.MySql.NetTopologySuite/project.assets.json"

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

    echo "Running SBOM generation with a ${sbom_timeout}-second deadline..."
    if ! python3 "${deadline_tool}" \
        --seconds "${sbom_timeout}" \
        --label "SBOM generation" \
        -- "${sbom_cmd[@]}"; then
        echo "SBOM generation failed or exceeded ${sbom_timeout} seconds." >&2
        exit 1
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
        echo "This changelog records specification, integration, migration deployment,"
        echo "runtime, coverage, package, audit, benchmark, and SBOM evidence."
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
        echo "- runtimeDirectory: runtime"
        echo "- coverageDirectory: coverage-merged"
        echo "- performanceDirectory: performance"
        echo "- reconciliationFile: release-candidate-reconciliation.json"
        echo "- changelogFile: release-candidate-changelog.md"
        echo "- evidenceFile: release-candidate-evidence.json"
        echo "- evidenceChecksumFile: release-candidate-evidence.sha256"
        echo "- packageCount: ${package_count}"
        echo
        echo "The manifest uses portable paths and verifies every retained artifact before upload."
    } > "${summary_file}"
}

require_evidence_file() {
    local path="$1"

    if [[ ! -f "${path}" || -L "${path}" ]]; then
        echo "Required release-candidate evidence is missing or non-regular: ${path}" >&2
        exit 1
    fi
}

require_evidence_directory() {
    local path="$1"

    if [[ ! -d "${path}" || -z "$(find "${path}" -type f -print -quit)" ]]; then
        echo "Required release-candidate evidence directory is empty or missing: ${path}" >&2
        exit 1
    fi
}

write_reconciliation() {
    # Reconcile every named release contract only after its executable gate has
    # passed. The manifest independently validates this index before sealing it.
    require_evidence_directory "${coverage_input_dir}/repo-tests"
    require_evidence_file "${coverage_merged_dir}/coverage.cobertura.xml"
    require_evidence_file "${specification_dir}/mysql84/test-database-evidence.json"
    require_evidence_file "${specification_dir}/mariadb114/test-database-evidence.json"
    require_evidence_file "${specification_dir}/mariadb118/test-database-evidence.json"
    require_evidence_file "${integration_dir}/compatibility-matrix-evidence.json"
    require_evidence_file \
        "${migration_deployment_root}/${release_candidate_run_id}/migration-deployment-evidence.json"
    require_evidence_file "${runtime_dir}/runtime-posture-evidence.json"
    require_evidence_directory "${packages_dir}"
    require_evidence_directory "${audit_dir}"
    require_evidence_directory "${sbom_dir}"
    require_evidence_file "${performance_dir}/mysql84/evidence/gate-performance-evaluation.json"
    require_evidence_file "${performance_dir}/mariadb118/evidence/gate-performance-evaluation.json"

    cat > "${reconciliation_file}" <<EOF
{
  "schemaVersion": 1,
  "generatedUtc": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "runId": "${release_candidate_run_id}",
  "sourceCommit": "$(git rev-parse HEAD)",
  "gates": [
    { "id": "source-identity", "status": "pass" },
    { "id": "adr-validation", "status": "pass" },
    { "id": "repository-quality", "status": "pass" },
    { "id": "repository-tests", "status": "pass" },
    { "id": "live-specification", "status": "pass" },
    { "id": "integration-configuration-failure", "status": "pass" },
    { "id": "migration-deployment", "status": "pass" },
    { "id": "runtime-full-trim", "status": "pass" },
    { "id": "coverage-union", "status": "pass" },
    { "id": "package-contract", "status": "pass" },
    { "id": "vulnerability-audit", "status": "pass" },
    { "id": "sbom", "status": "pass" },
    { "id": "performance-memory", "status": "pass" },
    { "id": "publication-readiness", "status": "pass" }
  ]
}
EOF
}

run_benchmark_and_gate() {
    local skip="${DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS:-0}"
    local compose_run_id

    if [[ -n "${performance_reuse_source}" ]]; then
        if [[ "${skip}" == "1" ]]; then
            echo "Performance evidence reuse and benchmark skipping cannot be combined." >&2
            exit 1
        fi

        archive_incomplete_stage "performance-reuse" "${performance_dir}"

        # Reuse is not a trust-based copy. The evidence helper verifies both
        # strict scorecards, their artifact hashes, the original clean source
        # hash, Git ancestry, and the complete changed-path set. Any provider,
        # benchmark, dependency, build, or container input change fails closed.
        python3 "${repo_root}/eng/release_evidence.py" reuse-performance \
            --repo "${repo_root}" \
            --source-root "${performance_reuse_source}" \
            --root "${release_candidate_dir}" \
            --run-id "${release_candidate_run_id}"
        return 0
    fi

    if [[ "${skip}" == "1" ]]; then
        echo "Benchmark gate skipped via DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS=1."
        echo "This bypass is for dev-loop iteration only; the resulting evidence is not release-eligible." >&2
        return 0
    fi

    local engines=("mysql84" "mariadb118")
    local engine_stage
    compose_run_id="$(
        printf '%s' "${release_candidate_run_id}" \
            | tr '[:upper:]' '[:lower:]' \
            | tr '.' '-'
    )"

    for engine in "${engines[@]}"; do
        engine_stage="performance-${engine}"
        if stage_is_complete "${engine_stage}"; then
            continue
        fi

        archive_incomplete_stage \
            "${engine_stage}" \
            "${performance_dir}/${engine}"
        echo "Running benchmark scorecard and soak evidence against ${engine}..."
        DOKA_BENCHMARK_PROFILE=scorecard \
            DOKA_BENCHMARK_TARGET="${engine}" \
            DOKA_BENCHMARK_RUN_ID="${release_candidate_run_id}" \
            DOKA_BENCHMARK_RESUME="${resume_mode}" \
            DOKA_BENCHMARK_COMPOSE_PROJECT_NAME="doka-benchmark-${compose_run_id}-${engine}" \
            DOKA_BENCHMARK_PORT=0 \
            "${repo_root}/eng/benchmark.sh" --up-run-down

        mkdir -p "${performance_dir}"
        cp -R \
            "${repo_root}/artifacts/benchmarks/${engine}/reports/${release_candidate_run_id}" \
            "${performance_dir}/${engine}"
        write_stage_checkpoint \
            "${engine_stage}" \
            "${performance_dir}/${engine}"
    done

    echo "Re-evaluating the complete performance and memory gate..."
    DOKA_BENCHMARK_PROFILE=scorecard \
        DOKA_BENCHMARK_GATE_STRICT=1 \
        DOKA_BENCHMARK_GATE_RUN_ID="${release_candidate_run_id}" \
        bash "${repo_root}/eng/check-benchmark-ratios.sh" \
            "${repo_root}/artifacts/benchmarks"

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

if stage_is_complete "complete"; then
    python3 "${repo_root}/eng/release_evidence.py" verify \
        --root "${release_candidate_dir}" \
        --repo "${repo_root}"
    echo "Release candidate ${release_candidate_run_id} is already complete and verified."
    exit 0
fi

"${repo_root}/eng/verify-dotnet.sh"
"${repo_root}/eng/validate-adrs.sh"

if ! stage_is_complete "performance"; then
    run_benchmark_and_gate
    write_stage_checkpoint "performance" "${performance_dir}"
fi

if ! stage_is_complete "quality"; then
    archive_incomplete_stage "quality" "${audit_dir}"
    run_repository_quality_gate
    write_stage_checkpoint "quality" "${audit_dir}"
fi

if ! stage_is_complete "repository-tests"; then
    archive_incomplete_stage \
        "repository-tests" \
        "${coverage_input_dir}/repo-tests"
    run_repository_test_gate
    write_stage_checkpoint \
        "repository-tests" \
        "${coverage_input_dir}/repo-tests"
fi

if ! stage_is_complete "specification"; then
    archive_incomplete_stage "specification" "${specification_dir}"
    run_specification_gate
    write_stage_checkpoint "specification" "${specification_dir}"
fi

if ! stage_is_complete "integration"; then
    archive_incomplete_stage \
        "integration" \
        "${integration_dir}" \
        "${coverage_input_dir}/integration"
    run_integration_configuration_and_failure_gate
    write_stage_checkpoint \
        "integration" \
        "${integration_dir}" \
        "${coverage_input_dir}/integration"
fi

if ! stage_is_complete "migration-deployment"; then
    archive_incomplete_stage \
        "migration-deployment" \
        "${migration_deployment_root}"
    run_migration_deployment_gate
    write_stage_checkpoint \
        "migration-deployment" \
        "${migration_deployment_root}"
fi

if ! stage_is_complete "runtime"; then
    archive_incomplete_stage "runtime" "${runtime_dir}"
    run_runtime_posture_gate
    write_stage_checkpoint "runtime" "${runtime_dir}"
fi

if ! stage_is_complete "coverage"; then
    archive_incomplete_stage "coverage" "${coverage_merged_dir}"
    run_coverage_gate
    write_stage_checkpoint "coverage" "${coverage_merged_dir}"
fi

if ! stage_is_complete "package"; then
    archive_incomplete_stage \
        "package" \
        "${packages_dir}" \
        "${dependency_graph_file}"
    run_pack
    write_stage_checkpoint \
        "package" \
        "${packages_dir}" \
        "${dependency_graph_file}"
fi

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

if ! stage_is_complete "sbom"; then
    archive_incomplete_stage \
        "sbom" \
        "${sbom_dir}" \
        "${sbom_components_dir}"
    run_sbom "${release_version}"
    write_stage_checkpoint \
        "sbom" \
        "${sbom_dir}" \
        "${sbom_components_dir}"
fi

archive_incomplete_stage \
    "finalization" \
    "${changelog_file}" \
    "${summary_file}" \
    "${reconciliation_file}" \
    "${release_candidate_dir}/release-candidate-evidence.json" \
    "${release_candidate_dir}/release-candidate-evidence.sha256"
write_changelog "${release_version}"

package_count="$(
    find "${packages_dir}" \
        -maxdepth 1 \
        -type f \
        -name '*.nupkg' \
        ! -name '*.symbols.nupkg' \
        | wc -l \
        | tr -d ' '
)"
sbom_file_count="$(find "${sbom_dir}" -type f | wc -l | tr -d ' ')"

if [[ "${package_count}" -lt 2 ]]; then
    echo "Expected release-candidate packaging to produce both provider packages." >&2
    exit 1
fi

if [[ "${sbom_file_count}" -eq 0 ]]; then
    echo "No SBOM files were generated under ${sbom_dir}." >&2
    exit 1
fi

bash "${repo_root}/eng/check-publication-readiness.sh"
write_reconciliation
write_summary "${release_version}" "${package_count}"
write_evidence "${release_version}"
write_stage_checkpoint \
    "complete" \
    "${changelog_file}" \
    "${summary_file}" \
    "${reconciliation_file}" \
    "${release_candidate_dir}/release-candidate-evidence.json" \
    "${release_candidate_dir}/release-candidate-evidence.sha256"
