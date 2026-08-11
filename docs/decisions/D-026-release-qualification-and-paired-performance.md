---
id: D-026
status: implemented
date: 2026-08-09
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Which evidence qualifies a release and how performance is compared"
supersedes: []
superseded-by: []
amends: [D-019, D-025]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-026 -- Qualify releases from bound evidence and paired performance runs

## Context and Problem Statement

The release-candidate workflow runs eleven stages against a tag. Seven repeat
work already performed for the same source on `main`. Four belong to the
publication boundary: packing, SBOM generation, evidence assembly, and
attestation. The workflow also measures performance again and compares that
measurement with an accepted historical baseline.

Seven release candidates were created without reaching publication. Every
failure occurred in release machinery rather than in the provider. Two
exhausted a workload deadline, two disagreed with the accepted baseline on its
contract version, one required a shared run identifier that a per-engine
matrix could not produce, one failed a repository test because hosted-only
environment state escaped into that test, and one exposed two independent
defects at once.

The last candidate established two structural problems.

### The coverage gate exists twice and the second copy is incomplete

On commit `9f7f097b1edc4719572411504f2b64095baa49c2`, the `ci` coverage
gate merged five inputs and measured 90.70 percent line and 75.99 percent
branch coverage against minimums of 84 and 70. The release path merged two
inputs and measured 74.59 percent and 57.95 percent over identical
denominators. Its specification stage did not set a coverage results
directory, and its coverage job restored only repository-test and integration
artifacts. The duplicate gate was structurally unsatisfiable.

### The historical comparison requires hardware the fleet does not promise

Accepted baselines select on target, profile, and runner class, then require
exact equality on processor model, processor count, operating system, runtime,
server image, and server version. The runner class is
`github-ubuntu-latest-x64`, so processor model acts as an undeclared second
key.

GitHub documents standard public Linux runners by processor count, memory, and
architecture. It does not promise a processor model. In release-candidate run
`31334669273`, `mariadb118` ran on an AMD EPYC 7763 and `mysql84` ran on an AMD
EPYC 9V74. The accepted baseline used the former, so the MySQL comparison
failed on environment drift.

MariaDB matched the historical processor and still failed. Workload
`json.compare.element.equal.bytes-1048576` produced these deciding values:

| Field | Value |
|---|---:|
| Accepted normalized median | 0.017095665304081735 |
| Confirmed candidate normalized median | 0.022488648161483674 |
| Contract maximum at 1.15x | 0.019660015099693992 |

The workload allocates zero bytes per operation. Across the other 54
workloads, the median absolute normalized-median change was approximately 1.41
percent. No production source changed between the accepted baseline commit
`86530cf8b80e13990fc7c10e21669b6be6888257` and the candidate commit.

Neither result proves a provider regression. The processor mismatch makes the
first comparison invalid. The isolated second outlier needs a same-environment
reference before it can be attributed to the provider. Neither received a
retry because only the measurement-quality exit code was classified as
inconclusive.

### Moving the same comparison to `main` does not repair it

Moving the historical comparison from a tag to `main` avoids consuming a
release identity, but it does not make the comparison valid. A
performance-relevant commit scheduled on a processor absent from the accepted
baseline still has no comparator.

BenchmarkDotNet warns that performance results established for one runtime,
architecture, JIT, and operating system apply to that environment and nothing
else. Calibration cannot prove that different microarchitectures, cache
hierarchies, and memory systems shift every provider workload proportionally.

### A second manual qualification workflow would create another failure mode

Moving all release stages into a manually dispatched pre-tag workflow would
avoid spending a version, but it would add an operator-selected commit, run,
version, and artifact handoff. That does not add integrity. It creates another
state that can drift or be selected incorrectly, repeats work that automation
can classify, and requires another full run after transient failures.

The release path therefore needs one automatic evidence model, one tag-bound
packaging path, and one protected publication approval. It must not require a
separate release-qualification dispatch, a run-ID handoff between qualification
and candidate assembly, or a mandatory hosted rehearsal.

## Decision Drivers

- A normal release must require no manually dispatched qualification workflow.
- Verification must not be reduced; every existing gate must retain release
  authority from one implementation.
- Scheduled verification must run often enough to detect external drift that
  arrives without a repository commit.
- Every gate a release depends on must produce evidence for the tagged commit.
- The tag must bind package version, package bytes, SBOM, manifest, and
  provenance to one immutable release identity.
- A transient hosted failure must be recoverable by rerunning the same tag.
- Performance comparisons must be valid by construction rather than by hoping
  that a hosted runner repeats a historical processor.
- Infrastructure conditions must not be reported as provider regressions.
- Evidence identity must survive reruns, skipped jobs, and working-artifact
  expiry.
- The design must remain viable on standard public GitHub-hosted runners.

## Considered Options

- Qualify from automatic bound evidence and compare performance in paired runs
- Add a manually dispatched pre-tag qualification workflow
- Repair duplicated release stages as defects surface
- Group accepted performance baselines by processor model
- Widen normalized latency budgets
- Move performance measurement onto dedicated capacity
- Remove release qualification

## Decision Outcome

Chosen option: "Qualify from automatic bound evidence and compare performance
in paired runs", because it removes duplicate verification, preserves every
gate, gives expensive work a policy-driven cadence, and makes the performance
comparison valid within one allocated runner.

### Operational topology

The release path has three boundaries.

**Automatic evidence production** runs from existing repository workflows and
adds no new manual or pre-tag trigger. Every pull request and `main` push
produces commit-exact repository evidence. Both patch matrices keep their
existing weekly schedule, which is what detects upstream patch releases
arriving without a repository commit. The benchmark workflow keeps its
existing shape as well: its classifier decides per `main` push whether a
measurement is warranted, and a monthly schedule detects hosted drift. That
measurement is early warning, not a release gate. A manual dispatch may remain
available for diagnosis, but it is never required by the normal release
procedure.

**Release-candidate assembly** starts from a signed `v*` tag. The tag push
must start the existing `release-candidate` workflow automatically. That
workflow validates the tag, resolves the required evidence without
operator-supplied run identifiers, packs the tagged source, generates the SBOM,
assembles a canonical manifest, and attests the package and manifest. It runs
no repository tests and no specification or integration suites.

It does run every gate a release depends on: migration deployment, runtime
posture, both patch matrices, and one paired performance comparison. All of
them are commit-exact by construction. The scorecard profile requires soak
evidence, so the paired comparison executes the candidate-side soak scenarios
as part of that measurement; what is removed is a separate soak job, not soak
coverage.

The paired comparison is safe at the tag precisely because it is paired: both
sides execute in one allocated job, so no processor mismatch and no historical
comparator can fail it for an infrastructure reason. Its remaining failure
modes are a transient measurement condition, recoverable by rerunning the same
tag, and a genuine regression, which requires a source change and therefore a
new tag in any design.

**Publication** remains a separate manual workflow protected by the `nuget`
environment. Its approval is the only manual hosted release gate because it is
the operation that changes external package state.

The ordinary operator path is therefore:

1. Merge a reviewed change and wait for automatic evidence.
2. Create and push one signed tag on the intended green `main` commit.
3. Review the successful release-candidate artifact.
4. Approve the protected NuGet publication.

Steps 1 to 3 copy nothing between them: the version comes from the tag and the
evidence is resolved by commit. Step 4 is deliberately different. The candidate
run identifier and the exact tag select which candidate is published; they do
not authorize anything. Authorization comes from the typed confirmation and the
protected `nuget` environment approval. Selecting and authorizing are kept
separate on purpose for the one irreversible action.

### Repository qualification

Quality gates, repository tests, the specification matrix, integration smoke,
coverage, and required code-scanning checks belong to the exact release
commit. A stably named `repository-qualification` aggregator is the required
repository check. It runs with `always()` and validates the concrete result of
every dependency because GitHub permits skipped required checks and skips jobs
whose dependencies fail unless their condition explicitly overrides that
behavior.

The tag preflight queries and records the protected check set for the release
commit. It accepts only checks from the expected application or workflow and
requires the versioned minimum set. A larger protected set remains compatible;
a set reduced below the minimum is rejected.

### Expensive gates run at the tag

Every gate that a release depends on runs against the tagged commit. There is
no evidence-reuse model: no relevant-input classifier, no evidence-age limit,
and no ancestry or source-equivalence path for importing gate evidence from an
earlier commit. The independent ancestry checks that bind the tag to protected
`main` and the performance reference to the candidate remain mandatory.

That choice follows from what the gates actually consume. Migration
deployment, runtime posture, and both patch matrices compile and exercise the
provider source. A provider change therefore invalidates all four. Across the
84 `origin/main` commits in the thirty days ending 2026-08-10, `src/` changed
in 28 of them, and the wider input set these gates consume -- `src/`,
`Directory.Build.*`, `docker/`, and `eng/testing/` -- changed in 32. Any reuse
model would have failed closed in most weeks and made releases wait for the
next scheduled refresh. Running them at the tag removes that wait and makes
their evidence commit-exact by construction.

The measured cost supports the choice. Against a repository footprint of about
48 hours per month, the four gates add roughly 74 minutes per release: 83
seconds for migration deployment, 101 seconds for runtime posture, about 105
seconds per leg for the MySqlConnector matrix, and about 34 minutes per leg for
the EF Core matrix.

The weekly dual-engine benchmark smoke that D-025 introduced is retained
unchanged. It is a short, non-qualifying measurement that validates the
benchmark driver, selected workloads, and absolute smoke contracts. It costs nothing at
the tag and surfaces a broken benchmark path long before a release depends on
one. It produces no release evidence.

Schedules remain, and their purpose is now only early warning. Both patch
matrices resolve a floating dependency leg -- `10.0.*` for EF Core and `2.*`
for MySqlConnector, with central floating versions enabled -- so an upstream
patch release can break the provider while the repository stands still. Their
weekly run surfaces that near the day it happens. The benchmark workflow keeps
its classifier and monthly schedule for the same reason. Neither schedule
qualifies or blocks a release; the tag measures for itself.

For the dedicated benchmark workflow, resolved `compare` mode also means a
paired same-run comparison. Only resolved `seed` mode uses a historical
scorecard, because its purpose is to create reviewable replacement baseline
evidence rather than judge the candidate against another machine. The reusable
workflow accepts one comparison choice and derives the corresponding baseline
behavior. A caller therefore cannot express a contradictory `paired`/`seed` or
`historical`/`compare` combination, and no historical default is inherited.

This amends D-025: scheduled execution remains the detector for external drift,
but it is no longer a producer of release evidence.

### Evidence selection

All release evidence is commit-exact. Repository qualification is produced for
the release commit on `main`; the expensive gates and the paired performance
comparison are produced for it in the release-candidate workflow. Evidence is
selected for the exact tagged commit under the versioned identity policy,
rather than through source-equivalence or freshness rules.

The policy first filters eligible results. Every result must match the exact
commit, repository, gate, expected producer, workflow identity where
applicable, policy digest, and successful conclusion. Repository qualification
must additionally originate from a `push` on protected `main`. Tag-produced
gates must belong to the current release-candidate workflow run.

The selector then chooses exactly one result per gate. For repository
qualification it selects the greatest eligible numeric workflow run identifier
and then the greatest successful run attempt. For a tag-produced gate, the run
identifier is already fixed and it selects the greatest successful attempt not
newer than the assembling attempt. Zero eligible results, duplicate artifact
identities at the selected run-and-attempt key, an unknown producer, or an
unorderable identifier is `invalid-evidence`.

Selection occurs once when the canonical manifest is assembled. The manifest
pins every run identifier, attempt, producer, workflow digest, artifact
identifier, and file digest; later verification never reselects evidence. The
workflow never prompts for a run identifier, never treats a skipped job as
evidence, and never accepts gate evidence from an ancestor commit.

### Evidence manifests and integrity

Every gate executed inside the release-candidate workflow emits an immutable
gate manifest. Repository qualification and externally produced code-scanning
checks cannot emit a repository-owned manifest. For those gates, the tag
preflight converts the authenticated GitHub API response into an immutable
protected-check receipt before selection. A receipt records the API resource
identity and response digest in addition to the applicable fields below.

Gate manifests and protected-check receipts record:

- schema and evidence-policy versions;
- repository identifier and full name;
- gate and evidence kind;
- source commit, tree, and gate-specific source hash;
- workflow path, workflow content digest, run identifier, and run attempt;
- event, actor or expected application, conclusion, and completion time;
- evidence-policy digest;
- runtime, SDK, dependency, engine, image, and runner identities that apply;
- artifact identifier, artifact digest, and canonical SHA-256 for every file;
- retry and attempt selection receipts;
- generation and expiry times.

The tag workflow creates one canonical
`release-qualification-manifest.json`. It records the deterministic selection
of every required gate, the protected-check snapshot, package identities, SBOM
identity, and the complete file inventory.

GitHub's artifact download digest check is not the release gate because a
digest mismatch is surfaced as a warning. The repository verifier recomputes
every canonical file digest and fails on a missing, additional, differently
typed, or differing file. It also verifies artifact identity, producer
identity, workflow identity, run attempt, repository, the exact tagged commit,
and the artifact attestation. There is no source-equivalence path.

The canonical release-candidate artifact copies every selected raw report,
evaluation, and receipt needed for offline verification. It remains
self-contained after its working artifacts expire.

### Tag-bound packaging and recovery

Packing occurs exactly once in the release-candidate workflow from the tagged
source. Package version is derived from the tag. Package bytes, SBOM,
qualification manifest, source commit, workflow revision, and attestation are
bound together before publication authority is granted.

A transient restore, pack, SBOM, evidence-assembly, upload, or attestation
failure is recovered by rerunning failed jobs in the same workflow run. A full
rerun for the same immutable tag is also valid. Every attempt revalidates the
tag and evidence and must reproduce the same package identities.

A new tag is required only when source, dependencies, package content,
workflow logic, evidence policy, or performance contract changes. A failed
hosted run by itself never consumes a new version.

### Performance comparison

The comparison runs in the release-candidate workflow as one logical
qualification per release against the tagged commit. The automatic benchmark
workflow uses the same paired architecture for performance-relevant `main`
changes and monthly early warning. Policy-bounded measurement attempts and
workflow reruns remain attempts of their respective logical comparison. It is
the one measurement family the tag performs, and it replaces the historical
cross-run comparison entirely rather than supplementing it.

The architecture is a **paired same-run comparison**. Reference and
candidate run in one allocated job on the same processor, runtime,
architecture, and digest-pinned database service. Build, restore, and database
preparation stay outside the measurement window. Processor identity remains
recorded evidence and must agree within the pair, but it no longer has to match
a historical processor.

The candidate benchmark driver and candidate performance contract are normative for
both sides:

- reference and candidate providers are built as separate immutable local
  package or assembly artifacts;
- the accepted dual-engine baseline identifies one common reference commit;
- that reference commit must be an ancestor of the candidate commit;
- the same candidate benchmark driver launches both provider artifacts in isolated
  child processes;
- benchmark driver hash, contract digest, runtime, engine image, database preparation,
  workload order, and sampling policy are identical for both sides;
- the historical benchmark project is never used to execute the reference;
- an unavailable reference commit or a reference incompatible with the
  candidate benchmark driver, EF Core, or runtime yields `recalibration-required`;
- accepting a new reference requires the existing reviewed baseline proposal
  path and records the new reference commit and artifact digests.

A benchmark driver or contract change cannot therefore be classified as a provider
regression against an incompatible executable.

If an explicitly invoked historical comparison encounters an exact processor
mismatch, it exits with code `76` and yields a typed
`environment-not-comparable` state when an attempt receipt is recorded. It is
never reported as a provider regression. No automatic hosted comparison uses
that path: hosted `compare` mode is paired, and hosted historical mode is
reserved for seeding rather than comparison.

### Statistical contract

D-019 keeps `benchmarks/performance-contract.json` as the authoritative
machine-readable policy. D-026 fixes the required paired-policy shape rather
than duplicating tunable values in prose. Paired mode cannot be enabled, and
this ADR cannot become `implemented`, until the same reviewed change adds all
of these explicit values to that contract:

- counterbalanced execution order, recorded as executed and validated against
  the registered patterns;
- primary workload and metric family;
- practical regression budget for every primary metric;
- interval sidedness, confidence level, and named nonparametric method;
- deterministic resampling seed and resample count where applicable;
- minimum and maximum complete block counts;
- per-block sample allocation and quality threshold;
- family-wise multiple-comparison procedure;
- the practical budget each metric family is decided against, which together
  with the interval and the family procedure is what fixes the qualification,
  regression, and inconclusive boundaries;
- retry eligibility, retry count, and combination rule;
- maximum paired-run and per-workload durations.

There are no implementation defaults. A missing field, unknown method, policy
digest mismatch, or deviation from the registered execution plan is
`invalid-evidence`. A policy change requires the reviewed baseline proposal
path before it can qualify a release.

Reference and candidate are measured in counterbalanced blocks. Both sides
start from the same population and are held to the same precision floor.
Extension is adaptive, so the two may finish with different counts; how far
apart they may finish is registered as a maximum ratio, because populations far
enough apart no longer measured the same stretch of time. Each block produces paired
candidate-to-reference ratios for its declared metrics. Statistics are formed
from paired ratios, never from a quotient of unrelated historical aggregates.
Practical and statistical significance remain separate: a detectable change
inside its practical budget is not a regression.

Absolute catastrophe ceilings, allocation budgets, garbage-collection
evidence, soak invariants, raw sample retention, adjacent calibration, and
source binding remain mandatory under D-019. No historical latency threshold
is widened or removed.

### Attempt and qualification states

Attempt and final qualification states are separate domains.

| Attempt state | Meaning |
|---|---|
| `passed` | A usable comparison remained within policy. |
| `regression` | A usable comparison exceeded policy. |
| `measurement-inconclusive` | Sampling did not reach required quality. |
| `environment-not-comparable` | Historical comparator environments differ. |
| `recalibration-required` | The accepted reference cannot run under the current contract. |
| `invalid-evidence` | Evidence is incomplete, inconsistent, or unbound. |

| Qualification state | Derivation |
|---|---|
| `qualified` | Every primary comparison satisfies the registered qualification boundary. |
| `regression` | At least one primary comparison satisfies the registered regression boundary. |
| `inconclusive` | Evidence crosses a boundary or bounded retries produce no comparable pair. |
| `recalibration-required` | Reviewed replacement reference evidence is required. |
| `invalid-evidence` | Evidence cannot be trusted. |

Two `environment-not-comparable` attempts resolve to `inconclusive` at the
qualification level. That blocks a release without asserting a regression.

### Retention

Working artifacts keep their workflow-specific retention; benchmark attempt
artifacts keep seven days.

The release-candidate workflow copies every raw report, evaluation, receipt,
and manifest it produced into one self-contained artifact retained for 90 days,
so the release remains independently verifiable after the working artifacts
expire. No evidence-age limit applies at tag time: repository qualification may
have completed before the tag was created, but every selected result is bound
to the exact tagged source commit rather than to an ancestor commit.

### Amendments to earlier decisions

**D-019 -- Exact historical processor match.** Replaced by paired same-run
comparison. Historical mismatch becomes a typed interim attempt.

**D-019 -- Historical latency comparison at the tag.** Replaced. The tag runs
one paired same-run comparison instead of comparing a fresh measurement against
a historical baseline recorded on another host.

**D-019 -- Attempt classification.** Replaced by the attempt and qualification
domains above.

**D-019 -- Absolute, allocation, GC, soak, host, and source gates.** Retained
unchanged.

**D-019 -- Baseline proposal and maintainer acceptance.** Retained for
reference recalibration and performance-policy acceptance.

**D-025 -- Scheduled drift lane.** Amended. Schedules still detect external
drift, but they no longer produce release evidence. Every gate a release
depends on is measured against the tagged commit.

**D-025 -- Per-event repository lane.** Retained and represented by the stable
qualification aggregator.

**Mandatory local or hosted rehearsal.** Removed as a release gate. Targeted
local diagnostics remain optional.

### Consequences

#### Positive

- Good, because the normal release has no additional pre-tag dispatch and no
  qualification-to-candidate run-ID handoff. The publication step keeps its
  candidate selector deliberately.
- Good, because each validation gate has one producing implementation.
- Good, because every gate a release depends on is proven against the tagged
  commit, so no release carries evidence from an ancestor.
- Good, because the performance comparison becomes valid within one runner.
- Good, because infrastructure conditions have typed outcomes and cannot be
  reported as provider regressions.
- Good, because package, version, SBOM, manifest, and provenance bind directly
  to the signed tag.
- Good, because artifact integrity is verified fail closed rather than through
  a warning-only digest check.

#### Negative

- Bad, because a paired run measures the reference and candidate provider
  revisions and roughly doubles the measurement window. That addition is an
  estimate, not a measurement: the paired comparison does not exist yet.
- Bad, because a regression that the early-warning benchmark does not catch
  first surfaces at the tag, where fixing it costs a version number.
- Bad, because an old reference may become incompatible with the candidate
  benchmark driver and require reviewed recalibration.
- Bad, because repository ruleset state remains external to version control and
  must be verified through the API.

#### Neutral

- Neutral, because manual dispatch remains useful for diagnosis but carries no
  special release authority.
- Neutral, because publication remains manual; this decision removes only
  avoidable qualification interaction.

### Confirmation

- No normal release step dispatches a `release-qualification` workflow.
- No new manual or pre-tag trigger is introduced. The candidate gains exactly
  one new automatic trigger: a `v*` tag push.
- An unsigned tag, a signature from a signer outside the allowed set, a tag
  whose commit is not reachable from protected `main`, or a repository
  qualification that exists only for a pull request and not for a `main` push
  each fail the tag preflight.
- Both patch matrices keep their weekly schedule as early warning only.
- Migration deployment, runtime posture, and both patch matrices run in the tag
  workflow and are commit-exact; no classifier, evidence-reuse ancestry proof,
  or maximum evidence age exists anywhere in the release path.
- Weekly and monthly early-warning runs surface external drift at their
  declared cadence without acquiring release authority.
- An unrelated `main` change reaches the benchmark classifier without
  allocating a measurement.
- The early-warning benchmark result never qualifies or blocks a release.
- Repository qualification is commit-exact and required by the `main` ruleset.
- The tag workflow resolves evidence without a supplied run identifier.
- Multiple eligible runs or attempts resolve through the versioned ordering
  rule, and the canonical manifest freezes the selected identities.
- The tag workflow runs no repository test and no specification or integration
  suite, and one logical paired performance qualification comprising one
  reference-candidate pair per engine.
- The paired comparison executes the candidate-side soak scenarios the
  scorecard profile requires; removing the separate soak job does not remove
  soak coverage.
- Package version, package hashes, SBOM, manifest, tag, and commit agree.
- Missing, additional, or digest-mismatched files fail the tag preflight.
- A different workflow run attempt cannot silently replace selected evidence.
- The canonical candidate remains independently verifiable after working
  artifacts expire.
- A transient hosted failure can rerun the same tag without a new version.
- Historical processor mismatch is not classified as a provider regression.
- Reference and candidate share benchmark driver, contract, runtime, engine image, and
  database preparation in a paired job.
- An incompatible reference yields `recalibration-required`.
- Paired mode remains disabled until every statistical policy field exists and
  deterministic synthetic-population tests cover every decision boundary.
- Only the protected NuGet publication changes external package state.

## Pros and Cons of the Options

### Qualify from automatic bound evidence and compare performance in paired runs

- Good, because it removes duplicated gates and operator-selected handoffs.
- Good, because it preserves both change detection and external-drift refresh.
- Good, because reference and candidate share one measurement environment.
- Bad, because evidence-policy correctness becomes release
  critical.

### Add a manually dispatched pre-tag qualification workflow

- Good, because every gate could run on an exact commit before a tag exists.
- Bad, because it adds a second manual workflow and repeats unaffected work.
- Bad, because commit, version, run, and artifact selection become operator
  inputs without adding integrity.

### Repair duplicated release stages as defects surface

- Good, because it requires no structural change.
- Bad, because seven candidates exposed seven different release-path defects.
- Bad, because the historical comparison remains invalid across environments.

### Group accepted performance baselines by processor model

- Good, because it preserves historical normalized comparison.
- Bad, because the first appearance of any new processor has no baseline and
  blocks until that processor happens to be seeded.

### Widen normalized latency budgets

- Good, because it changes no workflow topology.
- Bad, because it hides environment bias and provides no defensible new value.
- Bad, because D-019 forbids normalizing a regression threshold from the
  candidate being judged.

### Move performance measurement onto dedicated capacity

- Good, because it controls hardware at the source.
- Bad, because larger GitHub-hosted runners require a higher plan and owned
  self-hosted hardware adds hardening, monitoring, maintenance, and
  availability obligations.

### Remove release qualification

- Good, because it is simple.
- Bad, because it removes package, SBOM, provenance, compatibility, and
  performance release authority.

## More Information

### Stage placement

| Stage | PR and `main` | Automatic drift evidence | Tag candidate | Publication |
|---|---|---|---|---|
| Quality gates | run | -- | verify | verify |
| Repository tests | run | -- | verify | verify |
| Specification matrix | run | -- | verify | verify |
| Integration smoke | run | -- | verify | verify |
| Coverage gate | run | -- | verify | verify |
| Code scanning | run | scheduled by service | verify | verify |
| Migration deployment | -- | weekly early warning | run | verify |
| Runtime posture | -- | weekly early warning | run | verify |
| EF Core patch matrix | -- | weekly early warning | run | verify |
| MySqlConnector patch matrix | -- | weekly early warning | run | verify |
| Benchmark smoke, dual engine | -- | weekly early warning | -- | -- |
| Performance early warning | classified `main` push | monthly | -- | -- |
| Performance qualification | -- | -- | measure paired | verify |
| Package and version identity | -- | -- | produce | verify |
| SBOM | -- | -- | produce | verify |
| Canonical evidence manifest | -- | -- | produce | verify |
| Artifact attestation | -- | -- | produce and verify | verify |
| NuGet publication | -- | -- | -- | protected manual action |

### Measured workflow footprint

Standard GitHub-hosted runners are free for public repositories with no monthly
minute limit, so this is not a cost constraint. Keeping the footprint small
remains a preference: it bounds feedback latency and queueing.

Measured over the thirty days ending 2026-08-10, across 84 commits on
`origin/main`, summing every non-skipped job attempt of every run:

| Producer | Runs | Runner time |
|---|---:|---:|
| `ci.yml`, all events | 64 | 2578 min |
| `benchmark.yml` | 18 | 287 min |
| Total | 82 | 2865 min, about 48 hours |

The single weekly scheduled `ci` run accounts for 138 minutes of that total
across fifteen jobs. The four gates this decision moves to the tag are measured
at about 74 minutes together: roughly 34 minutes per leg across two legs for
the EF Core matrix, about 105 seconds per leg for the MySqlConnector matrix, 83
seconds for migration deployment, and 101 seconds for runtime posture.

The paired comparison has been measured once, locally and outside any release
context: one block per side took 151 seconds each and a complete one-block run
including the sustained-use measurement took 2,470 seconds projected across the
eight registered blocks. That figure came from an Apple M2 Max with twelve
cores, against a container already serving other work, with the provider and
driver builds warm from earlier runs. It establishes that the registered
7,200-second budget is reachable; it establishes nothing about a hosted runner,
which is slower, colder, and the only environment a release measures in. No
release property is derived from it.

What bounds the cost instead is the watchdog hierarchy. Each side run stops at
the smaller of its own hang deadline and what remains of the comparison's
budget; before each further block the runner forecasts from the blocks already
measured and stops early when the remaining budget cannot hold another one. A
run that stops that way reports `measurement-inconclusive`, which is retryable,
rather than a verdict about the provider. The registered budget is therefore an
operating limit that a real run can be measured against, and the first
qualified paired run is what will decide whether it needs to move.

The monthly footprint below therefore covers the branch and scheduled work
only; the paired comparison is added to it once a qualified run has produced a
duration to add.

The footprint receipt uses the half-open interval from
`2026-07-11T22:00:00Z` through `2026-08-10T22:00:00Z`, which is the thirty-day
period from 2026-07-12 through 2026-08-10 in `Europe/Berlin`. The workflow-runs
API is paginated to exhaustion for `.github/workflows/ci.yml` and
`.github/workflows/benchmark.yml`. For every returned run, the jobs API is
queried with `filter=all` and paginated to exhaustion so rerun attempts are not
lost. Skipped jobs are excluded; every other job must have valid start and
completion timestamps. Duration is summed in seconds as `completed_at -
started_at` without per-job rounding and rounded to whole minutes only for the
table. Commit counts use the same half-open interval against `origin/main`.

Three earlier drafts of this decision reported lower totals. The first was wrong
by roughly a factor of twelve because it counted only jobs whose names
contained `attempt-1`, only successful ones, truncated each run to whole
minutes, and omitted `ci.yml` entirely. The second still undercounted `ci.yml`
because the query capped results at 60 runs when 64 existed. The third used the
jobs endpoint's default `filter=latest`, which omitted 223 minutes from earlier
rerun attempts. The figures above use `filter=all` and are what the placement
decisions rest on.

### State of the art

GitHub documents a standard public `ubuntu-latest` runner by its resource
shape and architecture, not by processor model. Exact historical processor
equality is therefore not a stable contract for that runner class.

BenchmarkDotNet explicitly warns against extrapolating results between
environments. Its baseline documentation also treats a benchmark ratio as a
distribution rather than a quotient of two unrelated summary values. The
paired design follows both constraints by measuring reference and candidate
within one allocated environment and retaining the ratio distribution.

The Rust compiler performance infrastructure demonstrates the alternative:
own dedicated collectors and disable sources of machine variance. That model
is valid but carries an operational boundary this project has not accepted.

GitHub documents workflow artifacts as immutable in `upload-artifact` v4 and
exposes configurable retention, but its download digest mismatch is a warning.
The explicit canonical-file verification remains necessary for a fail-closed
release gate. GitHub artifact attestations bind provenance to the artifacts
built by the workflow; they complement rather than replace repository-owned
manifest verification.

### Implementation surface

- `.github/workflows/ci.yml`: add the stable repository aggregator running
  with `always()`, and keep the weekly schedule as an early-warning lane that
  produces no release evidence.
- `.github/workflows/benchmark.yml`: retain the existing classifier and
  monthly schedule as early warning, and host reference recalibration. Its
  result never qualifies a release.
- `.github/workflows/benchmark-scorecard.yml`: implement the paired reference
  and candidate comparison within one job per engine.
- `.github/workflows/release-candidate.yml`: trigger from `v*` tag pushes;
  require `verification.verified == true` and `verification.reason == valid`
  from the Git tag API, independently verify the local annotated tag against
  the versioned trusted-signers policy, and require the observed tagger identity
  and public-key fingerprint to match one policy entry; require the tagged
  commit to be reachable from protected `main`, and require repository
  qualification to originate from a `push` on `main` rather than from a pull
  request for the same commit; verify repository evidence for the tagged
  commit, run migration deployment, runtime posture, both patch matrices, and
  the paired performance comparison against that commit, then pack, generate
  the SBOM, assemble the candidate, and attest it. Missing, expired, revoked,
  mismatched, or otherwise unverifiable signer material fails before any
  expensive job starts.
- `.github/workflows/nuget-publish.yml`: keep the protected manual approval and
  its typed confirmation; align its candidate resolution with the canonical
  manifest.
- `benchmarks/performance-contract.json`: add the complete paired statistical
  policy without implementation defaults.
- `eng/release/evidence-policy.json`: define the consumed gate catalog and the
  identities each gate must bind; define `trustedTagSigners` entries that bind
  one exact tagger identity to one accepted public key and fingerprint; and
  version the run-and-attempt selection rule. A signer-policy change requires a
  reviewed `main` change and a new tag. No relevant-input classifier, schedule,
  or freshness limit is needed, because no evidence is reused.
- `eng/testing/`: retain the migration, runtime, and MySqlConnector commands as
  the sole gate implementations, and extract the current inline EF Core patch
  matrix command into one shared entry point. Scheduled CI and tag
  qualification call those same implementations and differ only in scheduling
  and evidence authority.
- `eng/performance/attempts.py`: implement the attempt-state domain and bounded
  historical comparator retry.
- `eng/performance/paired.py`: evaluate paired ratios under the exact
  registered interval and multiple-comparison policy.
- `eng/performance/environment.py`: enforce environment equality inside each
  pair and retain full environment evidence.
- `eng/release/gate_results.py`: derive one result per gate from the receipts,
  resolved artifacts, and API responses the run actually produced
- `eng/release/qualification.py`: select one result per gate under the versioned
  ordering rule, freeze the chosen identities, and verify a manifest without
  reselecting
- `eng/release/trust.py`: establish the tag trust root and resolve the protected
  check down to the workflow run behind it
- `eng/release/evidence.py`: inventory the retained artifacts, verify their
  hashes, and bind the inventory to the qualification manifest so the two
  documents cannot describe different releases.
- `eng/release/release-candidate.sh`: separate evidence verification from
  tag-bound packaging and remove repeated validation stages.
- `docs/operations/release-publication.md`: remove the mandatory rehearsal and
  pre-tag dispatch; document automatic evidence, signed tag, candidate review,
  and protected publication.
- Repository settings: require `repository-qualification` on `main` and retain
  protected `v*` tags and the reviewed `nuget` environment.

### Re-evaluation Triggers

- The tag-time gate set grows until a release takes materially longer than the
  work it publishes; the split between tag-time and early-warning execution is
  revisited without reintroducing evidence reuse.
- GitHub begins guaranteeing a stable processor model for standard runners;
  the need for paired hosted comparisons is reconsidered.
- A repository-approved hardware-independent instrument becomes available for
  the complete .NET and live-database workload.
- Dedicated capacity becomes acceptable in plan, budget, security, and
  operating cost.
- A published defect would have been caught by a missing gate; the evidence
  catalog and minimum protected-check set are amended.
- Reference incompatibility repeatedly forces recalibration; reference
  selection and compatibility policy are revisited.

### Decision History

- 2026-08-09: Decision recorded with status proposed.
- 2026-08-10: Removed the separate manual qualification workflow, assigned
  commit-exact evidence per gate, and bound packaging to the
  signed tag.
- 2026-08-10: Defined deterministic rerun selection, the signed-tag trust root,
  protected-check receipts, and the retained non-qualifying benchmark smoke.
- 2026-08-10: Status changed from proposed to accepted.
- 2026-08-10: Status changed from accepted to implemented.
- 2026-08-11: Required paired mode for automatic comparisons, retained
  historical mode only for reviewed baseline seeding, and removed the reusable
  workflow's implicit historical default.
- 2026-08-11: Collapsed the reusable workflow boundary to one comparison mode
  and derived baseline behavior so contradictory mode pairs cannot be passed.

### Implementation References

- `docs/decisions/D-019-performance-gate-architecture.md`
- `docs/decisions/D-025-public-repository-verification-model.md`
- `.github/workflows/ci.yml`
- `.github/workflows/benchmark.yml`
- `.github/workflows/benchmark-scorecard.yml`
- `.github/workflows/release-candidate.yml`
- `.github/workflows/nuget-publish.yml`
- `benchmarks/performance-contract.json`
- `benchmarks/baselines/doka-benchmark-baseline.json`
- `eng/performance/attempts.py`
- `eng/performance/paired.py`
- `eng/performance/environment.py`
- `eng/release/gate_results.py`
- `eng/release/qualification.py`
- `eng/release/trust.py`
- `eng/release/evidence.py`
- `eng/release/release-candidate.sh`
- `docs/operations/release-publication.md`

### Sources

- [Release-candidate run 31334669273][candidate-run]
  (primary source; retrieved 2026-08-10)
- [Main benchmark resolver run 31332817720][benchmark-run]
  (primary source; retrieved 2026-08-10)
- [GitHub-hosted runners reference][github-runners]
  (primary source; retrieved 2026-08-10)
- [GitHub workflow runs API][github-runs-api]
  (primary source; retrieved 2026-08-10)
- [GitHub Actions workflow syntax][github-workflow-syntax]
  (primary source; retrieved 2026-08-10)
- [GitHub Actions billing][github-actions-billing]
  (primary source; retrieved 2026-08-10)
- [GitHub Git tags API][github-tags-api]
  (primary source; retrieved 2026-08-10)
- [Git tag signature verification][git-verify-tag]
  (primary source; retrieved 2026-08-10)
- [GitHub artifact storage and digest validation][github-artifacts]
  (primary source; retrieved 2026-08-10)
- [GitHub artifact attestations][github-attestations]
  (primary source; retrieved 2026-08-10)
- [GitHub required status checks][github-required-checks]
  (primary source; retrieved 2026-08-10)
- [BenchmarkDotNet good practices][bdn-good-practices]
  (primary source; retrieved 2026-08-10)
- [BenchmarkDotNet baselines][bdn-baselines]
  (primary source; retrieved 2026-08-10)
- [rustc-perf deployment documentation][rustc-perf-deployment]
  (primary source; retrieved 2026-08-10)
- [rustc-perf collector documentation][rustc-perf-collector]
  (primary source; retrieved 2026-08-10)
- [Henning et al., cloud performance variability for application
  benchmarks][cloud-variability]
  (primary source; retrieved 2026-08-10)
- [Mytkowicz et al., measurement bias in systems experiments][measurement-bias]
  (primary source; retrieved 2026-08-10)

[candidate-run]:
  https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/runs/31334669273
[benchmark-run]:
  https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/runs/31332817720
[github-runners]:
  https://docs.github.com/en/actions/reference/runners/github-hosted-runners
[github-runs-api]: https://docs.github.com/en/rest/actions/workflow-runs
[github-workflow-syntax]:
  https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax
[github-actions-billing]:
  https://docs.github.com/en/billing/concepts/product-billing/github-actions
[github-tags-api]: https://docs.github.com/en/rest/git/tags
[git-verify-tag]: https://git-scm.com/docs/git-verify-tag
[github-artifacts]:
  https://docs.github.com/en/actions/tutorials/store-and-share-data
[github-attestations]:
  https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations
[github-required-checks]:
  https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks
[bdn-good-practices]:
  https://benchmarkdotnet.org/articles/guides/good-practices.html
[bdn-baselines]: https://benchmarkdotnet.org/articles/features/baselines.html
[rustc-perf-deployment]:
  https://github.com/rust-lang/rustc-perf/blob/main/docs/deployment.md
[rustc-perf-collector]:
  https://github.com/rust-lang/rustc-perf/blob/master/collector/README.md
[cloud-variability]: https://arxiv.org/abs/2504.11826
[measurement-bias]: https://dl.acm.org/doi/10.1145/1508244.1508275
