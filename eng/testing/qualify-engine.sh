#!/usr/bin/env bash

# Runs the existing reversible engine contracts for either PR or RC callers.
# Release admission, stage receipts and publication remain owned by the RC wrapper.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [[ "$#" -eq 1 && ( "$1" == "--list" || "$1" == "--all-stages" ) ]]; then
    # WHY: The branch policy owns applicability; a missing script must not
    # silently remove a required producer. Preview lines omit the EF range gate.
    engine_contracts="$(jq -ce '
        [.gates[] | select(.kind == "candidate-produced")
         | {stage: (.id
             | if . == "runtime-posture" then "runtime"
               elif . == "migration-deployment" or . == "efcore-patch-matrix"
                 or . == "mysqlconnector-patch-matrix" then .
               else error("Unknown candidate-produced qualification gate.") end),
            implementation}
         | if (.implementation
               | if type == "string" then test("\\Aeng/testing/test-[a-z0-9-]+[.]sh\\z") else false end)
           then . else error("Invalid qualification implementation field.") end]
        | if ([.[].stage] | length == (unique | length)
             and contains(["migration-deployment", "runtime", "mysqlconnector-patch-matrix"]))
          then . else error("Incomplete or duplicate qualification gates.") end
    ' "${repo_root}/eng/release/evidence-policy.json")"

    while IFS=$'\t' read -r required_stage declared_implementation; do
        case "${required_stage}" in
            runtime) implementation="runtime-posture" ;;
            efcore-patch-matrix) implementation="efcore-matrix" ;;
            mysqlconnector-patch-matrix) implementation="mysqlconnector-matrix" ;;
            *) implementation="${required_stage}" ;;
        esac

        implementation="eng/testing/test-${implementation}.sh"
        # WHY: Policy must describe the bounded producer that this stage actually dispatches.
        if [[ "${declared_implementation}" != "${implementation}" ]]; then
            echo "Qualification policy implementation does not match dispatched producer: ${required_stage}" >&2
            exit 2
        fi

        implementation="${repo_root}/${implementation}"
        if [[ ! -f "${implementation}" || -L "${implementation}" ]]; then
            echo "Required qualification implementation is missing or non-regular: ${implementation}" >&2
            exit 2
        fi
    done < <(jq -r '.[] | [.stage, .implementation] | @tsv' <<< "${engine_contracts}")

    engine_stages="$(jq -c '[.[].stage]' <<< "${engine_contracts}")"

    if [[ "$1" == "--all-stages" ]]; then
        jq -c '. + ["package", "sbom"]' <<< "${engine_stages}"
    else
        printf '%s\n' "${engine_stages}"
    fi

    exit 0
fi

if [[ "$#" -ne 3 ]]; then
    echo "Usage: $0 <stage> <output-root> <run-id> | --list | --all-stages" >&2
    exit 2
fi

stage="$1"
release_candidate_dir="${2%/}"
release_candidate_run_id="$3"

case "${stage}" in
    efcore-patch-matrix)
        # WHY: A branch without the stable EF contract cannot run this stage;
        # reject the request before SDK verification or producer setup.
        if [[ ! -f "${repo_root}/eng/testing/test-efcore-matrix.sh" \
            || -L "${repo_root}/eng/testing/test-efcore-matrix.sh" ]]; then
            echo "This branch has no EF dependency-range qualification stage." >&2
            exit 2
        fi
        ;;
    integration | migration-deployment | runtime | mysqlconnector-patch-matrix) ;;
    *) echo "Unknown qualification engine stage '${stage}'." >&2; exit 2 ;;
esac

if [[ ! "${release_candidate_run_id}" =~ ^[0-9A-Za-z][0-9A-Za-z._-]*$ ]]; then
    echo "Qualification run ID must be a path-safe ASCII identity." >&2
    exit 2
fi

if [[ "${release_candidate_dir}" != /* ]]; then
    release_candidate_dir="${repo_root}/${release_candidate_dir}"
fi

# WHY: A single run-owned directory prevents aliases or intermediate symlinks
# from redirecting destructive cleanup into another qualification's evidence.
if [[ "${release_candidate_dir}" != "${repo_root}/artifacts/release-candidate/${release_candidate_run_id}" ]]; then
    echo "Qualification output must be artifacts/release-candidate/<run-id>." >&2
    exit 2
fi

if [[ "${release_candidate_dir}" == *"/../"* || "${release_candidate_dir}" == *"/./"* \
    || "${release_candidate_dir}" == */.. || -L "${repo_root}/artifacts" \
    || -L "${repo_root}/artifacts/release-candidate" || -L "${release_candidate_dir}" ]]; then
    echo "Qualification output must not traverse or use a symbolic link." >&2
    exit 2
fi

coverage_input_dir="${release_candidate_dir}/coverage-input"
integration_dir="${release_candidate_dir}/integration"
migration_deployment_root="${release_candidate_dir}/migration-deployment"
runtime_dir="${release_candidate_dir}/runtime"
runtime_publish_dir="${repo_root}/artifacts/runtime-smoke/${release_candidate_run_id}/trimmed"
efcore_matrix_dir="${release_candidate_dir}/efcore-patch-matrix"
mysqlconnector_matrix_dir="${release_candidate_dir}/mysqlconnector-patch-matrix"

run_integration_configuration_and_failure_gate() {
    # Route every integration artifact into the candidate root. Files outside
    # this root cannot be hashed into the release manifest. The required-matrix
    # flag also prevents a future caller from narrowing either the engine set or
    # the test categories while still claiming release-candidate coverage.
    DOKA_COVERAGE_RESULTS_DIR="${coverage_input_dir}/integration" \
    DOKA_INTEGRATION_ARTIFACTS_DIR="${integration_dir}" \
    DOKA_INTEGRATION_RUN_ID="${release_candidate_run_id}" \
    DOKA_INTEGRATION_TARGETS="mysql84,mysql97,mariadb1011,mariadb114,mariadb118,mariadb123" \
    DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX=1 \
    DOKA_TEST_DATABASE_EVIDENCE_FILE="${integration_dir}/test-database-evidence.json" \
        bash "${repo_root}/eng/testing/test-integration.sh"

    # The examples are public product documentation. Execute their own
    # invariants against every supported engine after the infrastructure
    # contracts pass. PR and RC callers must exercise this same complete set.
    DOKA_EXAMPLE_RUN_ID="${release_candidate_run_id}" \
    DOKA_EXAMPLE_TARGETS="mysql84,mysql97,mariadb1011,mariadb114,mariadb118,mariadb123" \
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
    DOKA_RUNTIME_REQUIRE_CLEAN_SOURCE=1 \
    DOKA_RUNTIME_MYSQL_PORT=0 \
        bash "${repo_root}/eng/testing/test-runtime-posture.sh" --up-test-down
}

copy_matrix_evidence_legs() {
    local source_root="$1"
    local destination_root="$2"
    local release_artifacts_root="${repo_root}/artifacts/release-candidate"
    shift 2

    case "${destination_root}" in
        "${release_candidate_dir}"/*)
            ;;
        *)
            echo "Matrix evidence destination escapes the release candidate: ${destination_root}" >&2
            exit 1
            ;;
    esac

    if [[ -L "${repo_root}/artifacts" \
        || -L "${release_artifacts_root}" \
        || -L "${release_candidate_dir}" \
        || -L "${destination_root}" ]]; then
        echo "Matrix evidence destination path must not contain a symlink: ${destination_root}" >&2
        exit 1
    fi

    if [[ ! -d "${source_root}" || -L "${source_root}" ]]; then
        echo "Matrix evidence source is missing or non-regular: ${source_root}" >&2
        exit 1
    fi

    rm -rf -- "${destination_root}"
    mkdir -p "${destination_root}"

    local leg
    for leg in "$@"; do
        if [[ ! -d "${source_root}/${leg}" || -L "${source_root}/${leg}" ]]; then
            echo "Matrix evidence leg is missing or non-regular: ${source_root}/${leg}" >&2
            exit 1
        fi
        cp -R "${source_root}/${leg}" "${destination_root}/${leg}"
    done
}

run_efcore_matrix_gate() {
    local leg qualified_pattern qualified_version scope
    qualified_version="$(jq -er '
        [
          .efCoreVersions[]?
          | select(test("^10[.]0[.][0-9]+$"))
          | {version: ., components: (split(".") | map(tonumber))}
        ]
        | max_by(.components)
        | .version
    ' "${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Contracts/SpecSuiteBaseline.json")"
    qualified_pattern="^${qualified_version//./[.]}$"

    for leg in "10.0.8:^10[.]0[.]8$:minimum-10-0-8:dependency-graph" \
               "${qualified_version}:${qualified_pattern}:latest-10-0:full"; do
        scope="${leg##*:}"
        leg="${leg%:*}"
        DokaEfCoreVersion="${leg%%:*}" \
        DOKA_EF_CORE_RESOLVED_PATTERN="$(printf '%s' "${leg}" | cut -d: -f2)" \
        DOKA_EF_CORE_ARTIFACT_SUFFIX="${leg##*:}" \
        DOKA_EF_CORE_VALIDATION_SCOPE="${scope}" \
            bash "${repo_root}/eng/testing/test-efcore-matrix.sh"
    done
    copy_matrix_evidence_legs \
        "${repo_root}/artifacts/efcore-patch-matrix" \
        "${efcore_matrix_dir}" \
        "minimum-10-0-8" \
        "latest-10-0"
}

run_mysqlconnector_matrix_gate() {
    local leg
    for leg in "2.6.1:^2[.]6[.]1$:minimum-2-6-1" \
               "2.*:^2[.][0-9]+[.][0-9]+$:latest-2-x"; do
        DOKA_MYSQLCONNECTOR_VERSION="${leg%%:*}" \
        DOKA_MYSQLCONNECTOR_RESOLVED_PATTERN="$(printf '%s' "${leg}" | cut -d: -f2)" \
        DOKA_MYSQLCONNECTOR_ARTIFACT_SUFFIX="${leg##*:}" \
            bash "${repo_root}/eng/testing/test-mysqlconnector-matrix.sh"
    done
    copy_matrix_evidence_legs \
        "${repo_root}/artifacts/mysqlconnector-patch-matrix" \
        "${mysqlconnector_matrix_dir}" \
        "minimum-2-6-1" \
        "latest-2-x"
}

cd "${repo_root}"
"${repo_root}/eng/common/verify-dotnet.sh"

case "${stage}" in
    integration) run_integration_configuration_and_failure_gate ;;
    migration-deployment) run_migration_deployment_gate ;;
    runtime) run_runtime_posture_gate ;;
    efcore-patch-matrix) run_efcore_matrix_gate ;;
    mysqlconnector-patch-matrix) run_mysqlconnector_matrix_gate ;;
esac
