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
conformance across every active LTS target, and the merged coverage gate. These
lanes detect regressions caused by the change under review.

The scheduled lane retains migration deployment, the EF Core floor and latest
matrix, the MySqlConnector matrix, runtime posture, and both benchmark smoke
targets. These detect upstream drift or environment drift, which repository
events do not cause and cannot predict.

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

Dependency review runs per pull request against the repository license policy.
A preceding job restores the solution with the pinned SDK, converts the
resulting `project.assets.json` files into a NuGet snapshot, and submits it for
every pushed `main` revision and every trusted pull-request head. Both event
paths use the same stable detector and correlator identity. Dependency Review
therefore compares two graphs produced by the same restore contract instead of
classifying packages that only one detector can see as newly introduced. The
converter is repository-owned and dependency-free; it does not execute a
moving detector binary after review. The review job fails closed when trusted
head submission fails and retries snapshot warnings briefly to cover graph API
propagation. Main-push submissions are never cancelled because every pushed SHA
can remain the base of an open pull request; superseded PR-head runs remain
latest-wins.

The producer is skipped for forks and Dependabot pull requests because GitHub
deliberately gives those `pull_request` runs a read-only token by default; the
workflow does not replace that boundary with `pull_request_target`. Dependency
review still runs, but the repository does not claim a submitted transitive
snapshot for code outside its trusted same-repository branch boundary.

OpenSSF Scorecard runs on every pushed `main` revision and weekly, using only
the action's stable `push` and `schedule` triggers. A newer revision cancels a
superseded in-flight scan because only the latest default-branch state is
publishable evidence. CodeQL remains on GitHub's default setup for `csharp`,
`python`, and `actions`; a repository-local CodeQL workflow is explicitly not
adopted because advanced setup would displace the working default configuration.

### Consequences

- Good, because a specification or coverage regression now fails on the pull
  request that introduces it instead of on the following scheduled run.
- Good, because a fork contribution can no longer reach a persisted credential
  through a later step in the same job.
- Good, because dependency review no longer reports an empty internal-PR diff
  merely because automatic dependency submission covers only the default
  branch.
- Good, because trusted pull-request heads and their future `main` baseline use
  one stable submission identity and cannot turn detector drift into package
  additions.
- Good, because a later `main` push cannot cancel the snapshot required by a
  pull request that retains the earlier commit as its base.
- Good, because the workflow surface is enforced by actionlint and zizmor
  rather than by one hand-written parser covering a single defect class.
- Good, because the NuGet cache removes a full package restore from most jobs
  while the resolution matrices stay deliberately uncached.
- Good, because an unbounded job can no longer occupy a runner for six hours.
- Bad, because pull-request feedback now takes longer than the three-job lane
  D-023 defined.
- Bad, because each `main` push performs the same restore-backed submission as
  a trusted pull-request head so future comparisons retain a symmetric graph.
- Bad, because rapid `main` pushes queue their snapshot submissions instead of
  discarding intermediate base revisions.
- Bad, because the bootstrap pull request can still compare against the earlier
  automatic baseline; graph symmetry begins after its first `main` submission.
- Bad, because fork and Dependabot pull requests have no exact head snapshot;
  dependency review therefore cannot guarantee policy enforcement for
  transitive dependencies that become visible only after restore.
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
  `efcore-patch-matrix`, `mysqlconnector-patch-matrix`, `runtime-posture`, and
  `benchmark-smoke` carry the scheduled event condition, and that no other job
  does.
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
- 2026-08-13: Bound same-repository dependency review to a preceding snapshot
  for the exact pull-request head while preserving the read-only fork boundary.
- 2026-08-13: Extended the canonical snapshot to pushed `main` revisions and
  replaced per-PR correlators with one stable submission identity so both sides
  of a dependency comparison share the same graph contract.
- 2026-08-13: Prevented later `main` pushes from cancelling snapshots for
  commits that can remain the base revision of an open pull request.
- 2026-08-13: Restricted Scorecard execution to its stable `push` and `schedule`
  triggers and made superseded default-branch scans latest-wins.

### Implementation References

- `.github/workflows/ci.yml`
- `.github/workflows/dependency-review.yml`
- `.github/workflows/scorecard.yml`
- `eng/quality/lint-workflows.sh`
- `eng/quality/check-vulnerability-audit.sh`
- `eng/quality/dependency_snapshot.py`
- `eng/quality/restore-dependency-snapshot.sh`
- `eng/performance/check-benchmark-ratios.sh`
- `eng/tests/test_dependency_snapshot.py`
- `eng/tests/test_engineering_structure.py`
- `docs/operations/repository-security-settings.md`

### Sources

- [About billing for GitHub Actions](https://docs.github.com/en/billing/managing-billing-for-your-products/about-billing-for-github-actions) (primary source; retrieved 2026-08-08)
- [Keeping your GitHub Actions and workflows secure: Untrusted input](https://securitylab.github.com/resources/github-actions-untrusted-input/) (primary source; retrieved 2026-08-08)
- [zizmor documentation](https://docs.zizmor.sh/) (primary source; retrieved 2026-08-08)
- [actionlint](https://github.com/rhysd/actionlint) (primary source; retrieved 2026-08-08)
- [OpenSSF Scorecard action v2.4.4](https://github.com/ossf/scorecard-action/blob/v2.4.4/README.md)
  (primary source; retrieved 2026-08-13)
- [Dependency review](https://docs.github.com/en/code-security/concepts/supply-chain-security/dependency-review)
  (primary source; retrieved 2026-08-13)
- [Using the dependency submission API](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/use-dependency-submission-api)
  (primary source; retrieved 2026-08-13)
- [Configuring automatic dependency submission](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/submit-dependencies-automatically)
  (primary source; retrieved 2026-08-13)
- [REST API endpoints for dependency submission](https://docs.github.com/en/rest/dependency-graph/dependency-submission)
  (primary source; retrieved 2026-08-13)
- [GitHub Dependency Submission Toolkit](https://github.com/github/dependency-submission-toolkit)
  (primary source; retrieved 2026-08-13)
- [Dependabot on GitHub Actions](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-on-actions)
  (primary source; retrieved 2026-08-13)
