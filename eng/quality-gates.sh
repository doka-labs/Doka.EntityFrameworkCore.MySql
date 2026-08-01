#!/usr/bin/env bash

# Mirrors the CI quality contract locally. Fast mode is deliberately offline
# and requires existing restore assets; full mode adds audits, executable
# examples, README compilation, and migration-model verification.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
solution="${repo_root}/Doka.EntityFrameworkCore.MySql.slnx"
validator_assets="${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.AdrValidator/project.assets.json"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
spatial_project_root="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite"
spatial_project="${spatial_project_root}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
audit_dir="${repo_root}/artifacts/ci-audit"
mode="full"

print_usage() {
    cat <<'EOF'
Usage:
  ./eng/quality-gates.sh
  ./eng/quality-gates.sh --fast

Modes:
  (no args)  Run the complete CI quality gate, including restore, vulnerability
             audits, example builds, and migration-model verification.
  --fast     Run deterministic no-network commit checks. Existing restore
             assets are required.
EOF
}

if (( $# > 1 )); then
    print_usage >&2
    exit 2
fi

if (( $# == 1 )); then
    if [[ "$1" != "--fast" ]]; then
        echo "Unknown option '$1'." >&2
        print_usage >&2
        exit 2
    fi

    mode="fast"
fi

"${repo_root}/eng/verify-dotnet.sh"

if [[ "${mode}" == "full" ]]; then
    echo "Restoring the repository solution..."
    dotnet restore "${solution}" --tl:off
elif [[ ! -f "${validator_assets}" ]]; then
    # Commit hooks must never hydrate dependencies or contact package feeds.
    echo "Fast quality gates require existing restore assets." >&2
    echo "Run 'dotnet restore Doka.EntityFrameworkCore.MySql.slnx --tl:off' first." >&2
    exit 1
fi

echo "Validating architecture decisions..."
"${repo_root}/eng/validate-adrs.sh"

echo "Verifying repository formatting..."
dotnet format "${solution}" \
    --verify-no-changes \
    --no-restore

echo "Verifying unnecessary usings..."
dotnet format "${solution}" style \
    --diagnostics IDE0005 \
    --severity hidden \
    --verify-no-changes \
    --no-restore

echo "Building the complete repository solution..."
dotnet build "${solution}" \
    --configuration Release \
    --no-restore \
    --tl:off \
    -m:1

if [[ "${mode}" == "fast" ]]; then
    echo "Fast quality gates passed."
    exit 0
fi

mkdir -p "${audit_dir}"

audit_project() {
    local project_path="$1"
    local audit_name="$2"
    local audit_file="${audit_dir}/${audit_name}.json"

    echo "Auditing ${audit_name} dependencies..."
    dotnet package list \
        --project "${project_path}" \
        --vulnerable \
        --include-transitive \
        --format json > "${audit_file}"

    if ! bash "${repo_root}/eng/check-vulnerability-audit.sh" "${audit_file}"; then
        echo "Vulnerability audit failed for ${audit_name}." >&2
        exit 1
    fi
}

audit_project "${runtime_project}" "runtime"
audit_project "${spatial_project}" "spatial"

echo "Building standalone example projects..."
example_build_failed=0
for example_project in "${repo_root}"/examples/*/*.csproj; do
    if ! dotnet build "${example_project}" --tl:off -m:1 > /dev/null 2>&1; then
        echo "FAIL: ${example_project}" >&2
        example_build_failed=1
    fi
done

if [[ "${example_build_failed}" -ne 0 ]]; then
    echo "One or more example projects failed to build." >&2
    exit 1
fi

echo "All example projects build successfully."

# Compile owned README snippets against the current project so public API and
# documentation changes cannot drift independently.
bash "${repo_root}/eng/check-readme-snippets.sh"
"${repo_root}/eng/check-migration-model.sh"

echo "Full quality gates passed."
