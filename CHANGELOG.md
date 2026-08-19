# Changelog

All notable changes to this project will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [10.0.0-rc.10] - 2026-08-19

This release candidate supersedes `10.0.0-rc.9`. It carries the NuGet readback
corrections and the provider fixes found through consumer testing of temporal
models, GUID mappings, JSON constructors, invisible columns, entity splitting,
and spatial behavior.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.10
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.10
```

### Fixed

- Treat independently delayed NuGet package and symbol indexing as retryable
  until the bounded public-readback deadline. Every package push can now resume
  an already accepted but not yet visible version, while canonical byte,
  Portable PDB, and repository-signature verification still fail closed.
- Treat matching package bytes that precede their NuGet.org repository
  signature as pending until the same deadline. Public package URLs now use the
  dynamically discovered V3 package-content endpoint and accept only canonical
  normalized release versions; completion consumes the exact identity written
  by the readback producer.
- Materialize every supported NetTopologySuite geometry type from the runtime
  values returned by MySqlConnector across tracked, no-tracking, scalar,
  Include, and split-query paths.
- Enforce exact spatial-function capabilities by engine version. MariaDB
  `Crosses` now follows NetTopologySuite's DE-9IM contract, unavailable
  `IsValid`, collection aggregates, and quadrant-segment Buffer overloads fail
  during translation, nullable `Crosses` operands preserve SQL `NULL`, and
  supported MySQL/MariaDB versions retain native SQL.
- Enforce MariaDB column SRIDs with provider-owned check constraints and recover
  the `HasSrid(...)` contract during reverse engineering without scaffolding a
  duplicate user check constraint.
- Make context-level `Char36` GUID mappings materialize the connector's `Guid`
  and text reader shapes across connection strings, `DbConnection`, and
  `MySqlDataSource`. Explicit `Binary16` overrides now use provider-owned
  big-endian bytes, while the unannotated `Binary16` default keeps its native
  no-converter path.
- Keep `AUTO_INCREMENT` exclusively on the principal table of an entity-split
  mapping. Secondary shared primary/foreign keys remain non-generating through
  migrations, generated snapshots, live CRUD, and cascading deletes.
- Delay application-time period defaults until action-based configuration has
  completed, so typed or named endpoints do not leave unused `ValidFrom` and
  `ValidTo` shadow properties in models, snapshots, or migrations.
- Translate ordinary `params` calls to `EF.Functions.JsonArray` and
  `JsonObject` into variadic server functions, including parameterized values,
  empty constructors, JSON-null preservation for SQL `NULL` arguments, and
  focused rejection of invalid argument shapes.
- Preserve MySQL 8.0.23+ and MariaDB 10.3.3+ `INVISIBLE` column annotations
  through create, add, alter, snapshot, designer, and reverse visibility
  migrations.
- Preserve system-time, application-time, and bitemporal metadata in generated
  migration snapshots and designer models, including custom period columns, so
  an unchanged model no longer produces a redundant follow-up migration.
- Propagate a temporal table contract to convention-owned `OwnsOne` mappings
  that share the physical table. Separately stored current-only owned
  collections now fail with a focused diagnostic before historical and current
  rows can be mixed.
- Preserve the `Guid` model type for provider-native `Char36` properties in
  generated migration snapshots and designer models, including client-generated
  primary keys and dependent foreign keys.
- Order foreign-key removal and recreation around related `varchar(36)` to
  `char(36)` column migrations in both directions while preserving populated
  relationships, constraint identity, delete behavior, and dependent indexes.

## [10.0.0-rc.9] - 2026-08-17

This release candidate supersedes `10.0.0-rc.8`. Qualification completed,
but publication stopped before NuGet received any package because GitHub's
published-tag endpoint cannot return a draft release created moments earlier.
The signed RC 8 tag is therefore consumed even though its package bytes were
never published.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.9
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.9
```

### Fixed

- Rediscover a staged GitHub release through the authenticated, paginated
  release inventory instead of the published-tag endpoint. Draft creation and
  asset visibility now use bounded polling, and duplicate drafts fail closed
  without choosing or deleting remote state.
- Keep release and benchmark artifacts within the repository's 30-day
  retention limit. Benchmark drift confirmation now binds two independent
  hosted attempts from the same scorecard cycle instead of depending on two
  monthly artifacts that cannot coexist for the required window.

## [10.0.0-rc.8] - 2026-08-16

Release qualification no longer spends an immutable version before the
candidate is known to be valid. One hosted run qualifies exact untagged `main`
package bytes, waits for the signed tag and protected approval, then publishes
and reads back the same bound candidate. Performance remains independent
engineering evidence and has no release authority.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.8
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.8
```

### Added

- Add a public, scoped migration-operation handler SPI for extension packages.
  Exact custom operation ownership, immutable staged commands, provider
  baseline rendering, and fail-closed conflict and error behavior compose
  without exposing or replacing the internal migrations SQL generator.
- Expose an exhaustive migration feature projection for all six active LTS
  profiles, including native, emulated, and engine-unsupported routes for JSON,
  indexes, generated columns, temporal features, sequences, prepared DDL,
  atomic DDL, and transactional DDL.
- Add stable handler diagnostics: `MySqlEventId` 1110 through 1114, one
  internal activity, three counters, one duration histogram, bounded tags, and
  an operator runbook that excludes SQL and plugin exception payloads.
- Add two independent conformance handlers, exact registry and result-contract
  tests, packed-consumer compilation, executable normal and idempotent scripts,
  six-LTS runtime, tooling, and bundle lifecycles, and a 0/1/8-handler dispatch
  benchmark.

- Support every active upstream LTS line: MySQL 9.7 and MariaDB 10.11 / 12.3
  join the existing MySQL 8.4 and MariaDB 11.4 / 11.8 matrix. Exact image pins,
  support-policy classification, specification dispositions, live integration,
  migration deployment, runnable examples, and release qualification now
  share the same six-target contract.
- Execute CTE, temporal-table, and bitemporal live contracts on every newly
  admitted line. MariaDB 12.3 additionally executes its native CTE
  data-modification grammar while 10.11 / 11.4 / 11.8 retain the documented
  engine limitation.
- Freeze the evidence a release is qualified on in one canonical manifest.
  Every gate is derived into a result that states which commit and tree it
  describes, which workflow produced it, under which run and attempt, and which
  artifact carries the bytes. Selection happens once; later steps re-check the
  frozen identities but never reselect.
- Read back the exact protected check-run and workflow-run attempt selected by
  that manifest before publication. Its canonical response digest and manifest
  SHA-256 are immutable release evidence, so a later CI rerun cannot replace
  the qualification attached to a release.
- Verify the published payload against the manifest at publication. Every file
  digest is recomputed from the packages about to be published, and a missing,
  added, or altered file fails closed.
- Verify branch and signer prerequisites before untagged hosted qualification.
  `eng/pre-tag-check.sh` allocates no runner, writes no file, and creates no
  tag.

### Changed

- Qualify untagged exact-current `main` package bytes before creating a release
  tag. The same workflow run binds the later signed tag, stages a GitHub draft,
  obtains a protected short-lived NuGet credential, resumes partial package
  publication safely, finalizes the complete immutable GitHub release, and then
  records public byte, symbol, and repository-signature verification.
- Retry a benchmark attempt only for a measurement or environment condition.
  The decision travels with the attempt receipt, so a retry can never select
  away a verdict about the code.
- Bound a paired comparison with nested watchdogs rather than with a sum. A
  side run stops at the smaller of its own hang deadline and what remains of
  the comparison, and a block that cannot finish inside the remaining budget is
  not started. Either stop reports a measurement condition, which is retryable,
  instead of a verdict about the provider.
- Size each paired workload by the precision it reached. A block starts at the
  smallest population the profile accepts and extends only until the registered
  error budget is met, so coverage and block count are kept without paying a
  fixed population twice per block.
- Reconcile the public README, contributor guide, release governance,
  performance runbook, security settings, and threat model with pre-tag hosted
  qualification, same-run publication, and the read-only pre-tag lookup.

### Fixed

- Parse the authoritative MariaDB release token when `@@version` carries the
  legacy `5.5.5-` client-compatibility prefix. This keeps automatic detection
  and support-policy validation aligned on MariaDB 10.11.
- Reconstruct MariaDB 10.11 system- and application-time boundaries from its
  available generation-expression and canonical table-definition metadata,
  closing the reverse-engineering gap left by the 11.4-only period catalogs.
- Verify the exact EF Core package set a patch matrix resolved. The previous
  check counted packages, so a missing package could be compensated by a
  duplicate of another.
- Keep release engine qualification bounded and fail-closed. Runtime smoke now
  declares its trimming requirements, EF Core 10.0.11 carries exact six-target
  contracts, and future unregistered patches stop after dependency resolution
  before expensive test execution. Candidate qualification reuses the
  commit-exact floor proof and fully executes only the latest compatible patch.
- Build publication-readiness assemblies inside the clean finalization runner
  from the exact matrix-resolved EF Core and MySqlConnector patches. The gate
  no longer depends on build output left by another job or rewrites committed
  package lock files while qualifying a candidate.
- Reproduce a paired interval across processes. The per-test resampling seed was
  derived from a hash that Python randomizes per process, so a reviewer could
  not reproduce the number a release was decided on.
- Record a paired measurement as the attempt it is. The recorder looked for the
  historical verdict under its historical name, so every qualified paired run
  failed in the step after the measurement and produced no selectable result.
- Decide an absolute budget on every block rather than on the last one. A
  candidate that crossed a catastrophe ceiling early and recovered qualified.
- Give the release preflight the permission its own API calls need. An explicit
  permissions block inherits nothing, so resolving the qualification check
  would have been refused before any gate ran.
- Measure the paired comparison on the profile it reports. The benchmark driver
  recognized two profile names and sent every other one to the smoke subset, so
  a paired run measured fifteen of fifty-five workloads at one row count while
  its evidence claimed the complete matrix.
- Record a paired measurement under the profile that measured it. The workflow
  passed the profile it dispatches with, which the recorder refuses because the
  evaluation carries the block profile.
- Reconcile a release against the gates the policy declares. The writer and the
  validator carried two independent inventories and two schema versions, so the
  final step of an otherwise successful candidate could not complete.
- Compare paired sides that needed different sample populations. Extension is
  driven by precision, so a noisier side needs more samples to reach the same
  error budget; requiring equal counts made a measured run diverge on sixteen of
  fifty-five workloads and would have invalidated every paired comparison.
- Classify a deadline stop as the measurement condition it is. The outer
  watchdog reported its own timeout code, which the attempt path files as
  invalid evidence -- a state no retry can clear.
- Keep an ordinary build green after a paired comparison. Both sides shared one
  intermediate tree, so the reference restore left package references behind
  that the next build imported alongside the project references and failed on.
  Each side now builds under its own artifacts root, which keeps the per-project
  separation a global intermediate path destroys.
- Decide measurement precision the same way on both sides of the language
  boundary. The runner measured the standard error against the median and the
  evaluator against the mean, so a right-skewed population the runner had
  judged insufficient could still be accepted here.
- Refuse evidence that cannot support a verdict. A single observation reported
  zero dispersion, a non-numeric sample escaped as an ordinary failure, and the
  attempt recorder reads that failure as a regression -- so broken evidence
  could convict a provider it never measured. A foreign schema version, a
  foreign kind, and a missing or empty identity are now refused the same way,
  before any statistic runs.
- Let the measurement decide whether it converged. The runner records per side
  and per block whether a workload reached its precision or stopped at its
  sample cap; that verdict was stored and never read, so a run that never
  settled could still qualify. A capped workload is now withheld from its
  metric family altogether, because leaving its p-value in would move the
  false-discovery threshold for every workload that did converge -- while a
  regression in one of those still decides the release.
- Hold every raw block report to the canonical workload contract. The paired
  path checked only the fields it read, so a foreign document, a termination
  the runner cannot produce, and a statistic that does not follow from its own
  samples all reached the comparison.
- Treat the evaluation entry point as its own trust boundary. It was handed a
  finished evidence document and trusted it, so an incomplete workload matrix,
  a convergence claim without the duration floor, and a cap claimed at a
  fraction of the actual cap could all qualify a release -- and nothing
  downstream recomputes the statistics from the raw reports. The structural
  half is now closed with it: a missing block count made every other count
  check vacuous, candidate measurements covering one block held the candidate
  to a fraction of its run, a dropped environment record made the shared-machine
  claim unfalsifiable, and absent provenance left the numbers describing nothing
  in particular. Provenance is now bound rather than merely present: the
  contract digest must name the contract the evaluation loaded, two empty
  environment objects no longer prove a shared machine by agreeing about
  nothing, and every structure is type-checked before it is read -- reading
  first turned broken evidence into an ordinary failure, which the attempt
  recorder reads as a regression. The same discipline now covers the test and
  resource structures, the provenance fields are validated as real revisions
  and digests rather than as non-empty text, the driver identity a dirty
  working tree produces is a full digest the evaluator accepts instead of a
  shape it refused, and the contract binding is required by the signature so
  no caller can obtain a verdict without it. The absolute ceilings now take the
  workload family from the contract instead of from the document being judged,
  so a workload cannot re-declare itself into a more generous budget. Every
  ceiling input is now the measurement the paired decision is formed from:
  the recorded nanosecond samples of each block decide the latency budgets,
  where a per-block summary used to, so a candidate and its reference that
  degraded together can no longer pass a budget by summarizing themselves as
  fast. The summary stays in the document as an audit view and has to agree
  with the measurement it summarizes. The calibration travels with the evidence
  for the same reason, so the views of a block -- normalized samples, raw
  latencies, recorded sample count -- are proven to describe one measured
  population rather than assumed to. The divisor itself is rebuilt from the
  recorded calibration pulses instead of being read, so a document cannot
  leave the raw latencies untouched and rescale a regression into a
  qualification by choosing what to divide by. The candidate audit summary is
  now exactly the seven fields it is documented to be, produced by one function
  for the runner and the fixtures alike, so the document no longer carries a
  second unchecked copy of the measurement beside the canonical one.
- Keep the finalization share out of the sustained-use run. The reserve was
  withheld from the block forecast and from every side watchdog, then handed to
  the soak in full, which could leave nothing for assembling and evaluating the
  evidence.

### Removed

- Remove the local release rehearsal. It ran the whole candidate to buy
  certainty before spending a version number, could not cover the gate that kept
  failing, and cost more than the tag it was meant to protect.

## [10.0.0-rc.7] - 2026-08-09

This release candidate supersedes `10.0.0-rc.6`, which failed in the first
stage it reached. A test read an environment variable that a release runner
sets and a workstation does not, so it could not fail where it had been
verified. Both the test and the rehearsal it covers now ignore the caller's
environment entirely.

The engine images are held to a contract for the same reason. They decide
which server a candidate measures, and the pin lives in six places while only
one of them was ever offered for update.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.7
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.7
```

### Fixed

- Propose engine image updates again. The configured Dependabot ecosystem does
  not read compose files, so no security fix for the MySQL or MariaDB images
  had ever been offered, and one was available.
- Keep a rehearsal independent of the shell it runs in. It accepted three
  documented inputs and silently forwarded everything else, so a variable left
  over from an earlier run could compare a foreign baseline, skip measurement,
  or disable the orchestrator's own timeout.

### Changed

- Update the MySQL test and evidence image from 8.4.10 to 8.4.11, the current
  patch of the supported 8.4 LTS line. The release carries InnoDB, optimizer,
  JSON-schema, and replication fixes; the one behavior change that reaches a
  provider is stricter record-size validation for
  `ALTER TABLE ... ALGORITHM=INSTANT`. MariaDB 11.4 and 11.8 already carry
  their latest patches.

### Added

- Add a repository gate that holds every copy of an engine image pin against
  the compose stack, rejecting a pin that is malformed, missing, duplicated,
  assigned to the wrong target, or unknown to the source. The pin appears in
  two workflows, the performance contract, and a C# constant, and only the
  compose stack receives updates.
- Add a contract test that runs the real baseline-mode resolver against the
  checked-in contract and accepted baseline. A contract edited without a new
  version stops the hosted benchmark before it measures anything, and no other
  gate asks that question.
- Validate the contract version as a real calendar date with an optional
  same-day revision counting from two, in one place that the benchmark
  workflow reads through. The workflow previously carried a second pattern of
  its own, which rejected a version the tooling considered valid and stopped
  the run before the resolver could report anything.

## [10.0.0-rc.6] - 2026-08-09

This release candidate supersedes `10.0.0-rc.5`, whose qualification stopped at
the readiness gate before any stage ran. That gate required both engines to
share a run identifier, which the release matrix cannot produce: it runs one
measurement job per engine and names that job in the identifier. Baseline
promotion had already dropped the same requirement; the gate kept its own copy.

Qualifying a candidate now has a local rehearsal, because a pushed tag is
immutable and each failed attempt so far cost a version number that can never
be reused.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.6
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.6
```

### Fixed

- Accept baseline evidence measured by the release matrix, so readiness rests
  on the commit and source hash that prove both engines measured the same
  software rather than on an identifier that names one job.
- Resolve a packed version by its own package id. The spatial package carries
  the provider id as a prefix, so it answered for the provider whenever the
  filesystem listed it first, and the candidate then reported a version
  mismatch between two correctly built packages.

### Added

- Add `eng/rehearse-release.sh`, which runs the release orchestrator against
  the working commit without a tag, so a defect in the qualification path
  costs a local run instead of a version number.

## [10.0.0-rc.5] - 2026-08-09

This release candidate supersedes `10.0.0-rc.4`, which failed hosted
qualification on the same rejected baseline as its predecessor: automating the
proposal had removed the manual handover without making the accepted baseline
current. Refreshing it required repairing the measurement path first, which is
what this candidate carries. No candidate from `10.0.0-rc.1` through
`10.0.0-rc.4` reached publication, so every change listed under those versions
ships to users here for the first time.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.5
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.5
```

### Added

- Add one portable system-versioned temporal-table model and query contract for
  MySQL 8.4 and MariaDB 11.4 / 11.8. MariaDB uses native system versioning;
  MySQL uses transactional InnoDB history tables and provider-owned triggers.
- Add `TemporalAsOf`, `TemporalAll`, `TemporalFromTo`, `TemporalBetween`, and
  `TemporalContainedIn` query roots with UTC boundary validation and mandatory
  no-tracking semantics.
- Add deterministic temporal migrations, native and emulated reverse
  engineering, generated model-code round trips, schema-safety validation, and
  live engine-matrix contracts.
- Add complete non-recursive and recursive CTE conformance through EF Core's
  parameterized, composable SQL query roots, including the documented
  MariaDB 11.4 / 11.8 data-modification boundary.
- Add a live temporal-table and recursive-CTE example to the release-candidate
  matrix.
- Add temporal TPT and TPC mapping with independent physical-table period
  metadata, migration ordering, query translation, and conformance coverage.
- Add typed MariaDB application-time and bitemporal configuration, migrations,
  reverse engineering, generated model code, `WITHOUT OVERLAPS`, and
  `FOR PORTION OF` update and delete roots.
- Add complete `JSON_TABLE` expression quoting for compiled models and
  precompiled query generation.

### Fixed

- Restore release qualification, which no candidate had passed. Measurement
  sampling now stops at the configured cap instead of failing, that cap is
  sized for the population the accepted baseline actually needs plus the
  spread between runs, and a workload whose samples are too short to reach
  the duration floor is recalibrated rather than discarded as inconclusive.
- Accept baseline evidence measured by the release matrix. Promotion had
  required every engine to share one run identifier, which names a single
  measurement job and therefore differs per engine by construction. Identity
  now rests on the commit and source hash that establish both engines
  measured the same software.
- Bound measurement retries and preserve baseline provenance, so a second
  attempt on an independent runner settles an inconclusive measurement
  instead of repeating indefinitely.
- Reject verification results that prove nothing, and repair the hosted lint
  gate so a failed toolchain install ends the run rather than surfacing as a
  lint finding.

### Changed

- Harden repository verification for public operation: workflow actions are
  pinned by digest, tokens carry least privilege, and shell, workflow, and
  static-analysis gates run on every change.

### Documentation

- Document the temporal and CTE support matrix, public APIs, schema lifecycle,
  engine constraints, runnable verification, and retrieved primary sources.
- Document the EF Core 10 complex-type contract and separate its upstream
  boundaries from provider and engine responsibilities.
- Record how the measurement sample cap is dimensioned, and which security
  settings the repository relies on, so both survive a change of maintainer.

## [10.0.0-rc.4] - 2026-08-08

This release candidate supersedes `10.0.0-rc.3`, which failed hosted
qualification because the accepted performance baseline had been recorded under
an earlier evidence contract. It closes the manual handover in baseline
acceptance, so that evidence is produced, validated, and proposed by the
benchmark workflow itself rather than moved between runs by hand.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.4
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.4
```

### Fixed

- Validate hosted performance evidence before qualification begins, so a
  candidate fails on its preflight rather than after the full matrix has run.
- Produce baseline proposals from the benchmark workflow itself, with no
  artifact download, run-identifier handover, or second dispatch between
  measuring and proposing.
- Keep the fixed large-write and HiLo populations inside their deadlines by
  batching per-context inserts and preserving cancellation across the
  synchronous and asynchronous setup paths.
- Bound scorecard measurement retries to a second attempt on an independent
  runner, and carry the contract provenance of an accepted baseline so a
  rerun cannot silently rebind it to a different contract.

## [10.0.0-rc.3] - 2026-08-07

This release candidate supersedes `10.0.0-rc.2` after hosted qualification
exposed the same undersized hang deadline for the fixed 10,000-row
`SaveChanges` evidence populations.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.3
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.3
```

### Fixed

- Preserve all 128 independent scorecard observations for both 10,000-row
  `SaveChanges` workloads while assigning their fixed database work a bounded
  300-second hang deadline. Latency, allocation, GC, and historical-regression
  budgets remain unchanged.
- Cover every fixed large write population with one regression contract so a
  synchronous, asynchronous, SaveChanges, or HiLo variant cannot silently lose
  its workload-local deadline.

## [10.0.0-rc.2] - 2026-08-04

This release candidate supersedes `10.0.0-rc.1` after hosted qualification
exposed an undersized per-workload deadline for the fixed large HiLo evidence
population.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.2
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.2
```

### Fixed

- Preserve the complete large HiLo evidence population while applying bounded
  workload-local timeout floors on shared hosted runners. Sample counts,
  latency budgets, allocation budgets, GC budgets, and regression budgets
  remain unchanged.

### Documentation

- Define the canonical green-`main`, signed-tag, hosted-candidate, NuGet
  publication, public-readback, and immutable-release procedure.
- Reconcile installation, supported-engine, hosted-target, example, and
  project-layout guidance with the current provider contract.

## [10.0.0-rc.1] - 2026-08-04

First public release candidate for the `10.0.x` package line.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.1
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.1
```

### Changed (breaking)

- Default `decimal` mapping changed from `decimal(65,30)` (the MySQL maximum) to `decimal(18,2)` (the real-world common case for currency). Unannotated decimal properties now resolve to the new default; properties annotated with `[Precision(p, s)]` or `HasPrecision(p, s)` are unaffected. Existing schemata that have unannotated decimal columns wider than `(18,2)` should be audited via `SELECT MAX(ABS(x))` before the next migration runs. The `ImplicitDecimalPrecisionDefaulted` warning fires on first use per `DbContext`. See ADR D-006 for the full rationale.
- GUID stored as `char(36)` / `varchar(36)` now declares `unicode: false`, matching the ASCII-only canonical hex representation. The on-disk footprint and the network payload shrink to one byte per character. Existing schemata that declared GUID columns with utf8mb4 collation continue to read and write correctly; the migration only re-emits the type mapping.
- Server versions outside MySQL 8.4 and MariaDB 11.4 / 11.8 now require
  the explicit `MySqlServerVersionCompatibilityMode.AllowUnsupported`
  opt-in. Legacy, unvalidated, and future lines remain executable without
  a support guarantee and emit `MySqlEventId.UnsupportedServerVersion`.
- Object-bearing provider diagnostics now expose a stable 16-character
  `ObjectScopeId` instead of raw model or database object names. Invalid
  configuration events expose a bounded `Reason` value and no longer carry
  caller-provided messages or connection-string representations. Detailed
  validation errors remain available through the thrown exception.

### Added

**Core package (`Doka.EntityFrameworkCore.MySql`)**

- Entity Framework Core 10 provider for MySQL 8.4 LTS and MariaDB 11.4 / 11.8 LTS
- Three connection configuration paths: connection string, `DbConnection`, and `MySqlDataSource`
- `MySqlServerVersion` with explicit `MySql(...)` / `MariaDb(...)`
  factories, `AutoDetect(...)`, support classification, and an
  unsupported-version compatibility mode
- Separate engine-fact and provider-support contracts; provider
  capabilities resolve as native, emulated, or unavailable because of an
  engine limitation
- GUID storage format selection: `Binary16` (default) and `Char36`, both configurable via `DefaultGuidFormat(...)` and per-property `HasMySqlGuidFormat(...)`
- Value generation strategies: `AutoIncrement`, `ClientGuid`, and `HiLo` via `UseHiLo(...)`
- Native MariaDB sequences (10.3+) plus table-based sequence emulation for MySQL
- Advisory-lock-backed migration serialization via `GET_LOCK` / `RELEASE_LOCK` on a dedicated non-pooled connection
- Idempotent migration scripting via `DROP PROCEDURE` / `CREATE PROCEDURE` stored-procedure wrappers (`dotnet ef migrations script --idempotent`)
- JSON pipeline: native JSON on MySQL, `longtext COLLATE utf8mb4_bin CHECK (JSON_VALID(...))` alias on MariaDB, with scaffolding detection
- JSON CLR-type preservation: `JsonElement`, `JsonDocument`, `JsonNode`, `JsonObject`, `JsonArray` with embedded `ValueConverter` and deep-equality `ValueComparer`
- Query translation coverage for common string, DateTime, DateOnly, TimeOnly, Math, and aggregate (`string.Join` -> `GROUP_CONCAT ... SEPARATOR`) operations
- `EF.Functions` extensions: `Regexp`, `Match`, `MatchInBooleanMode`, `JsonSet`, `JsonReplace`, `JsonRemove`, `JsonArray`, `JsonObject`, `JsonDepth`, `JsonLength`, `JsonType`, `JsonKeys`, `JsonContains`
- Engine-aware REGEXP dialect (`REGEXP_LIKE(...)` on MySQL, infix `REGEXP` on MariaDB)
- Full-text search via `MATCH(col) AGAINST(term [IN BOOLEAN MODE])` with sentinel-rewrite SQL generation
- MySQL 8.0.23+ and MariaDB 10.3.3+ `INVISIBLE` column support via the
  `IsInvisible()` fluent API
- SQL-generation hardening: shared ASCII grammar-token validation for
  charsets, storage engines, and query, table, and column collations;
  JSON-path property-name escaping for single quotes and backslashes
- Transient-exception detection with depth-limited inner-exception traversal for retrying execution strategies
- Stable `MySqlEventId` catalog and seven logger categories (`Configuration`, `Query`, `Update`, `Migrations`, `Scaffolding`, `Resilience`, `Spatial`)
- Trim-aware runtime surface; NativeAOT readiness deferred until upstream EF Core stabilizes the precompiled-query workflow (see ADR D-017)

**Optional spatial package (`Doka.EntityFrameworkCore.MySql.NetTopologySuite`)**

- `UseNetTopologySuite()` opt-in activation for NTS-backed spatial types
- Geometry-first type mapping for `Point`, `LineString`, `Polygon`, `MultiPoint`, `MultiLineString`, `MultiPolygon`, `GeometryCollection`, and `Geometry`
- Spatial index DDL generation (`CREATE SPATIAL INDEX`) with model-validator rejection of unique, multi-column, or non-NTS spatial indexes
- SRID-aware scaffolding and design-time warnings for unsupported spatial configurations

### Tested

- 668 unit tests and 463 provider-local functional tests
- Upstream specification contracts covering 29,746 MySQL 8.4,
  29,410 MariaDB 11.4, and 29,411 MariaDB 11.8 test cases
- 171 discovered live integration cases: 166 supported-matrix cases and five
  explicit skips reserved for the external-only MySQL 8.0 baseline
- Live integration coverage against MySQL 8.4 LTS, MariaDB 11.4 LTS, and
  MariaDB 11.8 LTS, plus an external-only opt-in MySQL 8.0 compatibility
  baseline
- Representative dual-engine benchmark smoke and scorecard runs

[Unreleased]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/compare/v10.0.0-rc.10...HEAD
[10.0.0-rc.10]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.10
[10.0.0-rc.9]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.9
[10.0.0-rc.8]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.8
[10.0.0-rc.7]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.7
[10.0.0-rc.6]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.6
[10.0.0-rc.5]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.5
[10.0.0-rc.4]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.4
[10.0.0-rc.3]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.3
[10.0.0-rc.2]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.2
[10.0.0-rc.1]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.1
