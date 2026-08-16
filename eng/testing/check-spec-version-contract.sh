#!/usr/bin/env bash

# Reject an EF Core patch whose generated specification contracts have not
# been reviewed yet. This preflight runs immediately after dependency
# resolution, before the expensive build and live-engine matrix.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
contracts_root="${DOKA_SPEC_CONTRACTS_ROOT:-${repo_root}/tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Contracts}"
ef_core_version="${1:?EF Core version is required}"
inventory="${contracts_root}/SpecSuiteInventory.${ef_core_version}.json"
discovery="${contracts_root}/SpecDiscovery.${ef_core_version}.json"
baseline="${contracts_root}/SpecSuiteBaseline.json"

report_remediation() {
    echo "Generate and review the exact-version contracts before retrying; see" >&2
    echo "tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Contracts/README.md." >&2
}

if [[ ! "${ef_core_version}" =~ ^[0-9]+[.][0-9]+[.][0-9]+$ ]]; then
    echo "EF Core version must be an exact stable version, found '${ef_core_version}'." >&2
    exit 2
fi

for contract in "${inventory}" "${discovery}" "${baseline}"; do
    if [[ ! -f "${contract}" ]]; then
        echo "No committed specification contract exists for EF Core ${ef_core_version}: ${contract}" >&2
        report_remediation
        exit 1
    fi
done

jq -e --arg version "${ef_core_version}" '
    select(
      .schemaVersion == 1
      and .efCoreVersion == $version
      and (.testMethods | type == "array" and length > 0)
      and (.baseClasses | type == "array" and length > 0)
      and (.testMethods | length) == (.testMethods | unique | length)
      and ([.baseClasses[].id] | length) == ([.baseClasses[].id] | unique | length)
    )
' "${inventory}" >/dev/null || {
    echo "The EF Core ${ef_core_version} inventory is empty or version-mismatched." >&2
    report_remediation
    exit 1
}

jq -e --arg version "${ef_core_version}" '
    .supportedTargets as $supportedTargets
    | select(
      .schemaVersion == 1
      and (.efCoreVersions | index($version)) != null
      and (.efCoreVersions | length) == (.efCoreVersions | unique | length)
      and (.supportedTargets | type == "array" and length > 0)
      and (.supportedTargets | length) == (.supportedTargets | unique | length)
      and (.entries | type == "array" and length > 0)
      and ([.entries[].upstreamBaseId] | length)
        == ([.entries[].upstreamBaseId] | unique | length)
      and all(
        .entries[];
        (.efCoreVersions | index($version)) != null
        and (.targets | sort) == ($supportedTargets | sort)
      )
    )
' "${baseline}" >/dev/null || {
    echo "The specification baseline does not completely declare EF Core ${ef_core_version}." >&2
    report_remediation
    exit 1
}

jq -se --arg version "${ef_core_version}" '
    .[0] as $inventory
    | .[1] as $baseline
    | ([
        $inventory.baseClasses[]
        | {key: .id, value: .suiteDomain}
      ] | from_entries) as $inventoryBases
    | ([
        $baseline.entries[]
        | select((.efCoreVersions | index($version)) != null)
        | {key: .upstreamBaseId, value: .suiteDomain}
      ] | from_entries) as $baselineBases
    | select($inventoryBases == $baselineBases)
' "${inventory}" "${baseline}" >/dev/null || {
    echo "The specification baseline does not exactly match the EF Core ${ef_core_version} inventory." >&2
    report_remediation
    exit 1
}

supported_targets="$(jq -cer '.supportedTargets | sort' "${baseline}")"
jq -e \
    --arg version "${ef_core_version}" \
    --argjson supportedTargets "${supported_targets}" '
    select(
      .schemaVersion == 1
      and .efCoreVersion == $version
      and .providerAssembly == "Doka.EntityFrameworkCore.MySql.FunctionalTests"
      and ([.targets[].target] | sort) == $supportedTargets
      and all(
        .targets[];
        (.minimumTestCount | type == "number")
        and .minimumTestCount > 0
        and (.testIds | type == "array")
        and (.testIds | length) == .minimumTestCount
        and (.testIds | unique | length) == .minimumTestCount
        and (.fixtureTypes | type == "array" and length > 0)
        and (.fixtureTypes | length) == (.fixtureTypes | unique | length)
      )
    )
' "${discovery}" >/dev/null || {
    echo "The EF Core ${ef_core_version} discovery contract is incomplete or version-mismatched." >&2
    report_remediation
    exit 1
}

echo "EF Core ${ef_core_version} specification contracts are ready."
