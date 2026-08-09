#!/usr/bin/env bash

# This script is a fail-closed release orchestrator. Its order is intentional:
# validate source identity, produce fresh verification artifacts, validate the
# publication surface, and only then seal the complete directory with a
# manifest and detached checksum.
# Stage ordering, checkpoint ownership, and cleanup intentionally form one state
# machine here. Focused build, test, evidence, and publication work is delegated
# to the domain tools instead of duplicating orchestration state across scripts.

set -euo pipefail

export DOTNET_CLI_USE_MSBUILD_SERVER=0

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
deadline_module="eng.common.deadline"
stage_checkpoint_module="eng.release.checkpoint"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
spatial_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite"
spatial_project="${spatial_project}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
functional_test_project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests"
functional_test_project="${functional_test_project}/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"
specification_contract_project="${repo_root}/eng/tools/Doka.EntityFrameworkCore.MySql.SpecificationContract"
specification_contract_project="${specification_contract_project}/Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj"
release_candidate_run_id="${DOKA_RELEASE_CANDIDATE_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)}"
release_run_attempt="${DOKA_RELEASE_RUN_ATTEMPT:-1}"
release_runner_identity="${DOKA_RELEASE_RUNNER_IDENTITY:-local}"
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
selected_stage="all"
release_source_ref=""
release_tag=""
release_version=""
spatial_release_version=""

if (( $# > 0 )); then
    if [[ "$#" != "2" || "$1" != "--stage" ]]; then
        echo "Usage: $0 [--stage <stage>]" >&2
        exit 1
    fi

    selected_stage="$2"
fi

if [[ ! "${maximum_release_duration_seconds}" =~ ^[1-9][0-9]*$ ]]; then
    echo "DOKA_RELEASE_CANDIDATE_MAXIMUM_DURATION_SECONDS must be a positive integer." >&2
    exit 1
fi

if [[ "${resume_mode}" != "0" && "${resume_mode}" != "1" ]]; then
    echo "DOKA_RELEASE_CANDIDATE_RESUME must be 0 or 1." >&2
    exit 1
fi

if [[ ! "${release_run_attempt}" =~ ^[1-9][0-9]*$ ]]; then
    echo "DOKA_RELEASE_RUN_ATTEMPT must be a positive integer." >&2
    exit 1
fi

if [[ -z "${release_runner_identity}" \
    || ! "${release_runner_identity}" =~ ^[0-9A-Za-z._:/-]+$ ]]; then
    echo "DOKA_RELEASE_RUNNER_IDENTITY must be a non-empty ASCII identity." >&2
    exit 1
fi

# Own the entire orchestrator below one portable deadline. Individual gates
# may have their own tighter limits, but none may extend the candidate beyond
# this operator-visible upper bound or leave descendant processes behind.
if [[ "${DOKA_RELEASE_CANDIDATE_DEADLINE_ACTIVE:-0}" != "1" ]]; then
    export DOKA_RELEASE_CANDIDATE_DEADLINE_ACTIVE=1
    export DOKA_RELEASE_CANDIDATE_RUN_ID="${release_candidate_run_id}"
    exec python3 -m "${deadline_module}" \
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

    release_source_ref="${DOKA_RELEASE_EXPECTED_REF:-}"
    if [[ -z "${release_source_ref}" ]]; then
        release_source_ref="$(git symbolic-ref -q HEAD || true)"
    fi
    if [[ -z "${release_source_ref}" ]]; then
        release_source_ref="detached/$(git rev-parse HEAD)"
    fi

    if [[ "${require_release_tag}" != "1" ]]; then
        release_tag="not-required"
        return 0
    fi

    local version_tags
    version_tags="$(git tag --points-at HEAD | grep -E '^v[0-9]+[.][0-9]+[.][0-9]+([-.][0-9A-Za-z.-]+)?$' || true)"
    if [[ "$(printf '%s\n' "${version_tags}" | sed '/^$/d' | wc -l | tr -d ' ')" != "1" ]]; then
        echo "A release candidate must run from exactly one semantic version tag at HEAD." >&2
        exit 1
    fi

    release_tag="$(printf '%s\n' "${version_tags}" | sed '/^$/d')"
    if [[ -z "${release_version_override}" ]]; then
        release_version_override="${release_tag#v}"
    elif [[ "${release_tag}" != "v${release_version_override}" ]]; then
        echo "Release tag ${release_tag} does not match package version ${release_version_override}." >&2
        exit 1
    fi

    if [[ "${release_source_ref}" != "refs/tags/${release_tag}" ]]; then
        echo "Expected release ref ${release_source_ref} does not match refs/tags/${release_tag}." >&2
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

performance_contract_sha256() {
    python3 -c \
        'import hashlib, pathlib, sys; print(hashlib.sha256(pathlib.Path(sys.argv[1]).read_bytes()).hexdigest())' \
        "${repo_root}/benchmarks/performance-contract.json"
}

stage_is_complete() {
    local stage="$1"
    local checkpoint="${stage_checkpoint_dir}/${stage}.json"

    if [[ "${resume_mode}" != "1" || ! -f "${checkpoint}" ]]; then
        return 1
    fi

    local command=(
        python3 -m "${stage_checkpoint_module}" verify
        --repo "${repo_root}"
        --root "${release_candidate_dir}"
        --checkpoint-directory "${stage_checkpoint_dir}"
        --run-id "${release_candidate_run_id}"
        --source-ref "${release_source_ref}"
        --release-tag "${release_tag}"
        --maximum-run-attempt "${release_run_attempt}"
        --stage "${stage}"
    )
    if [[ "${stage}" == performance-* ]]; then
        command+=(
            --engine "${stage#performance-}"
            --contract-sha256 "$(performance_contract_sha256)"
        )
    fi

    if ! "${command[@]}"; then
        echo "Release-stage checkpoint '${stage}' is invalid; refusing an unsafe resume." >&2
        exit 1
    fi

    echo "Reusing verified release stage ${stage}."
    return 0
}

write_stage_checkpoint() {
    local stage="$1"
    local started_utc="$2"
    shift 2
    local command=(
        python3 -m "${stage_checkpoint_module}" write
        --repo "${repo_root}"
        --root "${release_candidate_dir}"
        --checkpoint-directory "${stage_checkpoint_dir}"
        --run-id "${release_candidate_run_id}"
        --source-ref "${release_source_ref}"
        --release-tag "${release_tag}"
        --run-attempt "${release_run_attempt}"
        --runner-identity "${release_runner_identity}"
        --started-utc "${started_utc}"
        --stage "${stage}"
    )
    local artifact

    if [[ "${stage}" == performance-* ]]; then
        command+=(
            --engine "${stage#performance-}"
            --contract-sha256 "$(performance_contract_sha256)"
        )
    fi

    for artifact in "$@"; do
        command+=(--artifact "${artifact}")
    done

    "${command[@]}"
}

verify_required_stage_set() {
    local include_complete="${1:-0}"
    local expected_stages=(
        quality
        repository-tests
        specification
        integration
        migration-deployment
        runtime
        coverage
        package
        sbom
        performance-mysql84
        performance-mariadb118
    )

    if [[ "${include_complete}" == "1" ]]; then
        expected_stages+=(complete)
    fi
    local command=(
        python3 -m "${stage_checkpoint_module}" verify-set
        --repo "${repo_root}"
        --root "${release_candidate_dir}"
        --checkpoint-directory "${stage_checkpoint_dir}"
        --run-id "${release_candidate_run_id}"
        --source-ref "${release_source_ref}"
        --release-tag "${release_tag}"
        --maximum-run-attempt "${release_run_attempt}"
        --performance-contract-sha256 "$(performance_contract_sha256)"
    )
    local stage

    for stage in "${expected_stages[@]}"; do
        command+=(--expected-stage "${stage}")
    done

    "${command[@]}"
}

archive_incomplete_stage() {
    local stage="$1"
    shift

    if [[ "${resume_mode}" != "1" ]]; then
        return 0
    fi

    local archive_directory
    archive_directory="${stage_checkpoint_dir}/incomplete/${stage}/$(date -u +%Y%m%dT%H%M%SZ)-$$"
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
    local matches

    # A package id can be the prefix of another one: the spatial package is
    # named after the provider. Requiring a digit where the version starts is
    # what separates them, because a NuGet version always begins with one.
    # Without it, which package answers depends on directory order.
    matches="$(
        find "${packages_dir}" \
            -maxdepth 1 \
            -type f \
            -name "${package_name}.[0-9]*.nupkg" \
            ! -name "*.symbols.nupkg" \
            | sort
    )"

    if [[ -z "${matches}" ]]; then
        echo "Unable to locate package '${package_name}' under ${packages_dir}." >&2
        exit 1
    fi

    if [[ "$(printf '%s\n' "${matches}" | wc -l | tr -d ' ')" != "1" ]]; then
        echo "Multiple packages match '${package_name}' under ${packages_dir}:" >&2
        printf '%s\n' "${matches}" | sed 's/^/  /' >&2
        echo "A release candidate qualifies exactly one build per package." >&2
        exit 1
    fi

    package_path="${matches}"
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
    mkdir -p "${sbom_components_dir}/runtime" "${sbom_components_dir}/spatial"

    dotnet restore "${runtime_project}" --tl:off
    dotnet restore "${spatial_project}" --tl:off

    # Bind the exact restored dependency graphs to the package stage. SBOM
    # generation must consume these immutable candidate-local copies rather
    # than mutable obj files that another build can replace later.
    cp \
        "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql/project.assets.json" \
        "${sbom_components_dir}/runtime/project.assets.json"
    cp \
        "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.NetTopologySuite/project.assets.json" \
        "${sbom_components_dir}/spatial/project.assets.json"

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
    bash "${repo_root}/eng/testing/check-spec-contract.sh"
    dotnet test "${functional_test_project}" \
        --configuration Release --no-build --no-restore --tl:off \
        --filter "FullyQualifiedName~SpecDispositionContractTests" \
        --logger trx \
        --results-directory "${specification_dir}/contract"
    bash "${repo_root}/eng/testing/check-spec-discovery.sh"

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
        bash "${repo_root}/eng/testing/check-spec-results.sh" \
            "${target}" \
            "${specification_dir}/${target}"
    done
}

run_repository_test_gate() {
    DOKA_COVERAGE_RESULTS_DIR="${coverage_input_dir}/repo-tests" \
        bash "${repo_root}/eng/testing/test.sh"
}

run_repository_quality_gate() {
    # Reuse the same complete contract as CI and pre-push while retaining its
    # audit output inside this candidate rather than in mutable shared storage.
    DOKA_QUALITY_AUDIT_DIR="${audit_dir}" \
        bash "${repo_root}/eng/quality/quality-gates.sh"
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
        bash "${repo_root}/eng/testing/test-integration.sh"

    # The examples are public product documentation. Execute their own
    # invariants against every supported engine after the infrastructure
    # contracts pass; this is intentionally a release gate, not a push gate.
    DOKA_EXAMPLE_RUN_ID="${release_candidate_run_id}" \
    DOKA_EXAMPLE_TARGETS="mysql84,mariadb114,mariadb118" \
    DOKA_EXAMPLE_EVIDENCE_DIR="${integration_dir}/examples" \
        bash "${repo_root}/eng/testing/test-examples.sh"
}

run_migration_deployment_gate() {
    DOKA_MIGRATION_DEPLOYMENT_RUN_ID="${release_candidate_run_id}" \
    DOKA_MIGRATION_DEPLOYMENT_EVIDENCE_ROOT="${migration_deployment_root}" \
        bash "${repo_root}/eng/testing/test-migration-deployment.sh"
}

run_runtime_posture_gate() {
    # The smoke executable is host-specific and is not a release payload. Its
    # digest, publish contract, execution result, source identity, and engine
    # identity are retained inside the portable candidate evidence instead.
    DOKA_RUNTIME_POSTURE_RUN_ID="${release_candidate_run_id}" \
    DOKA_RUNTIME_POSTURE_EVIDENCE_DIR="${runtime_dir}" \
    DOKA_RUNTIME_POSTURE_PUBLISH_DIR="${runtime_publish_dir}" \
    DOKA_RUNTIME_MYSQL_PORT=0 \
        bash "${repo_root}/eng/testing/test-runtime-posture.sh" --up-test-down
}

run_coverage_gate() {
    bash "${repo_root}/eng/quality/merge-coverage.sh" \
        "${release_candidate_dir}" \
        "${coverage_merged_dir}"
    bash "${repo_root}/eng/quality/check-coverage-threshold.sh" "${coverage_merged_dir}"
}

run_sbom() {
    local release_version="$1"
    local sbom_timeout="${DOKA_SBOM_TIMEOUT:-300}"
    local runtime_assets="${sbom_components_dir}/runtime/project.assets.json"
    local spatial_assets="${sbom_components_dir}/spatial/project.assets.json"

    mkdir -p "${sbom_dir}"

    if [[ ! -f "${runtime_assets}" || ! -f "${spatial_assets}" ]]; then
        echo "Release package dependency assets are missing; run_pack must complete before SBOM generation." >&2
        exit 1
    fi

    # NuGet records the absolute obj location inside project.assets.json. The
    # package and SBOM stages run in separate jobs, so restore that location
    # from the immutable candidate copy after validating every recorded path.
    # A fresh restore here could silently replace the qualified graph.
    python3 -m eng.release.sbom \
        --repository-root "${repo_root}" \
        --assets "${runtime_assets}" \
        --project "${runtime_project}" \
        --output-directory "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql"
    python3 -m eng.release.sbom \
        --repository-root "${repo_root}" \
        --assets "${spatial_assets}" \
        --project "${spatial_project}" \
        --output-directory "${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.NetTopologySuite"

    # Component detection consumes the exact restored graphs of the two
    # released packages. This excludes stale, test, and benchmark graphs while
    # retaining all direct and transitive package dependencies.
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
    if ! python3 -m "${deadline_module}" \
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
        echo "This changelog records specification, integration, live-example,"
        echo "migration deployment, runtime, coverage, package, audit, benchmark,"
        echo "and SBOM evidence."
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
    require_evidence_file "${integration_dir}/examples/live-example-matrix-evidence.json"
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
    { "id": "live-examples", "status": "pass" },
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

run_performance_engine() {
    local engine="$1"
    local skip="${DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS:-0}"
    local qualified_artifact_root="${DOKA_RELEASE_CANDIDATE_PERFORMANCE_ARTIFACT_ROOT:-}"
    local compose_run_id

    if [[ "${skip}" == "1" ]]; then
        echo "A release performance stage cannot be completed while benchmarks are skipped." >&2
        exit 1
    fi

    if [[ -n "${qualified_artifact_root}" ]]; then
        local selection="${qualified_artifact_root}/performance-attempt-selection.json"

        if [[ ! -f "${selection}" ]]; then
            echo "Qualified ${engine} performance selection is missing: ${selection}" >&2
            exit 1
        fi

        # The reusable scorecard already evaluated this exact artifact. Import
        # its digest-bound evidence instead of measuring or classifying it a
        # second time under the release-candidate run identifier.
        mkdir -p "${performance_dir}"
        python3 -m eng.performance.cli import-attempt-selection \
            --artifact-root "${qualified_artifact_root}" \
            --selection "${selection}" \
            --destination "${performance_dir}/${engine}" \
            --expected-target "${engine}" \
            --expected-commit "$(git rev-parse HEAD)"
        return
    fi

    compose_run_id="$(
        printf '%s' "${release_candidate_run_id}" \
            | tr '[:upper:]' '[:lower:]' \
            | tr '.' '-'
    )"

    echo "Running benchmark scorecard and soak evidence against ${engine}..."
    DOKA_BENCHMARK_PROFILE=scorecard \
        DOKA_BENCHMARK_TARGET="${engine}" \
        DOKA_BENCHMARK_RUN_ID="${release_candidate_run_id}" \
        DOKA_BENCHMARK_RESUME="${resume_mode}" \
        DOKA_BENCHMARK_COMPOSE_PROJECT_NAME="doka-benchmark-${compose_run_id}-${engine}" \
        DOKA_BENCHMARK_PORT=0 \
        "${repo_root}/eng/performance/benchmark.sh" --up-run-down

    mkdir -p "${performance_dir}"
    cp -R \
        "${repo_root}/artifacts/benchmarks/${engine}/reports/${release_candidate_run_id}" \
        "${performance_dir}/${engine}"
}

run_performance_mysql84() {
    run_performance_engine "mysql84"
}

run_performance_mariadb118() {
    run_performance_engine "mariadb118"
}

reuse_performance_evidence() {
    if [[ "${DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS:-0}" == "1" ]]; then
        echo "Performance evidence reuse and benchmark skipping cannot be combined." >&2
        exit 1
    fi

    if [[ -f "${stage_checkpoint_dir}/performance-mysql84.json" \
        || -f "${stage_checkpoint_dir}/performance-mariadb118.json" ]]; then
        echo "Performance reuse cannot overwrite an existing engine receipt." >&2
        exit 1
    fi

    archive_incomplete_stage "performance-reuse" "${performance_dir}"
    local started_utc
    started_utc="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

    # Reuse is not a trust-based copy. The evidence helper verifies both
    # strict scorecards, their artifact hashes, the original clean source
    # hash, Git ancestry, and the complete changed-path set. Any provider,
    # benchmark, dependency, build, or container input change fails closed.
    python3 -m eng.release.evidence reuse-performance \
        --repo "${repo_root}" \
        --source-root "${performance_reuse_source}" \
        --root "${release_candidate_dir}" \
        --run-id "${release_candidate_run_id}"

    write_stage_checkpoint \
        "performance-mysql84" \
        "${started_utc}" \
        "${performance_dir}/mysql84"
    write_stage_checkpoint \
        "performance-mariadb118" \
        "${started_utc}" \
        "${performance_dir}/mariadb118"
}

run_combined_performance_gate() {
    local mysql_import="${performance_dir}/mysql84/import-receipt.json"
    local mariadb_import="${performance_dir}/mariadb118/import-receipt.json"

    if [[ -f "${mysql_import}" && -f "${mariadb_import}" ]]; then
        echo "Verifying imported qualified performance evidence..."
        python3 -m eng.performance.cli verify-imported-attempt \
            --destination "${performance_dir}/mysql84" \
            --expected-target mysql84 \
            --expected-commit "$(git rev-parse HEAD)"
        python3 -m eng.performance.cli verify-imported-attempt \
            --destination "${performance_dir}/mariadb118" \
            --expected-target mariadb118 \
            --expected-commit "$(git rev-parse HEAD)"
        return
    fi

    if [[ -f "${mysql_import}" || -f "${mariadb_import}" ]]; then
        echo "Imported performance evidence is incomplete across engines." >&2
        return 1
    fi

    local scratch_root
    scratch_root="$(mktemp -d "${TMPDIR:-/tmp}/doka-release-performance.XXXXXX")"
    mkdir -p "${scratch_root}/mysql84/reports" "${scratch_root}/mariadb118/reports"
    cp -R \
        "${performance_dir}/mysql84" \
        "${scratch_root}/mysql84/reports/${release_candidate_run_id}"
    cp -R \
        "${performance_dir}/mariadb118" \
        "${scratch_root}/mariadb118/reports/${release_candidate_run_id}"

    echo "Re-evaluating the complete performance and memory gate..."
    if ! DOKA_BENCHMARK_PROFILE=scorecard \
        DOKA_BENCHMARK_GATE_RUN_ID="${release_candidate_run_id}" \
        bash "${repo_root}/eng/performance/check-benchmark-ratios.sh" \
            "${scratch_root}"; then
        rm -rf -- "${scratch_root}"
        return 1
    fi

    rm -rf -- "${scratch_root}"
}

resolve_release_version() {
    release_version="$(package_version_from_file "Doka.EntityFrameworkCore.MySql")"
    spatial_release_version="$(
        package_version_from_file "Doka.EntityFrameworkCore.MySql.NetTopologySuite"
    )"

    if [[ "${spatial_release_version}" != "${release_version}" ]]; then
        echo "Provider package version ${release_version} does not match spatial package ${spatial_release_version}." >&2
        exit 1
    fi

    if [[ -n "${release_version_override}" \
        && "${release_version}" != "${release_version_override}" ]]; then
        echo "Packed version ${release_version} does not match requested version ${release_version_override}." >&2
        exit 1
    fi
}

run_sbom_stage() {
    resolve_release_version
    run_sbom "${release_version}"
}

run_finalization_stage() {
    # Finalization consumes only verified stage receipts. It never infers
    # completion from a directory that merely happens to contain files.
    verify_required_stage_set
    run_combined_performance_gate
    resolve_release_version
    write_changelog "${release_version}"

    local package_count
    package_count="$(
        find "${packages_dir}" \
            -maxdepth 1 \
            -type f \
            -name '*.nupkg' \
            ! -name '*.symbols.nupkg' \
            | wc -l \
            | tr -d ' '
    )"
    local sbom_file_count
    sbom_file_count="$(find "${sbom_dir}" -type f | wc -l | tr -d ' ')"

    if [[ "${package_count}" -lt 2 ]]; then
        echo "Expected release-candidate packaging to produce both provider packages." >&2
        exit 1
    fi

    if [[ "${sbom_file_count}" -eq 0 ]]; then
        echo "No SBOM files were generated under ${sbom_dir}." >&2
        exit 1
    fi

    bash "${repo_root}/eng/release/check-publication-readiness.sh"
    write_reconciliation
    write_summary "${release_version}" "${package_count}"
    write_evidence "${release_version}"
}

run_named_stage() {
    local stage="$1"
    local runner="$2"
    shift 2

    if stage_is_complete "${stage}"; then
        return 0
    fi

    archive_incomplete_stage "${stage}" "$@"
    local started_utc
    started_utc="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
    "${runner}"
    write_stage_checkpoint "${stage}" "${started_utc}" "$@"
}

run_all_stages() {
    # The monolithic local path shares one host across every stage. Capture
    # performance evidence before build and database gates can warm caches or
    # introduce competing load. Hosted qualification additionally isolates
    # each performance engine in its own matrix job.
    if [[ -n "${performance_reuse_source}" ]]; then
        local mysql_performance_complete=0
        local mariadb_performance_complete=0

        if stage_is_complete "performance-mysql84"; then
            mysql_performance_complete=1
        fi
        if stage_is_complete "performance-mariadb118"; then
            mariadb_performance_complete=1
        fi

        if [[ "${mysql_performance_complete}" != "${mariadb_performance_complete}" ]]; then
            echo "Reused performance evidence must have checkpoints for both engines or neither engine." >&2
            exit 1
        fi
        if [[ "${mysql_performance_complete}" == "0" ]]; then
            reuse_performance_evidence
        fi
    else
        run_named_stage \
            "performance-mysql84" \
            run_performance_mysql84 \
            "${performance_dir}/mysql84"
        run_named_stage \
            "performance-mariadb118" \
            run_performance_mariadb118 \
            "${performance_dir}/mariadb118"
    fi

    run_named_stage "quality" run_repository_quality_gate "${audit_dir}"
    run_named_stage \
        "repository-tests" \
        run_repository_test_gate \
        "${coverage_input_dir}/repo-tests"
    run_named_stage "specification" run_specification_gate "${specification_dir}"
    run_named_stage \
        "integration" \
        run_integration_configuration_and_failure_gate \
        "${integration_dir}" \
        "${coverage_input_dir}/integration"
    run_named_stage \
        "migration-deployment" \
        run_migration_deployment_gate \
        "${migration_deployment_root}"
    run_named_stage "runtime" run_runtime_posture_gate "${runtime_dir}"
    run_named_stage "coverage" run_coverage_gate "${coverage_merged_dir}"
    run_named_stage \
        "package" \
        run_pack \
        "${packages_dir}" \
        "${dependency_graph_file}" \
        "${sbom_components_dir}"
    run_named_stage "sbom" run_sbom_stage "${sbom_dir}"

    run_named_stage \
        "complete" \
        run_finalization_stage \
        "${changelog_file}" \
        "${summary_file}" \
        "${reconciliation_file}" \
        "${release_candidate_dir}/release-candidate-evidence.json" \
        "${release_candidate_dir}/release-candidate-evidence.sha256"
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
    python3 -m eng.release.evidence "${arguments[@]}"

    python3 -m eng.release.evidence verify \
        --root "${release_candidate_dir}" \
        --repo "${repo_root}"
}

require_command jq
require_command python3
cd "${repo_root}"

case "${selected_stage}" in
    all | quality | repository-tests | specification | integration \
        | migration-deployment | runtime | coverage | package | sbom \
        | performance-mysql84 | performance-mariadb118 | finalize)
        ;;
    *)
        echo "Unknown release-candidate stage '${selected_stage}'." >&2
        exit 1
        ;;
esac

# Do not reorder these gates without updating the evidence contract. The final
# manifest inventory must observe every retained artifact and must be written
# only after publication readiness has passed.
validate_release_source
prepare_release_directory

if stage_is_complete "complete"; then
    verify_required_stage_set 1
    python3 -m eng.release.evidence verify \
        --root "${release_candidate_dir}" \
        --repo "${repo_root}"
    echo "Release candidate ${release_candidate_run_id} is already complete and verified."
    exit 0
fi

"${repo_root}/eng/common/verify-dotnet.sh"
if [[ "${selected_stage}" == "all" || "${selected_stage}" == "quality" ]]; then
    "${repo_root}/eng/quality/validate-adrs.sh"
fi

case "${selected_stage}" in
    all)
        run_all_stages
        ;;
    quality)
        run_named_stage "quality" run_repository_quality_gate "${audit_dir}"
        ;;
    repository-tests)
        run_named_stage \
            "repository-tests" \
            run_repository_test_gate \
            "${coverage_input_dir}/repo-tests"
        ;;
    specification)
        run_named_stage "specification" run_specification_gate "${specification_dir}"
        ;;
    integration)
        run_named_stage \
            "integration" \
            run_integration_configuration_and_failure_gate \
            "${integration_dir}" \
            "${coverage_input_dir}/integration"
        ;;
    migration-deployment)
        run_named_stage \
            "migration-deployment" \
            run_migration_deployment_gate \
            "${migration_deployment_root}"
        ;;
    runtime)
        run_named_stage "runtime" run_runtime_posture_gate "${runtime_dir}"
        ;;
    coverage)
        run_named_stage "coverage" run_coverage_gate "${coverage_merged_dir}"
        ;;
    package)
        run_named_stage \
            "package" \
            run_pack \
            "${packages_dir}" \
            "${dependency_graph_file}" \
            "${sbom_components_dir}"
        ;;
    sbom)
        run_named_stage "sbom" run_sbom_stage "${sbom_dir}"
        ;;
    performance-mysql84)
        run_named_stage \
            "performance-mysql84" \
            run_performance_mysql84 \
            "${performance_dir}/mysql84"
        ;;
    performance-mariadb118)
        run_named_stage \
            "performance-mariadb118" \
            run_performance_mariadb118 \
            "${performance_dir}/mariadb118"
        ;;
    finalize)
        run_named_stage \
            "complete" \
            run_finalization_stage \
            "${changelog_file}" \
            "${summary_file}" \
            "${reconciliation_file}" \
            "${release_candidate_dir}/release-candidate-evidence.json" \
            "${release_candidate_dir}/release-candidate-evidence.sha256"
        ;;
esac
