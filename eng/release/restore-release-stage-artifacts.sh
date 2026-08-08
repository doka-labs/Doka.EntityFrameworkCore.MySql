#!/usr/bin/env bash
set -euo pipefail

# Restore the newest immutable receipt for each requested stage. Successful
# jobs from an earlier GitHub rerun attempt remain valid, while a rerun of a
# failed job contributes a newer receipt. The Python boundary validates that
# every archive belongs to this workflow run before any content is extracted.

output_directory=""
selection_output=""
stages=()

while (($# > 0)); do
    case "$1" in
        --output)
            output_directory="${2:-}"
            shift 2
            ;;
        --stage)
            stages+=("${2:-}")
            shift 2
            ;;
        --selection-output)
            selection_output="${2:-}"
            shift 2
            ;;
        *)
            echo "Unknown argument: $1" >&2
            exit 2
            ;;
    esac
done

if [[ -z "${output_directory}" || "${#stages[@]}" -eq 0 ]]; then
    echo \
        "Usage: $0 --output <directory> [--selection-output <file>] --stage <stage> [--stage <stage> ...]" \
        >&2
    exit 2
fi

required_environment=(
    GITHUB_REPOSITORY
    GITHUB_RUN_ATTEMPT
    GITHUB_RUN_ID
    GH_TOKEN
)
for variable_name in "${required_environment[@]}"; do
    if [[ -z "${!variable_name:-}" ]]; then
        echo "Required environment variable '${variable_name}' is missing." >&2
        exit 2
    fi
done

temporary_directory="$(mktemp -d)"
trap 'rm -rf "${temporary_directory}"' EXIT
metadata_path="${temporary_directory}/artifacts.json"
selection_path="${temporary_directory}/selection.json"

# --slurp keeps all API pages in one JSON array without dropping artifacts
# when a release run eventually exceeds the default page size.
gh api --paginate --slurp \
    "/repos/${GITHUB_REPOSITORY}/actions/runs/${GITHUB_RUN_ID}/artifacts?per_page=100" \
    > "${metadata_path}"

selection_command=(
    python3 -m eng.release.artifacts select
    --metadata "${metadata_path}"
    --run-id "${GITHUB_RUN_ID}"
    --maximum-attempt "${GITHUB_RUN_ATTEMPT}"
    --output "${selection_path}"
)
for stage in "${stages[@]}"; do
    selection_command+=(--stage "${stage}")
done
"${selection_command[@]}"

mkdir -p "${output_directory}"
while IFS=$'\t' read -r artifact_id artifact_name artifact_digest; do
    archive_path="${temporary_directory}/${artifact_name}.zip"
    gh api \
        "/repos/${GITHUB_REPOSITORY}/actions/artifacts/${artifact_id}/zip" \
        > "${archive_path}"
    python3 -m eng.release.artifacts restore \
        --archive "${archive_path}" \
        --destination "${output_directory}" \
        --sha256 "${artifact_digest}"
done < <(
    jq -er \
        '.artifacts[] | [.id, .name, .sha256] | @tsv' \
        "${selection_path}"
)

cp "${selection_path}" "${output_directory}/release-stage-artifact-selection.json"

if [[ -n "${selection_output}" ]]; then
    mkdir -p "$(dirname "${selection_output}")"
    cp "${selection_path}" "${selection_output}"
fi
