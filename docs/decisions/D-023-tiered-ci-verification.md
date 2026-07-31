---
id: D-023
status: implemented
date: 2026-07-31
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "GitHub Actions verification lanes, cadence, and runner-minute budget"
supersedes: []
superseded-by: []
amends: [D-009, D-018]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-023 -- Use tiered CI verification under a fixed runner budget

## Context and Problem Statement

The repository CI workflow grew to thirteen Linux jobs for every push and pull
request. The complete run repeated repository tests, specification discovery,
live integration, and engine setup across several independent matrices. A
single run consumed approximately 175 billable runner minutes according to the
account usage observed when this decision was made.

Dependabot pull requests triggered the same workflow. Nine independent NuGet
update pull requests could therefore consume more runner minutes than the
repository's monthly included allowance before normal development and
scheduled compatibility evidence were considered.

The verification surface is intentional: the provider must retain floor and
latest EF Core validation, every supported engine, merged coverage, migration
deployment, runtime posture, and benchmark evidence. The problem is the
frequency at which every surface runs, not the existence of those surfaces.

## Decision Drivers

- Every repository event needs fast, deterministic regression feedback.
- Exhaustive compatibility must remain automatic and manually dispatchable.
- SQL-mode, TLS, authentication, pool, reset, and failover contracts must be
  release-blocking without consuming runner minutes on every push.
- A newly published EF Core patch must be detected without repository activity.
- Superseded branch runs should not consume the remaining runner budget.
- Dependency updates should be grouped by risk and consumer surface.
- Release eligibility must continue to require the complete release-candidate
  path.

## Considered Options

- Fast, exhaustive, and release verification lanes
- Exhaustive verification on every repository event
- Fast verification with manual exhaustive runs only

## Decision Outcome

Chosen option: "Fast, exhaustive, and release verification lanes", because it
preserves every verification surface while aligning its cadence with the
failure mode it detects.

The `ci` workflow has two automatic lanes:

1. Every push to `main` and every pull request runs quality gates, repository
   tests, and the representative MySQL 8.4 plus MariaDB 11.8 integration path.
   That integration smoke excludes the dedicated configuration, security, and
   failure categories.
2. A weekly schedule runs the complete workflow. Manual dispatch also runs the
   complete workflow. The exhaustive lane adds migration deployment, the EF
   Core floor/latest matrix, all three specification engines, the merged
   coverage gate, runtime posture, and both benchmark-smoke targets.

The release-candidate workflow is manual. It remains the pre-tag gate and
retains packaging, vulnerability, SBOM, publication-readiness, and complete
performance evidence that does not belong in the development feedback loop.
Its integration path is unfiltered and must cover MySQL 8.4, MariaDB 11.4,
and MariaDB 11.8. The retained evidence records that the full matrix was
required; manifest generation rejects filtered, partial, or failed evidence.

The dedicated supported-engine container matrix and performance scorecard
remain weekly. Runs for the same workflow and Git ref use one concurrency
group per event type, and a newer run cancels an older in-progress run. A fast
`main` push therefore cannot cancel a scheduled exhaustive run.

Dependabot keeps its weekly review cadence but groups every non-excluded NuGet
version update into one ecosystem-wide train and every GitHub Actions update
into a second train. Each ecosystem permits one open routine version-update
pull request. EF Core version-update pull requests are suppressed across patch,
minor, and major updates because D-009 assigns patch discovery to the scheduled
floor/latest matrix and requires a deliberate decision for a new minor or
major. Security updates retain their independent Dependabot path and are not
constrained by the routine version-update pull-request limit.

Local enforcement reuses the hosted quality contract. The versioned
`pre-commit` hook runs its deterministic no-network subset, and `pre-push`
runs the complete quality gate. Git does not activate repository files as
hooks automatically, so an explicit installer sets only the working copy's
local `core.hooksPath` and refuses to replace an existing custom path.

### Consequences

- Good, because a normal `main` push or pull request starts three jobs instead
  of thirteen while still testing source, repository policy, and two live
  engine families.
- Good, because exhaustive evidence remains automatic every week and can be
  requested on any branch before merge.
- Good, because the scheduled EF Core latest-patch check detects upstream
  changes even when no repository event occurs.
- Good, because local and hosted quality checks cannot drift into independent
  command lists.
- Good, because release evidence distinguishes a representative smoke run from
  the full integration configuration and failure contract.
- Good, because a scheduled Dependabot run creates at most one routine NuGet
  pull request and one routine GitHub Actions pull request.
- Bad, because a regression visible only in the exhaustive lane can remain
  undetected until the next weekly run unless a contributor dispatches it
  manually.
- Bad, because branch protection must require fast-lane checks rather than a
  heavy job that is intentionally skipped on ordinary pull requests.
- Bad, because Git hooks require one explicit activation per clone and remain
  bypassable through Git's standard `--no-verify` option.
- Bad, because a failing NuGet train may require the operator to isolate one
  package before the update can be accepted.

### Confirmation

- Inspect `.github/workflows/ci.yml` and confirm that only `quality-gates`,
  `repo-tests`, and `integration-smoke` lack the exhaustive event condition.
- Confirm that `schedule` and `workflow_dispatch` execute every CI job.
- Confirm that `concurrency.cancel-in-progress` is enabled per workflow and
  event type plus Git ref.
- Confirm that the fast integration lane excludes `ConfigurationContract`,
  `SecurityConfigurationContract`, and `FailureConfigurationContract`.
- Confirm that `eng/release-candidate.sh` requires the unfiltered MySQL 8.4,
  MariaDB 11.4, and MariaDB 11.8 integration matrix.
- Run the release-evidence unit tests and confirm that filtered or unrequired
  integration evidence is rejected.
- Confirm that Dependabot admits one routine pull request for each of the
  NuGet and GitHub Actions ecosystems.
- Run `eng/quality-gates.sh --fast` and `eng/quality-gates.sh`.
- Run `eng/install-git-hooks.sh` in an isolated clone and confirm that only the
  local `core.hooksPath` becomes `.githooks`.
- Run `eng/validate-adrs.sh`.
- Run the workflow-contract unit tests.
- Dispatch `ci` manually and confirm that all thirteen jobs complete.

## Pros and Cons of the Options

### Fast, exhaustive, and release verification lanes

- Good, because feedback cadence follows risk while all evidence paths remain
  executable and automatic.
- Bad, because contributors must understand which lane proves which contract.

### Exhaustive verification on every repository event

- Good, because every commit receives the complete evidence corpus
  immediately.
- Bad, because repeated matrices can exhaust the monthly runner allowance and
  prevent later security or release verification from running.

### Fast verification with manual exhaustive runs only

- Good, because it minimizes automatic runner consumption.
- Bad, because an upstream patch or engine regression can remain invisible
  indefinitely when no operator dispatches the workflow.

## More Information

The weekly exhaustive lane is an upper bound, not a reason to wait. Changes to
EF Core coupling, query translation, migration generation, engine capability
handling, coverage policy, runtime posture, or benchmark contracts should use
manual dispatch on their branch before merge.

The fast lane intentionally retains representative live integration. Unit and
non-live tests alone cannot prove test-owned container lifecycle, database
protocol behavior, migration locking, or spatial round trips.

Skipped heavy jobs remain visible in ordinary workflow runs. This avoids path
filters that could leave a required workflow check pending and makes the lane
boundary explicit in the Actions UI.

Local hooks are an early feedback mechanism, not an authority boundary. Hosted
CI remains required because client-side hooks can be absent or deliberately
bypassed.

### Re-evaluation Triggers

- Monthly Actions usage exceeds 80 percent of the included allowance for two
  consecutive billing cycles.
- The weekly exhaustive lane exceeds six hours or fails to complete before the
  next scheduled run.
- A regression reaches `main` that the exhaustive lane would have caught on
  the originating pull request.
- The repository moves to public standard runners, a paid runner allowance, or
  a dedicated trusted self-hosted runner.
- GitHub changes billing, cancellation, required-check, or Dependabot trigger
  semantics.

### Decision History

- 2026-07-31: Decision recorded with status implemented.
- 2026-07-31: Consolidated routine dependency updates into one pull request
  per ecosystem after observing avoidable CI fan-out from narrower groups.
- 2026-07-31: Reserved the SQL-mode, TLS/authentication, pool/reset, and
  failover matrix for exhaustive and release lanes; made unfiltered
  three-engine evidence mandatory for release candidates.

### Implementation References

- `.github/workflows/ci.yml`
- `.github/workflows/container-matrix.yml`
- `.github/workflows/release-candidate.yml`
- `.github/dependabot.yml`
- `.githooks/pre-commit`
- `.githooks/pre-push`
- `docs/release-governance.md`
- `eng/install-git-hooks.sh`
- `eng/quality-gates.sh`
- `eng/test-integration.sh`
- `eng/release_evidence.py`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Contracts/AdrRepositoryValidatorTests.cs`

### Sources

- [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions) (primary source; retrieved 2026-07-31)
- [Control workflow concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency) (primary source; retrieved 2026-07-31)
- [Triggering a workflow](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow) (primary source; retrieved 2026-07-31)
- [Control jobs with conditions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-jobs-with-conditions) (primary source; retrieved 2026-07-31)
- [Dependabot on GitHub Actions](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-on-actions) (primary source; retrieved 2026-07-31)
- [Dependabot options reference](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference) (primary source; retrieved 2026-07-31)
- [Git hooks](https://git-scm.com/docs/githooks) (primary source; retrieved 2026-07-31)
