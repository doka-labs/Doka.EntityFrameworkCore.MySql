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

## 2026-08-15 Amendment: Performance has no release authority

Performance evidence is removed from the release boundary. The standalone
benchmark workflows remain available for characterization, early warning, and
reviewed baseline maintenance, but no benchmark outcome or artifact qualifies
or blocks a release.

The six-target `main` run `31903353665` demonstrated that the performance
system was not a reliable release predicate even when its measurements showed
no broad provider regression. Four targets failed because a paired
schema-version result was handed to a historical-schema verifier. One target
rejected a noisy reference population as invalid evidence, and one rejected a
quantized GC-count ratio. The previously successful historical seed path had
not exercised this paired end-to-end chain. Review also found a latent release
import that still expected the historical result shape.

The correction is structural rather than a warning suppression:

- `.github/workflows/release-candidate.yml` contains no benchmark job or
  reusable benchmark invocation;
- `eng/release/release-candidate.sh` contains no benchmark stage, mode, bypass,
  baseline preflight, or performance-artifact import;
- `eng/release/evidence-policy.json` grants no performance gate release
  authority;
- stage receipts, gate derivation, reconciliation, and the immutable candidate
  manifest contain no performance result; and
- the candidate manifest advances to schema version 2 because the removed
  `performanceEvidence` member changes its public evidence shape.

Release qualification now consists of the imported commit-exact
`repository-qualification` result and four tag-produced gates: migration
deployment, runtime posture, the EF Core patch matrix, and the MySqlConnector
patch matrix. Package and SBOM integrity remain bound into the candidate, and
their separate payload stages bring the exact stage-receipt set to six.

This amendment does not convert benchmark failure into success and does not add
`continue-on-error`. The release workflow cannot observe a benchmark result at
all. Performance may regain release authority only through a new reviewed ADR
after multiple complete six-target runs prove the production handoff,
selection, endpoint roles, and failure classification end to end. Until then,
the remainder of this ADR's paired-release design is retained as historical
rationale and is superseded wherever it conflicts with this amendment.

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

- Qualify from bound evidence without performance release authority
- Qualify from automatic bound evidence and compare performance in paired runs
- Add a manually dispatched pre-tag qualification workflow
- Repair duplicated release stages as defects surface
- Group accepted performance baselines by processor model
- Widen normalized latency budgets
- Move performance measurement onto dedicated capacity
- Remove release qualification

## Decision Outcome

Chosen option: "Qualify from bound evidence without performance release
authority", because release qualification must remain deterministic while
benchmarks have repeatedly produced infrastructure and orchestration failures
unrelated to a broad provider regression. Releases use automatic bound
repository evidence and tag-produced package, SBOM, migration, runtime, and
dependency-patch evidence. Performance remains independent engineering
evidence. The original paired-release outcome below is historical context for
the design that this ADR's 2026-08-15 amendment retired.

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

It runs every tag-produced gate a release depends on: migration deployment,
runtime posture, both patch matrices, package integrity, and SBOM integrity.
All are commit-exact by construction. Benchmark and soak evidence remain in the
standalone performance workflow and cannot affect candidate assembly.

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

The weekly benchmark smoke that D-025 introduced remains a short,
non-qualifying measurement. Its matrix is now derived from
`performance-contract.json.requiredTargets`, so it validates the benchmark
driver, selected workloads, and absolute smoke contracts against every active
LTS image without duplicating the support inventory. It costs nothing at the
tag and surfaces a broken target-specific benchmark path long before a release
depends on one. It produces no release evidence.

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
the release commit on `main`; migration deployment, runtime posture, both patch
matrices, package integrity, and SBOM integrity are produced for it in the
release-candidate workflow. Evidence is selected for the exact tagged commit
under the versioned identity policy, rather than through source-equivalence or
freshness rules.

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
- the accepted six-target baseline matrix identifies one common reference
  commit;
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
- one pre-registered required endpoint and explicitly observational secondary
  endpoints;
- one required or observational role for every supported target;
- practical regression budgets for the required and observational metrics;
- interval sidedness, confidence level, and named nonparametric method;
- deterministic resampling seed and resample count where applicable;
- one exact complete block count, fixed before measurement;
- per-block sample allocation and quality threshold;
- one run-wide family-wise multiple-comparison procedure over every required
  target;
- a reproducible power model, minimum detectable effect, maximum log-ratio
  dispersion, minimum power, simulation confidence, trial count, and seed;
- the practical budget each metric family is decided against, which together
  with the interval and the family procedure is what fixes the qualification,
  regression, and observational boundaries;
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
enough apart no longer measured the same stretch of time. Each target executes
exactly ten blocks, alternating which side starts. Each block produces paired
candidate-to-reference ratios for its declared metrics. Statistics are formed
from paired ratios, never from a quotient of unrelated historical aggregates.
Practical and statistical significance remain separate: a detectable change
inside its practical budget is not a regression. An interval that overlaps the
budget remains visible as an observational result; it does not add blocks or
authorize another attempt after the result is known.

The endpoint roles are fixed before measurement. Each target has one required
latency endpoint: within each block, take the geometric mean across the
complete workload matrix's normalized-median candidate-to-reference ratios.
Per-workload normalized median, p95, and p99 results remain observational.
They retain diagnostic value and their hard absolute ceilings, but they cannot
individually turn a release red or consume a path in the multiple-comparison
family. This is the same primary-versus-secondary separation used when several
endpoints are collected but only pre-specified primary endpoints support a
confirmatory conclusion.

The six required target endpoints form one run-wide family. A single Holm
step-down procedure controls the family-wise probability of any false
regression at `0.05`; no target or metric family receives a separate alpha.
Holm requires no independence assumption, which is necessary because targets
share provider code and workflow infrastructure. A target is a statistical
regression only when its Holm-adjusted p-value rejects and its interval is
above the practical budget. Resource regressions, absolute ceilings, and soak
failures remain independent hard gates.

The values Holm adjusts are exact one-sided randomization p-values, not
bootstrap proportions. For each target, the evaluator centers the ten paired
block log ratios on the practical budget and enumerates all 1,024 sign
assignments. Counterbalancing makes sign reversal the registered boundary-null
exchangeability model. The BCa bootstrap remains the estimator for the effect
interval; it does not manufacture the p-value used for family-wise control.

The dispersion bound is empirical and conservative, not a point estimate. The
planning-only file
`benchmarks/characterization/paired-dispersion-2026-08-13.json` binds four
hosted attempts from workflow run `31671904108` by artifact ID, evidence hash,
run ID, source commit, and contract digest. Those measurements cannot be reused
as release qualification. Their largest aggregate log-ratio standard deviation
is `0.025445185090747145`. The registered `0.06048100249438095` limit is its
one-sided 99 percent upper confidence bound, computed with the NIST chi-square
formula for a standard deviation and the registered lower-tail critical value
`1.239` at seven degrees of freedom. The characterization file itself is bound
to the contract by SHA-256, so changing either input invalidates the policy.

The exact ten-block population is justified by an executable sensitivity
assurance. It drives the production BCa estimator, exact sign-flip test, and
the first Holm threshold
for six required targets through 200 deterministic log-normal planning
experiments. At the conservative dispersion bound, a regression at `1.10`
times the practical budget is detected in 180 of 200 experiments. Its one-sided
95 percent Wilson lower bound is approximately `0.8596`, above the registered
80 percent minimum power. The detectable aggregate normalized-median ratio is
therefore `1.265` against the `1.15` practical budget.

Each paired attempt writes a digest-bound
`paired-dispersion-observation.json` with target, run, source commit, runner
class, source hash, contract digest, reference commit, realized dispersion,
registered bound, and `stable` or `drift`. Monthly automatic scorecards retain
these small observations for ninety days and report drift as a workflow
warning. They therefore form an auditable time series without retaining the
large raw samples or adding another manual workflow. A required target above
the registered bound is
`measurement-inconclusive`; it cannot claim power the measured population did
not possess. The second independent attempt remains the only automatic retry.

There is a governed exit from persistent fail-closed behavior. If the same
required target exceeds the dispersion bound in both attempts of two separate
complete scorecard workflow runs within thirty days, the next release requires
an amendment to this ADR. The amendment must choose and verify one of three
outcomes: remove a proven measurement defect and retain the contract; register
a new planning-only characterization and block plan with recomputed power; or
change that target to `observational` only after documenting why functional,
resource, absolute-ceiling, and soak evidence still support the advertised
target contract. No runtime result changes a role automatically, and a
characterization change requires the reviewed contract path before another
qualifying run.

Absolute catastrophe ceilings, allocation budgets, garbage-collection
evidence, soak invariants, raw sample retention, adjacent calibration, and
source binding remain mandatory under D-019. No historical latency threshold
is widened or removed.

### Attempt and qualification states

Attempt and final qualification states are separate domains.

| Attempt state | Meaning |
|---|---|
| `passed` | A usable target comparison is eligible for run-wide adjustment. |
| `regression` | A usable comparison exceeded policy. |
| `measurement-inconclusive` | Sampling did not reach required quality. |
| `environment-not-comparable` | Historical comparator environments differ. |
| `recalibration-required` | The accepted reference cannot run under the current contract. |
| `invalid-evidence` | Evidence is incomplete, inconsistent, or unbound. |

| Qualification state | Derivation |
|---|---|
| `qualified` | The run-wide Holm procedure rejects no required target and every hard gate passes. |
| `regression` | At least one required target rejects run-wide above its practical budget, or a hard gate fails. |
| `inconclusive` | Bounded retries produce no complete comparable measurement. |
| `recalibration-required` | Reviewed replacement reference evidence is required. |
| `invalid-evidence` | Evidence cannot be trusted. |

Two `environment-not-comparable` attempts resolve to `inconclusive` at the
qualification level. That blocks a release without asserting a regression.

### Retention

Raw benchmark attempt artifacts keep seven days. Their small dispersion
observations keep ninety days so the governed thirty-day drift trigger remains
auditable.

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
- The tag workflow runs no repository test, specification suite, integration
  suite, or benchmark.
- Candidate assembly reads no performance contract, baseline, scorecard,
  selection receipt, or benchmark artifact.
- Package version, package hashes, SBOM, manifest, tag, and commit agree.
- Missing, additional, or digest-mismatched files fail the tag preflight.
- A different workflow run attempt cannot silently replace selected evidence.
- The canonical candidate remains independently verifiable after working
  artifacts expire.
- A transient hosted failure can rerun the same tag without a new version.
- Historical processor mismatch cannot block a release because performance is
  outside the release boundary.
- Only the protected NuGet publication changes external package state.

## Pros and Cons of the Options

### Qualify from bound evidence without performance release authority

- Good, because release qualification remains bound to deterministic,
  commit-exact correctness, compatibility, package, SBOM, and provenance
  evidence.
- Good, because benchmark failures remain visible without spending release
  versions or blocking publication for infrastructure and measurement defects.
- Bad, because a performance regression can no longer reject a release until a
  future ADR proves a reliable release-authority design.

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
| Performance qualification | -- | independent engineering evidence | -- | -- |
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

The paired comparison was measured once, locally and outside any release
context under the previous eight-block contract: one block per side took 151
seconds each and the sustained-use measurement took 54 seconds, projecting to
2,470 seconds in total. That figure came from an Apple M2 Max with twelve
cores, against a container already serving other work, with the provider and
driver builds warm from earlier runs. It establishes that the registered
7,200-second budget is reachable; it establishes nothing about a hosted runner,
which is slower, colder, and the only environment a release measures in. No
hosted extrapolation is made from this measurement. The current contract fixes
ten complete blocks; no release property is derived from
the older projection.

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

BenchmarkDotNet's pilot stage estimates how many invocations an iteration needs
to approach its requested duration. Go's benchmark runner likewise predicts a
larger iteration count from observed time and keeps the iteration-count limit as
a separate bound. The paired workload runner follows that separation: after
warmup, one configured operation batch is timed, then the measured batch is
scaled so the workload's starting population plans 120 percent of the minimum
duration, with a maximum 1,024-fold multiplier. Sixteen starting samples target
150 milliseconds each; an explicit 8,192-sample tail population targets about
293 microseconds each. Both plan at least 2.4 seconds against the two-second
duration floor. Every workload uses the fastest of three pilot observations so
a scheduler stall cannot undersize it. Raw evidence schema 5 records the
configured batch, batching mode, every pilot duration, and the actual batch,
and the validator recomputes the choice from those fields.

This sizing is restricted to paired evidence. Historical scorecard and stress
profiles keep fixed batches so accepted baselines retain their original
measurement contract. The sample cap remains a count bound in every profile;
it is not used to manufacture the minimum duration.

Benchmark run 31568808053 exposed the former coupling on both performance
release targets: MySQL 8.4 and MariaDB 11.8 each exhausted the 1,024-sample cap
on two independent runners without reaching the duration floor. The selector correctly
withheld both attempts as `measurement-inconclusive`; changing retry or
selection semantics would only have hidden valid evidence. Pilot sizing removes
the contract defect that made a faster workload consume the count cap.

Go's `benchstat` guidance separately recommends choosing the number of runs in
advance, using at least ten and ideally twenty, interleaving old and new, and
then keeping that population. The paired policy therefore fixes ten
counterbalanced blocks per target before measurement. A confidence interval
that overlaps the practical budget is retained as `observed-overlap`;
the workflow does not add blocks or retry merely because the fixed population
did not prove a regression. This avoids result-driven optional stopping while
keeping confirmed regressions and hard safety ceilings fail closed.

The target population is derived from
`performance-contract.json.requiredTargets`. GitHub documents both matrix
expansion and a matrix calling a reusable workflow; one contract-derived matrix
therefore covers every active LTS line without six copied job definitions or a
new maintainer-facing workflow. Oracle allows features to be added or removed
at the first release of an LTS series, so a representative family pair is not
evidence for the other supported LTS lines.

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
- `.github/workflows/benchmark-scorecard.yml`: derive the active target matrix
  from the performance contract.
- `.github/workflows/benchmark-target.yml`: implement one target's paired
  reference and candidate comparison behind the internal reusable boundary.
- `.github/workflows/release-candidate.yml`: trigger from `v*` tag pushes;
  require `verification.verified == true` and `verification.reason == valid`
  from the Git tag API, independently verify the local annotated tag against
  the versioned trusted-signers policy, and require the observed tagger identity
  and public-key fingerprint to match one policy entry; require the tagged
  commit to be reachable from protected `main`, and require repository
  qualification to originate from a `push` on `main` rather than from a pull
  request for the same commit; verify repository evidence for the tagged
  commit, run migration deployment, runtime posture, and both patch matrices,
  then pack, generate the SBOM, assemble the candidate, and attest it. Missing,
  expired, revoked, mismatched, or otherwise unverifiable signer material fails
  before any
  expensive job starts.
- `.github/workflows/nuget-publish.yml`: keep the protected manual approval and
  its typed confirmation; align its candidate resolution with the canonical
  manifest.
- `benchmarks/performance-contract.json`: add the complete paired statistical
  policy without implementation defaults.
- `eng/release/evidence-policy.json`: define the consumed gate catalog and the
  identities each gate must bind; define `trustedTagSigners` entries that bind
  one exact tagger identity to one accepted public key and fingerprint; and
  version the run-and-attempt selection rule. Performance evidence is excluded
  from this catalog and has no candidate provenance entry. A signer-policy
  change requires a reviewed `main` change and a new tag. No relevant-input
  classifier, schedule, or freshness limit is needed for tag-produced evidence,
  because no such evidence is reused.
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
- 2026-08-12: Separated paired sample duration from sample count by adding
  bounded pilot-based operation batching and verifiable schema-5 provenance.
- 2026-08-13: Replaced result-dependent block ranges with ten pre-registered
  counterbalanced blocks backed by an executable sensitivity assurance, made
  statistical overlap visible without a statistical retry, and expanded
  performance qualification to every active MySQL and MariaDB LTS target from
  the canonical contract.
- 2026-08-14: Derived the scheduled smoke matrix from every required
  performance target and bound the direct release-candidate runner explicitly
  to paired comparison evidence.
- 2026-08-14: Bound scorecard reuse invalidation to the extracted target
  workflow and sensitivity assurance, and corrected release provenance to the
  workflow that uploads qualified performance artifacts.
- 2026-08-14: Bound scorecard reuse invalidation to the paired endpoint
  estimator and bounded attempt selector, with a structural contract that
  covers every module imported by the supported performance CLI.
- 2026-08-14: Replaced per-workload confirmatory latency tests with one
  required aggregate endpoint per target, controlled the six-target family by
  Holm, bound sensitivity to a 99 percent NIST dispersion upper bound over
  immutable hosted characterization, and added automatic drift observations
  plus an ADR-governed exit from persistent inconclusive evidence.
- 2026-08-15: Removed performance from release qualification after the complete
  six-target paired workflow demonstrated multiple orchestration and
  classification failures unrelated to a broad provider regression. Retained
  benchmark workflows as independent engineering evidence.

### Implementation References

- `docs/decisions/D-019-performance-gate-architecture.md`
- `docs/decisions/D-025-public-repository-verification-model.md`
- `.github/workflows/ci.yml`
- `.github/workflows/benchmark.yml`
- `.github/workflows/benchmark-smoke.yml`
- `.github/workflows/benchmark-scorecard.yml`
- `.github/workflows/benchmark-target.yml`
- `.github/workflows/release-candidate.yml`
- `.github/workflows/nuget-publish.yml`
- `benchmarks/performance-contract.json`
- `benchmarks/characterization/paired-dispersion-2026-08-13.json`
- `benchmarks/baselines/doka-benchmark-baseline.json`
- `eng/performance/attempts.py`
- `eng/performance/paired.py`
- `eng/performance/sensitivity.py`
- `eng/performance/environment.py`
- `eng/release/gate_results.py`
- `eng/release/qualification.py`
- `eng/release/trust.py`
- `eng/release/evidence.py`
- `eng/release/release-candidate.sh`
- `docs/operations/release-publication.md`

### Sources

- [Six-target paired benchmark run 31903353665][failed-six-target-run]
  (primary source; retrieved 2026-08-15)
- [Release-candidate run 31334669273][candidate-run]
  (primary source; retrieved 2026-08-10)
- [Main benchmark resolver run 31332817720][benchmark-run]
  (primary source; retrieved 2026-08-10)
- [Repeated capped benchmark run 31568808053][capped-benchmark-run]
  (primary source; retrieved 2026-08-12)
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
- [BenchmarkDotNet console arguments][bdn-console-arguments]
  (primary source; retrieved 2026-08-12)
- [BenchmarkDotNet measurement stages][bdn-how-it-works]
  (primary source; retrieved 2026-08-12)
- [BenchmarkDotNet job characteristics][bdn-jobs]
  (primary source; retrieved 2026-08-13)
- [Go benchmark runner source][go-benchmark-source]
  (primary source; retrieved 2026-08-12)
- [Go benchstat guidance][go-benchstat]
  (primary source; retrieved 2026-08-13)
- [NIST sample sizes required][nist-sample-sizes]
  (primary source; retrieved 2026-08-13)
- [NIST proportion confidence intervals][nist-proportion-intervals]
  (primary source; retrieved 2026-08-13)
- [NIST one-sided confidence limits for a standard deviation][nist-sigma-limit]
  (primary source; retrieved 2026-08-14)
- [NIST chi-square critical values][nist-chi-square]
  (primary source; retrieved 2026-08-14)
- [NIST measurement-process characterization][nist-measurement-process]
  (primary source; retrieved 2026-08-14)
- [NIST standard-deviation control chart][nist-standard-deviation-chart]
  (primary source; retrieved 2026-08-14)
- [FDA multiple-endpoints guidance][fda-multiple-endpoints]
  (primary source; retrieved 2026-08-14)
- [SPEC HPC 2021 result-computation rules][spec-hpc-result-computation]
  (primary source; retrieved 2026-08-14)
- [SciPy exact paired permutation-test contract][scipy-permutation-test]
  (primary source; retrieved 2026-08-14)
- [GitHub matrix jobs][github-matrix]
  (primary source; retrieved 2026-08-13)
- [GitHub reusable workflows][github-reusable-workflows]
  (primary source; retrieved 2026-08-13)
- [MySQL release model][mysql-release-model]
  (primary source; retrieved 2026-08-13)
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
[failed-six-target-run]:
  https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/runs/31903353665
[benchmark-run]:
  https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/runs/31332817720
[capped-benchmark-run]:
  https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/runs/31568808053
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
[bdn-console-arguments]:
  https://benchmarkdotnet.org/articles/guides/console-args.html
[bdn-how-it-works]: https://benchmarkdotnet.org/articles/guides/how-it-works.html
[bdn-jobs]: https://benchmarkdotnet.org/articles/configs/jobs.html
[go-benchmark-source]: https://go.dev/src/testing/benchmark.go
[go-benchstat]: https://pkg.go.dev/golang.org/x/perf/cmd/benchstat
[nist-sample-sizes]:
  https://www.itl.nist.gov/div898/handbook/prc/section2/prc222.htm
[nist-proportion-intervals]:
  https://www.itl.nist.gov/div898/software/dataplot/refman1/auxillar/propconf.htm
[nist-sigma-limit]:
  https://www.itl.nist.gov/div898/handbook/prc/section2/prc231.htm
[nist-chi-square]:
  https://www.itl.nist.gov/div898/handbook/eda/section3/eda3674.htm
[nist-measurement-process]:
  https://www.itl.nist.gov/div898/handbook/mpc/section2/mpc2.htm
[nist-standard-deviation-chart]:
  https://www.itl.nist.gov/div898/handbook/mpc/section2/mpc22.htm
[fda-multiple-endpoints]:
  https://www.fda.gov/regulatory-information/search-fda-guidance-documents/multiple-endpoints-clinical-trials
[spec-hpc-result-computation]:
  https://www.spec.org/hpc2021/docs/runrules.html
[scipy-permutation-test]:
  https://docs.scipy.org/doc/scipy/reference/generated/scipy.stats.permutation_test.html
[github-matrix]:
  https://docs.github.com/en/actions/using-jobs/using-a-matrix-for-your-jobs
[github-reusable-workflows]:
  https://docs.github.com/en/actions/sharing-automations/reusing-workflows
[mysql-release-model]:
  https://dev.mysql.com/doc/refman/9.7/en/mysql-releases.html
[rustc-perf-deployment]:
  https://github.com/rust-lang/rustc-perf/blob/main/docs/deployment.md
[rustc-perf-collector]:
  https://github.com/rust-lang/rustc-perf/blob/master/collector/README.md
[cloud-variability]: https://arxiv.org/abs/2504.11826
[measurement-bias]: https://dl.acm.org/doi/10.1145/1508244.1508275
