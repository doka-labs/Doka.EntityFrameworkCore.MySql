---
id: D-025
status: accepted
date: 2026-08-08
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "CI verification lanes, workflow security posture, and lint contract for a public repository"
supersedes: [D-023]
superseded-by: []
amends: []
amended-by: [D-026]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-025 -- Verify on every event and harden the workflow surface for a public repository

## 2026-08-23 Amendment: Qualify the PR tree once

Merge-authoritative product qualification now runs on pull requests, explicit
manual CI, and the scheduled drift lane. The identical build, unit,
specification, integration, and coverage work no longer runs automatically on
`push main`. `repository-qualification` remains the single fail-closed
aggregator for the five merge gates.

Benchmark and OpenSSF scorecard workflows run only on schedule or explicit
dispatch. No merge queue, `merge_group`, main-admission workflow, or new
qualification policy is introduced.

The release trust owner resolves the one merged PR associated with a main
squash, requires its successful `repository-qualification` check, and compares
the qualified PR-head Git tree with the main-squash Git tree. A bypass without
an identical qualified tree is not releaseable. The hosted ruleset remains an
external separately approved change after the new aggregator has reported
success.

Statements below that require the same product lanes on both PR and `push
main`, or describe the retired `baseline-proposal` profile, are historical.

## 2026-08-17 Amendment: Make runtime posture commit-exact

RID-specific restore and publish behavior is affected by repository changes and
runner operating systems, not only by upstream drift. A release-candidate run
proved that the scheduled runtime lane could report success while producing
source evidence that finalization rejected. Runtime posture therefore runs for
every pull request and `main` push, requires an unchanged clean source tree, and
is an explicit input to `repository-qualification`.

The shared script runs inside the existing `integration-smoke` job, reusing its
checkout, SDK, package cache, and runner. No workflow, job, runner, or manual
trigger is added. D-017 excludes NativeAOT from this gate, so no NativeAOT
toolchain installation is required.

## Context and Problem Statement

D-023 rationed verification against a fixed runner-minute allowance. Its
opening premise was that one complete run consumed approximately 175 billable
runner minutes and that nine Dependabot pull requests could exhaust the monthly
allowance before ordinary development.

The repository is now public. GitHub does not bill standard hosted runners for
public repositories, so the constraint that produced the weekly cadence no
longer exists. D-023 cannot be repaired by editing its numbers because its
decision drivers, its chosen option, and its consequences all reason from a
budget that is gone.

Becoming public also changed the threat model in the opposite direction. Pull
requests now arrive from forks the maintainers do not control, and every
workflow file is readable by anyone looking for a weakness. Two exposures
follow directly. First, the default checkout persists a credential into the
job's Git configuration where any later step can reach it. Second, workflow
expressions expanded into `run:` blocks are shell source text, so a value that
ever becomes attacker-influenced becomes code.

A third gap is independent of visibility but was masked by it: 2539 lines of
workflow YAML and 37 shell scripts had no linter. The repository compensated
with a hand-written duplicate-key parser inside a Python test, which covers one
defect class out of many.

## Decision Drivers

- Verification cadence should follow the failure mode a lane detects, not a
  billing constraint that no longer applies.
- A regression that a lane can detect should be detected on the change that
  introduced it, not up to seven days later.
- Lanes that detect upstream drift rather than repository change gain nothing
  from running per event.
- Untrusted fork contributions must not reach a persisted credential.
- No workflow expression may reach a shell as source text.
- The workflow and shell surface needs the same automated enforcement the C#
  surface already has.
- Local and hosted verification must not drift into independent command lists.
- Package resolution must stay uncached where resolution is the property under
  test.

## Considered Options

- Per-event verification for change-detecting lanes, scheduled for drift-detecting lanes
- Complete verification on every repository event
- Retain the D-023 cadence and update only its rationale

## Decision Outcome

Chosen option: "Per-event verification for change-detecting lanes, scheduled
for drift-detecting lanes", because it binds each lane to the failure mode it
actually detects while keeping pull-request feedback bounded.

Every push to `main` and every pull request runs quality gates, repository
tests, the representative dual-engine integration path, specification
conformance across every active LTS target, the merged coverage gate, and the
real Linux RID runtime-posture path. These lanes detect regressions caused by
the change under review.

The scheduled lane retains migration deployment, the EF Core floor and latest
matrix, the MySqlConnector matrix, and every benchmark smoke target in the
performance contract. These detect upstream drift or environment drift, which
repository events do not cause and cannot predict.

The `baseline-proposal` dispatch profile continues to skip both lanes beyond
the three required checks. That profile exists for an automated pull request
that changes one reviewed JSON file, and widening it would spend engine matrices
on a diff that cannot affect them.

Workflow hardening is uniform. Every checkout sets `persist-credentials: false`
except the two baseline-proposal jobs that must push. Those two retain the
credential, bounded by the trigger and the permission set: the workflow runs
only from the default branch, never on fork input, and each job requests the
narrowest write scope it needs. Their tree-diff guards constrain the committed
content on top of that boundary rather than forming it. Every workflow
expression consumed by a shell reaches it through `env:` rather than through
expansion into the script text. Every job declares `timeout-minutes`.

The lint contract lives in `eng/quality/lint-workflows.sh` and runs from the
shared quality gate, so the hook, the local command, and CI execute one
implementation. A runner (`CI=true`) accepts only pinned, digest-verified
builds and ignores whatever the image happens to ship; a workstation uses the
contributor's own installation and reports a version that differs from the pin.
`DOKA_LINT_AUTO_INSTALL` overrides that choice in either direction. Deriving
the default from the environment rather than from a variable each workflow must
set keeps a new caller from inheriting a gate that cannot run. The commit hook
runs the offline shell subset; push and CI run the complete contract.

Dependency review runs per pull request against the repository license and
vulnerability policy. GitHub Automatic Dependency Submission is the single
resolved-graph producer for NuGet. It runs independently for repository pushes,
including automation-created branches and default-branch commits, so the
review workflow must not submit a second snapshot with a competing detector or
correlator identity.

GitHub documents the dependency-comparison warning header as the readiness
signal when submission and review run independently. For trusted same-repository
pull requests, a repository-owned preflight first requires successful
`submit-nuget` check runs for both exact revisions. Each check must bind the
requested SHA to the `github-actions` App; a missing, active, or failed producer
is a distinct fail-closed state. Base and head share a five-minute producer
window because the base should already have been submitted on its `main` push,
while the head producer starts with the branch push. Separate per-side windows
would silently double the registered job budget.

Only after both producer receipts succeed does the preflight start a fresh
15-minute graph-propagation window. It queries the exact base/head comparison
and retries the warning header with exponential backoff until the header is
absent. Producer polling is capped at ten seconds because check completion can
change promptly; graph-propagation polling is capped at 30 seconds because that
phase operates on a minutes scale. The shared producer window therefore makes
at most 37 Check Runs requests, and the propagation window makes at most 35
comparison requests. The normal ready path still makes three requests. This
bounded 72-request ceiling preserves margin within GitHub's documented
1,000-request hourly repository limit for `GITHUB_TOKEN`. A persistent missing,
propagating, or count-mismatched snapshot fails closed before the official
action can apply its soft retry timeout. The readiness receipt records both
check IDs and the measured producer and propagation durations so later runs can
recalibrate the registered budgets from evidence instead of silently extending
them.

The two budgets are based on the 2026-08-14 failure and recovery: automatic
submission completed in 54 seconds, the comparison was still incomplete 2
minutes later, and it was complete within 13 minutes after producer success. A
30-minute job timeout contains the five-minute producer window, the 15-minute
propagation window, the pinned action's three-minute defensive retry, up to one
minute for in-flight API calls at both deadlines, and runner overhead. GitHub
publishes retry guidance but no propagation SLA; exhausting either registered
window therefore remains an explicit external-evidence failure rather than a
policy bypass.

Fork and Dependabot pull requests retain their read-only token boundary; the
workflow does not replace it with `pull_request_target`. They still run the
official dependency-review action with its documented snapshot-warning retry.
The repository does not claim fail-closed transitive coverage where it cannot
orchestrate the untrusted head's graph production. Per-dependency OpenSSF
lookup remains enabled for every review path.

Repository OpenSSF Scorecard runs on every pushed `main` revision and weekly,
using only the action's stable `push` and `schedule` triggers. A newer revision
cancels a superseded in-flight scan because only the latest default-branch
state is publishable evidence. CodeQL remains on GitHub's default setup for
`csharp`, `python`, and `actions`; a repository-local CodeQL workflow is
explicitly not adopted because advanced setup would displace the working
default configuration.

### Consequences

- Good, because a specification or coverage regression now fails on the pull
  request that introduces it instead of on the following scheduled run.
- Good, because a fork contribution can no longer reach a persisted credential
  through a later step in the same job.
- Good, because one official producer owns each revision's resolved NuGet
  graph; duplicate snapshots cannot turn detector count drift into a false
  package delta.
- Good, because GitHub's automatic producer also covers commits created by
  repository automation without relying on a recursively suppressed `push`
  workflow.
- Good, because trusted review cannot turn an expired snapshot-warning retry
  into a successful but incomplete policy result.
- Good, because producer execution cannot consume the graph-propagation budget,
  and every accepted comparison cites successful checks for both exact SHAs.
- Good, because the workflow surface is enforced by actionlint and zizmor
  rather than by one hand-written parser covering a single defect class.
- Good, because the NuGet cache removes a full package restore from most jobs
  while the resolution matrices stay deliberately uncached.
- Good, because an unbounded job can no longer occupy a runner for six hours.
- Bad, because pull-request feedback now takes longer than the three-job lane
  D-023 defined.
- Bad, because trusted dependency review depends on GitHub's automatic
  submission service and fails closed when that external graph is unavailable
  or remains incomplete after the bounded retry window.
- Bad, because an unavailable automatic producer or graph can occupy the
  dependency-review runner for up to the registered 30-minute job timeout.
- Bad, because GitHub does not publish the `submit-nuget` check identity as a
  stable API contract; a vendor rename fails closed until the new identity is
  verified on an exact revision and registered in code, tests, and operations
  guidance.
- Bad, because fork and Dependabot pull requests cannot receive the same
  fail-closed head-readiness guarantee under their read-only token boundary.
- Bad, because the required status checks in the branch ruleset must be updated
  by an administrator before the newly per-event lanes actually block a merge.
- Bad, because two additional third-party actions enter the supply chain and
  must be added to the repository action allowlist before they can run.

### Confirmation

- `./eng/quality/lint-workflows.sh` exits zero; actionlint reports no findings
  and zizmor reports no findings above informational for every workflow.
- `python3 -m unittest discover -s eng/tests -p 'test_*.py'` passes, including
  `test_engineering_structure.test_declared_consumers_cover_every_executing_caller`,
  which fails when a workflow runs a root command the manifest does not declare.
- Inspect `.github/workflows/ci.yml` and confirm that `migration-deployment`,
  `efcore-patch-matrix`, `mysqlconnector-patch-matrix`, and `benchmark-smoke`
  carry the scheduled event condition. Confirm that `integration-smoke` owns
  the runtime-posture command and feeds `repository-qualification`.
- Inspect `.github/workflows/` and confirm every `actions/checkout` step sets
  `persist-credentials: false` except the two baseline-proposal jobs that push,
  and that each of those two runs only from the default branch and requests no
  write scope beyond the one its push and pull-request creation need.
- `python3 -m unittest eng.tests.test_release_workflow_policy` passes, including
  the contract that binds internal dependency submission to the exact pull-
  request head and keeps its write permissions out of the review job.
- `grep -c "timeout-minutes" .github/workflows/*.yml` reports one entry per job
  in every workflow.
- `python3 -m unittest eng.tests.test_benchmark_ratio_gate` passes, including
  the case that runs the performance gate without the missing-evidence opt-out
  and requires a non-zero exit.

## Pros and Cons of the Options

### Per-event verification for change-detecting lanes, scheduled for drift-detecting lanes

- Good, because each lane runs at the frequency of the failure it detects.
- Good, because pull-request runtime stays bounded by excluding matrices that
  cannot be affected by the diff under review.
- Good, because the scheduled lane keeps detecting upstream changes that occur
  without repository activity.
- Bad, because the split has to be justified per lane and can drift as lanes
  are added.

### Complete verification on every repository event

- Good, because no regression class waits for a schedule.
- Good, because the cadence needs no per-lane justification.
- Bad, because pull-request feedback would include four engine matrices and two
  benchmark targets that a source diff cannot influence.
- Bad, because runner concurrency limits would queue ordinary pull requests
  behind matrices with no relevance to them.

### Retain the D-023 cadence and update only its rationale

- Good, because it requires no workflow change and no ruleset update.
- Good, because pull-request feedback stays at its current three jobs.
- Bad, because it keeps a rationing decision after the constraint that
  justified it was removed.
- Bad, because it leaves specification and coverage regressions undetected for
  up to seven days for no remaining reason.

## More Information

The performance gate default was inverted as part of this decision. Missing
current-run evidence is now a gate failure, with an explicit
`DOKA_BENCHMARK_GATE_ALLOW_MISSING=1` opt-out for a deliberately partial local
run, and the gate refuses to report success when it evaluated no target at all.
Previously the permissive path was the default and only the release script
opted into strictness, so any future caller would have inherited a gate that
passed on absent evidence.

The coverage gate now asserts that its producing jobs succeeded. It previously
combined `needs:` with `always()`, and because the coverage uploads also run
under `always()`, a failed test job still published partial coverage that the
gate would merge and evaluate.

Several repository settings carry part of this model and are not expressible in
version control. They are documented in
`docs/operations/repository-security-settings.md`.

### Re-evaluation Triggers

- GitHub begins billing standard hosted runners for public repositories, or the
  repository returns to private visibility.
- Pull-request wall-clock time exceeds roughly 30 minutes and blocks review.
- A regression class is found that the scheduled lane detected too late,
  indicating the lane assignment is wrong.
- A CodeQL configuration need arises that default setup cannot express, which
  would reopen the advanced-setup decision.
- A contributor outside the trusted maintainer group receives repository write
  access, reopening the same-repository pull-request token boundary.
- zizmor or actionlint reports a finding class that must be permanently
  suppressed, indicating the tool no longer matches the repository posture.

### Decision History

- 2026-08-08: Decision recorded with status accepted.
- 2026-08-08: Supersedes D-023 after the repository became public and its
  runner-minute premise no longer applied.
- 2026-08-11: Expanded per-event specification conformance from three
  representative targets to every active MySQL and MariaDB LTS line.
- 2026-08-13: Bound same-repository dependency review to complete exact
  base/head graph evidence while preserving the read-only fork boundary.
- 2026-08-13: Restricted Scorecard execution to its stable `push` and `schedule`
  triggers and made superseded default-branch scans latest-wins.
- 2026-08-14: Removed the duplicate repository-owned NuGet submission after an
  automation-created `main` commit proved that the extra producer could not be
  triggered symmetrically. Automatic Dependency Submission now owns both sides,
  while the repository preflight requires a warning-free exact comparison.
- 2026-08-14: Split automatic-submission completion from graph propagation after
  a successful head producer still needed more than the original shared
  180-second window to become visible to dependency review.
- 2026-08-17: Reclassified runtime posture as commit-exact after a hosted
  release run exposed RID-specific source mutation that scheduled execution had
  not prevented from reaching finalization.

### Implementation References

- `.github/workflows/ci.yml`
- `.github/workflows/dependency-review.yml`
- `.github/workflows/scorecard.yml`
- `eng/quality/lint-workflows.sh`
- `eng/quality/check-vulnerability-audit.sh`
- `eng/quality/dependency_snapshot_readiness.py`
- `eng/performance/check-benchmark-ratios.sh`
- `eng/testing/test-runtime-posture.sh`
- `eng/tests/test_dependency_snapshot_readiness.py`
- `eng/tests/test_engineering_structure.py`
- `eng/tests/test_runtime_posture_evidence_chain.py`
- `docs/operations/repository-security-settings.md`

### Sources

- [About billing for GitHub Actions](https://docs.github.com/en/billing/managing-billing-for-your-products/about-billing-for-github-actions) (primary source; retrieved 2026-08-08)
- [Keeping your GitHub Actions and workflows secure: Untrusted input](https://securitylab.github.com/resources/github-actions-untrusted-input/) (primary source; retrieved 2026-08-08)
- [zizmor documentation](https://docs.zizmor.sh/) (primary source; retrieved 2026-08-08)
- [actionlint](https://github.com/rhysd/actionlint) (primary source; retrieved 2026-08-08)
- [OpenSSF Scorecard action v2.4.4](https://github.com/ossf/scorecard-action/blob/v2.4.4/README.md)
  (primary source; retrieved 2026-08-13)
- [Dependency review](https://docs.github.com/en/code-security/concepts/supply-chain-security/dependency-review)
  (primary source; retrieved 2026-08-15)
- [Dependency Review Action v5.0.0](https://github.com/actions/dependency-review-action/tree/a1d282b36b6f3519aa1f3fc636f609c47dddb294)
  (primary source; retrieved 2026-08-15)
- [Pinned Dependency Review comparison implementation](https://github.com/actions/dependency-review-action/blob/a1d282b36b6f3519aa1f3fc636f609c47dddb294/src/dependency-graph.ts)
  (primary source; retrieved 2026-08-15)
- [Check Runs API](https://docs.github.com/en/rest/checks/runs)
  (primary source; retrieved 2026-08-15)
- [REST API rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api)
  (primary source; retrieved 2026-08-15)
- [Configuring automatic dependency submission](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/submit-dependencies-automatically)
  (primary source; retrieved 2026-08-14)
- [Automatic dependency submission](https://docs.github.com/en/code-security/reference/supply-chain-security/automatic-dependency-submission)
  (primary source; retrieved 2026-08-14)
- [`GITHUB_TOKEN` workflow-trigger behavior](https://docs.github.com/en/actions/concepts/security/github_token#when-github_token-triggers-workflow-runs)
  (primary source; retrieved 2026-08-14)
- [Dependabot on GitHub Actions](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-on-actions)
  (primary source; retrieved 2026-08-13)
