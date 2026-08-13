## Summary

- change summary:
- why now:
- risk and rollback:

## Governance Impact

Use `unchanged` or `changed` for every row. A changed contract requires a
concise impact description and links to the corresponding tests or documents.

| Contract | Disposition | Impact and evidence |
| --- | --- | --- |
| Diagnostics categories and `MySqlEventId` values |  |  |
| Engine-difference handling and supported-engine policy |  |  |
| Public API shape |  |  |

## Evidence Impact

Use `unchanged` or `changed` for every row. Describe how changed evidence paths
are validated in this PR.

| Evidence path | Disposition | Impact and evidence |
| --- | --- | --- |
| Benchmark evidence |  |  |
| Compatibility evidence |  |  |
| Release-candidate evidence |  |  |

## Validation

Use `passed`, `not applicable`, or `pending` for every row. A `passed` result
must identify where it ran. `Not applicable` requires a change-specific reason.
Resolve every `pending` row before marking the PR ready for review.

Applicability:

- `./eng/test.sh`: source, test, build, engineering-tool, or workflow changes.
- `./eng/test-integration.sh`: database-visible behavior, engine differences,
  containers, migrations, scaffolding, networking, or integration fixtures.
- `./eng/test-runtime-posture.sh --up-test-down`: runtime posture, trimming,
  deployment, packaging, or runtime dependency changes.
- `./eng/benchmark.sh --up-smoke-down`: performance-sensitive provider paths,
  benchmark infrastructure, or performance-evidence changes.
- `./eng/release-candidate.sh`: release qualification, packaging, SBOM,
  publication, or release-evidence orchestration changes.

| Validation | Status | Evidence or rationale |
| --- | --- | --- |
| `./eng/test.sh` |  |  |
| `./eng/test-integration.sh` |  |  |
| `./eng/test-runtime-posture.sh --up-test-down` |  |  |
| `./eng/benchmark.sh --up-smoke-down` |  |  |
| `./eng/release-candidate.sh` |  |  |
| Other targeted checks |  |  |
