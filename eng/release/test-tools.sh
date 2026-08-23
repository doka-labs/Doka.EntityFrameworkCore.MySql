#!/usr/bin/env bash

# Runs only tests for the retained Python release boundary. General provider,
# quality, and performance verification remains owned by .NET.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${repo_root}"

PYTHONDONTWRITEBYTECODE=1 python3 -m unittest \
    eng.tests.test_github_release \
    eng.tests.test_local_package_consumer \
    eng.tests.test_materialize_sbom_assets \
    eng.tests.test_migration_bundle_isolation \
    eng.tests.test_nuget_publication \
    eng.tests.test_publication_readiness \
    eng.tests.test_reconciliation_contract \
    eng.tests.test_release_artifact_resolver \
    eng.tests.test_release_evidence \
    eng.tests.test_release_finalization_chain \
    eng.tests.test_release_package_lock \
    eng.tests.test_release_package_resolution \
    eng.tests.test_release_provenance \
    eng.tests.test_release_qualification \
    eng.tests.test_release_qualification_contract \
    eng.tests.test_release_stage_checkpoint \
    eng.tests.test_release_trust \
    eng.tests.test_run_with_deadline \
    eng.tests.test_runtime_posture_evidence_chain
