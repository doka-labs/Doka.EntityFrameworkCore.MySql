#!/usr/bin/env bash

# Executes one complete branch-owned specification partition and reconciles its TRX.
# The shared workflow and local matrix use this same command, including coverage.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
if [[ "$#" -ne 2 ]]; then
    echo "Usage: $0 <supported-target> <results-directory>" >&2
    exit 2
fi

target="$1"
results_directory="$2"
case "${target}" in
    mysql84 | mysql97 | mariadb1011 | mariadb114 | mariadb118 | mariadb123) ;;
    *) echo "Unsupported specification target '${target}'." >&2; exit 2 ;;
esac

runner="$(jq -er '.test.runner // "VSTest"' "${repo_root}/global.json")"
case "${runner}" in
    VSTest | Microsoft.Testing.Platform) ;;
    *) echo "Unsupported branch test runner '${runner}'." >&2; exit 2 ;;
esac

# WHY: The SDK environment override must agree with the branch contract selected below.
export DOTNET_TEST_RUNNER="${runner}"

if [[ "${results_directory}" != /* ]]; then
    results_directory="${repo_root}/${results_directory}"
fi

"${repo_root}/eng/common/verify-dotnet.sh"
mkdir -p "${results_directory}"
export DOKA_SPEC_TEST_TARGET="${target}"
export DOKA_TEST_DATABASE_EVIDENCE_FILE="${results_directory}/test-database-evidence.json"
project="${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"

if [[ "${target}" == "mysql84" ]]; then
    bash "${repo_root}/eng/testing/check-spec-contract.sh"
    bash "${repo_root}/eng/testing/check-spec-discovery.sh"

    case "${runner}" in
        VSTest)
            dotnet test "${project}" --configuration Release --no-build --no-restore --tl:off \
                --filter "FullyQualifiedName~SpecDispositionContractTests"
            ;;
        Microsoft.Testing.Platform)
            dotnet test --project "${project}" --configuration Release --no-build --no-restore --tl:off \
                --filter "FullyQualifiedName~SpecDispositionContractTests"
            ;;
    esac
fi

# WHY: The existing 10.x and 11.x lines use different native test protocols.
# Select the checkout's declared runner rather than inferring it from a major.
case "${runner}" in
    VSTest)
        dotnet test "${project}" --configuration Release --no-build --no-restore --tl:off \
            --filter "Category=Spec|Category=Live" \
            --collect:"XPlat Code Coverage" \
            --logger "trx;LogFileName=spec-tests.trx" \
            --results-directory "${results_directory}"
        ;;
    Microsoft.Testing.Platform)
        dotnet test --project "${project}" --configuration Release --no-build --no-restore --tl:off \
            --filter "FullyQualifiedName~Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.|Category=Spec|Category=Live" \
            --coverlet --coverlet-output-format cobertura --coverlet-file-prefix "spec-${target}" \
            --results-directory "${results_directory}" \
            --report-xunit-trx --report-xunit-trx-filename spec-tests.trx
        ;;
esac

bash "${repo_root}/eng/testing/check-spec-results.sh" "${target}" "${results_directory}"
