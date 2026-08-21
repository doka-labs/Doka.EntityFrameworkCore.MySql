# Performance Baseline Operations

This runbook owns engine-image acceptance, historical baseline seeding,
comparison, and the hosted proposal controller. It does not qualify or block a
release. Use [Performance Evidence](performance-evidence.md) for local execution
and triage, and [Performance Evidence Reference](performance-evidence-reference.md)
for schemas and measurement states.

## Accept an Engine Image Update

Dependabot proposes engine images against the Compose stack, which is the one
place it edits. The same pin also lives in the performance contract, the C#
test image catalog, and applicable workflow inputs, so its pull request is
incomplete by construction and the pin gate rejects it until every copy
follows:

```bash
gh pr checkout <number>
python3 eng/quality/check-image-pins.py --fix
```

A new image is a new measurement environment, so the contract needs a new
`contractVersion` in the same change. The accepted baseline was taken against
the previous image and stays as it is; the next hosted run reseeds and opens
its own review.

Proposals arrive monthly rather than weekly, because these images are rebuilt
whenever their base picks up patches, without any change to the engine
version. Each accepted rebuild costs a benchmark run, so they are batched. A
published vulnerability is handled when it is published and does not wait for
that cadence.

An update that leaves one of the supported MySQL 8.4 / 9.7 or MariaDB 10.11 /
11.4 / 11.8 / 12.3 release lines is not an image update but a support decision,
with its own specification matrix and baseline work. The pin gate rejects it
even when every copy agrees.

## Seed an Accepted Baseline

Seeding is a review action, not a regression-recovery shortcut.

Run every contract target in `seed` mode with the same profile and runner
class:

```bash
while read -r target; do
  DOKA_BENCHMARK_TARGET="${target}" \
  DOKA_BENCHMARK_PROFILE=scorecard \
  DOKA_BENCHMARK_BASELINE_MODE=seed \
  DOKA_BENCHMARK_RUNNER_CLASS=local-darwin-arm64 \
  DOKA_BENCHMARK_RUN_ID="local-seed-${target}" \
  ./eng/benchmark.sh --up-run-down
done < <(jq -r '.requiredTargets | keys[]' benchmarks/performance-contract.json)
```

Create the candidate only after reviewing every evaluation:

```bash
evidence=()
while read -r target; do
  evidence+=(
    --evidence
    "artifacts/benchmarks/${target}/reports/local-seed-${target}/evidence/performance-evaluation.json"
  )
done < <(jq -r '.requiredTargets | keys[]' benchmarks/performance-contract.json)

python3 -m eng.performance.cli seed \
  --contract benchmarks/performance-contract.json \
  --baseline benchmarks/baselines/doka-benchmark-baseline.json \
  --version <reviewed-baseline-version> \
  "${evidence[@]}"
```

Every evaluation handed to `seed` or `compare` must agree on profile, runner
class, commit, and source hash: promotion accepts one measured state of one
piece of software, not a set assembled from several. The run identifier is
deliberately not part of that agreement. It names a single measurement job, so
the commands above give each target its own, and the hosted matrix does the
same with one job per target. The contract's `evidenceMaximumAgeHours` keeps
the evaluations close together in time.

When adding a new runner class, retain existing accepted groups:

```bash
# Reuse the complete evidence array constructed above.
python3 -m eng.performance.cli seed \
  --contract benchmarks/performance-contract.json \
  --baseline artifacts/doka-benchmark-baseline.candidate.json \
  --version <reviewed-baseline-version> \
  --merge-existing benchmarks/baselines/doka-benchmark-baseline.json \
  "${evidence[@]}"
```

Validate the result before review:

```bash
python3 -m eng.performance.cli validate-baseline \
  --contract benchmarks/performance-contract.json \
  --baseline benchmarks/baselines/doka-benchmark-baseline.json \
  --output artifacts/baseline-validation.json
```

The seed command rejects a missing target, duplicate target, incomplete
workload matrix, failed evaluation, wrong contract version, or malformed
existing baseline. It replaces only matching target/profile/runner tuples.

## Compare with the Accepted Baseline

Run every required target with `DOKA_BENCHMARK_BASELINE_MODE=compare`, then
enforce the cross-target boundary:

```bash
DOKA_BENCHMARK_PROFILE=scorecard \
DOKA_BENCHMARK_GATE_RUN_ID=<run-id> \
bash eng/performance/check-benchmark-ratios.sh artifacts/benchmarks
```

The gate exits:

- `0` when every required target passes;
- `1` when current evidence or a budget fails;
- `2` when a required target has no current-run evidence.

Missing evidence fails by default, so no caller can inherit a permissive gate
by omitting a variable. A local run that deliberately measures one engine sets
`DOKA_BENCHMARK_GATE_ALLOW_MISSING=1`; even then the gate refuses to report
success when it evaluated no target at all.

Historical evidence outside the selected run ID cannot satisfy the gate.

## Hosted Runner Baseline

Any edit to the contract needs a new `contractVersion`. The resolver compares
that version first: a different one means the accepted baseline belongs to an
earlier contract and is reseeded, an equal one means it belongs to this
contract and is validated against its bytes. Editing the contract without
advancing the version therefore fails before measurement, because stored
evidence no longer matches the contract it claims.

Versions are dated. A second revision on the same day appends a counter, as in
`2026-08-09.2`, rather than borrowing a date the revision does not belong to.
The accepted baseline keeps the version it was measured under until a hosted
run produces a reviewed replacement.

`.github/workflows/benchmark.yml` and
`eng/performance/workflow_state.py` resolve baseline mode and required work
before starting services or expensive matrix jobs:

- control-plane-only changes run the resolver without allocating measurement
  runners;
- provider source changes select the complete six-target smoke lane;
- benchmark-defining source, contracts, images, SDK/build inputs, dependencies,
  evaluator, harness, target workflow, and sensitivity assurance select the
  complete scorecard lane;
- `Directory.Packages.props` is compared structurally, so production and
  benchmark packages select scorecard while classified test, analyzer, and
  example-only groups do not;
- mixed changes select the strongest required tier and cannot downgrade
  scorecard to smoke;
- documentation, tests, parent control-plane code, and accepted baseline output
  remain on the inexpensive resolver path;
- an exact current-contract `github-ubuntu-latest-x64` matrix selects
  `compare`; a missing or incompatible matrix selects `seed`;
- manual runs default to `auto`;
- automatic `compare` uses `paired` same-run evidence; `seed` alone uses
  `historical` comparison to create baseline evidence;
- malformed or partial current-contract evidence fails before the matrix;
- monthly and manual runs request fresh scorecard evidence; and
- proposal health alone cannot make an unrelated push allocate scorecard
  runners.

An automation branch that changes any path other than the canonical baseline
fails before the scorecard matrix starts. Later `main` pushes queue behind a
running scorecard instead of cancelling evidence already being collected.

Each target emits a typed attempt receipt. Success selects immediately.
`measurement-inconclusive`, represented by exit code `75`, permits exactly one
retry in a fresh hosted job with
a fresh database service. Any functional, budget, contract, or infrastructure
failure is terminal and cannot be selected away. Selection revalidates the
identity and digests of every attempt before copying a stable target artifact.

The routing is the CPU-independence mechanism: every automatic comparison is
paired before a measurement job starts. Exit code `76` remains a defense for
an explicitly requested local historical comparison; automatic hosted
comparison does not rely on it.

Both engine jobs must succeed before a seed run can propose a baseline update.
A seed still enforces workload completeness, absolute budgets, statistical
integrity, allocation, GC, soak, environment, and host admission. It omits only
a historical comparison that cannot exist yet.

After validation, a canonical semantic projection compares the seed candidate
with the accepted baseline. Run identifiers, timestamps, source hashes,
artifact hashes, and transient host admission remain immutable evidence but do
not create proposal churn. Workloads, statistics, budgets, stable environment
descriptors, and enforcement controls remain part of the accepted contract.

The workflow opens or updates a semantic baseline proposal but never approves
it. A private repository-scoped GitHub App registers squash auto-merge without
bypassing protected-branch policy. The operator path is:

1. Review the baseline diff and linked benchmark run.
2. Confirm `quality-gates`, `repo-tests`, and `integration-smoke` for the
   current proposal head.
3. Approve the current pull-request revision.
4. Let GitHub complete the App-owned squash merge after all requirements pass.

The ruleset dismisses an approval when later automation changes the reviewed
bytes and requires approval of the latest reviewable push. The App-owned
auto-merge request remains active because GitHub keeps the existing auto-merge
request active for a write-authorized automation update. Review the new
canonical-baseline diff, wait for the checks on the most recent reviewable push,
and approve that revision again; no workflow rerun, artifact download, or
manual merge is required.

The proposal exposes source commit and source hash, exact server images,
runtime and processor identity, raw and normalized statistics, calibration,
allocation, collection counts, absolute and soak verdicts, and raw report
digests. Allocated bytes have a qualifying paired ratio. Sparse Gen2 collection
ratios are observational, while the candidate-side absolute Gen2 ceiling
remains a hard gate.

Because `GITHUB_TOKEN` updates do not recursively trigger workflows, proposal
maintenance dispatches the trusted `baseline-proposal` CI profile on the exact
automation head. That profile runs only `quality-gates`, `repo-tests`, and
`integration-smoke`. Repository administrators must enable **Allow GitHub
Actions to create and approve pull requests**; the workflow uses creation, not
review or ruleset bypass.

Branch maintenance and restricted CI dispatch use the ephemeral
`GITHUB_TOKEN`. Only auto-merge registration uses a short-lived GitHub App
installation token restricted to this repository and the `contents` and
`pull-requests` write permissions. The controller reads the resulting state
back from GitHub and accepts only an App-owned squash request or completed
App-owned merge.

GitHub exposes Actions as `app/github-actions`, not the commit author name
`github-actions[bot]`. The temporary legacy-Actor migration branch must be
removed after the first dedicated-App proposal merges successfully and no open
legacy-Actor proposal remains. Record the qualifying pull request and run in
D-019 before removal. The resolver also creates and immediately revokes an unused App token
before expensive measurement when proposal maintenance may be required, so a
misconfigured App fails before runner allocation.

## Primary Sources

Retrieved 2026-08-07 and revalidated for the App integration on 2026-08-14:

- [Automatic token authentication][github-token-authentication]
- [Triggering a workflow][github-workflow-events]
- [Troubleshooting required status checks][github-required-checks]
- [Skipping workflow runs][github-skipped-workflows]
- [Authenticating as a GitHub App installation][github-app-authentication]
- [`actions/create-github-app-token`][github-app-token-action]
- [Rules available for rulesets][github-ruleset-reviews]

[github-actions-policy]:
  https://docs.github.com/en/organizations/managing-organization-settings/disabling-or-limiting-github-actions-for-your-organization
[github-required-checks]:
  https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks
[github-token-authentication]:
  https://docs.github.com/en/actions/concepts/security/github_token
[github-workflow-events]:
  https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow
[github-skipped-workflows]:
  https://docs.github.com/en/actions/how-tos/manage-workflow-runs/skip-workflow-runs
[github-app-authentication]:
  https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation
[github-app-token-action]:
  https://github.com/actions/create-github-app-token
[github-ruleset-reviews]:
  https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets
