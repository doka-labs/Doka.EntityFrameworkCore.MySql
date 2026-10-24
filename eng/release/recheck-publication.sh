#!/usr/bin/env bash
# WHY: Remote writes re-read protected authority without changing frozen intent.
set -euo pipefail

publication_evidence="artifacts/release-publication"
python3 -m eng.release.publication recheck \
    --repository "${GITHUB_REPOSITORY:?}" \
    --candidate-root "artifacts/release-candidate/${DOKA_RELEASE_CANDIDATE_RUN_ID:?}" \
    --candidate-receipt "${publication_evidence}/candidate-receipt.json" \
    --admission "${publication_evidence}/publication-admission.json" \
    --output "${publication_evidence}/current-authorization-observation.json"
