# Engineering Standards

This document defines how Doka applies the shared engineering rules to the
.NET 10 provider line. The applied rules and project exceptions are readable
here without access to another repository or a private planning directory.
Implementation boundaries remain in [Architecture](architecture.md), contribution
instructions in [Contributing](../CONTRIBUTING.md), and individual decisions in
the [ADR corpus](decisions/README.md).

## Adopted Snapshot

The shared catalog and repository standard were adopted on 2026-11-19. These
SHA-256 identities record the exact source documents used for the adoption:

| Source document | Snapshot | SHA-256 |
| --- | --- | --- |
| Engineering-Standards.md | 2026-11-19 | `cd554e9873131182c4f36cbe3ea8e0448c0e1e183e5245015049487690aef1c0` |
| Repo-Standard.md | 2026-11-19 | `8167f674a72659d5b199a77d50a5f145b20acbd397385f204f82d928f05931de` |

The hashes identify the adoption input; the applied project rules below and
their repository references are the documentation a contributor needs. A
later change to the shared catalog does not silently change this baseline.
Updating the baseline includes reviewing the changed rules and recording any
new project-specific decisions.

## Responsibilities and Resource Use

- Maintainability takes priority, followed by measured performance and efficient
  memory use. Responsibilities have clear owners; shared semantics are defined
  once and consumed rather than independently reconstructed.
- EF adapters retain their compiler, model, query, storage, migration, and
  design-time boundaries. Feature organization does not introduce new packages,
  dispatch frameworks, or a parallel implementation of EF behavior.
- Methods around 40 lines and files around 400 lines are review guidelines,
  not mechanical gates. Substantial coordinators use named steps; topic partials
  keep a large service navigable. Splitting must clarify responsibilities and
  preserve execution, initialization, exception and resource ownership semantics.
  A concrete exception receives a WHY comment. Helpers are not created solely
  to meet a line count.
- Optional features allocate and retain only their required state. Local
  exclusive mutation is allowed; functional composition does not require copies,
  closures, or LINQ allocations per element.
- Resource claims use the actual collection types and complete methods. Arrays
  and spans may use `foreach`; suitable other indexed collections prefer `for`.
  No materialization or repeated `ElementAt` access is introduced for syntax.
  A loop's WHY and local Rider comments explain the concrete choice without
  inventing a measurement.
- Use spans for suitable synchronous contiguous-data processing. Memory views,
  pooled buffers and async state retain explicit ownership and lifetime.
  Pattern matching, range syntax and collection expressions preserve evaluation
  order, binding, copies, null behavior and server-side query translation.
- Benchmarks are explicitly started diagnostic work. CPU time, wall-clock time,
  latency and throughput cannot reject a run because of hardware or load.
  Functional correctness, evidence integrity and qualified resource checks
  remain enforced. See [Performance Evidence](operations/performance-evidence.md).

## C# and API Contracts

- Use nullable reference types and fix correctness diagnostics at their cause.
  Global imports belong in each project's `Imports.cs`; aliases preserve exact
  symbol bindings. Classes and structs use explicit constructors. Positional
  records remain suitable for data contracts.
- Narrow imports for upstream specification models and discovery adapters may
  remain file-local when a global import would broaden model or framework name
  binding. Shared namespaces are centralized without importing unrelated model
  names into every test.
- All public APIs have meaningful XML documentation. Internal and private APIs
  also have it where it explains a useful contract, invariant, ownership,
  parameter or side effect in the IDE.
- Non-obvious implementation choices receive a short `// WHY:` at their owner.
  Comments explain the reason rather than repeat the following instruction.
- Own async work requires a final, non-optional `CancellationToken` and passes
  it to the actual work. Framework signatures remain valid. An intentionally
  uncancellable caller uses `CancellationToken.None` explicitly. Necessary
  cleanup stays tokenless and documents the protected completion boundary.
- Native asynchronous database operations remain asynchronous. Started work
  finishes or is fully observed before its resources leave scope. Cleanup must
  preserve the documented priority of the original failure and later failures.
- Shared state has explicit ownership and qualified synchronization. Operations
  do not overlap on the same `DbContext`. A caller-owned connection, transaction,
  buffer or host object is not disposed by an operation that only borrows it.
- Public API changes retain the shipped/unshipped baseline, compatibility review,
  runtime and code-generation tests. Modern syntax is used only when the required
  compiler, consumer, documentation and API tooling preserve those contracts.
- New external dependencies need an explicit decision. Existing SDK analyzers and
  reviewed API analyzers remain the enforcement baseline; no extra analyzer or
  testing framework follows merely from adopting this document.

## Format and Cleanup

The checked-in `.editorconfig` defines the Rider layout, with a 120-character
C# line width. Use the versioned `Doka-Rider-Cleanup` solution profile.
Automatic field-to-property conversion, setter removal and Markdown
reformatting stay disabled. Cleanup still requires a reviewed diff and build;
profile storage alone does not prove that a change preserves behavior.
`dotnet format whitespace` is not used.

Separate logical groups with blank lines. Put a blank line after a completed
`if`/`else`/`switch` construct and after a multiline variable initialization.
Put a blank line before `return`, except in the agreed two-code-line block
case; keep explanatory comments attached to their statement. Empty bodies and
short expressions retain the existing Rider rules.

IDE suggestions are reviewed against semantics and this document. A proven
false positive or a deliberate, justified style choice may receive a local
WHY and the matching `ReSharper disable once` comment. Correctness, nullability
and security diagnostics are not hidden by blanket suppressions. In particular,
trusted test DDL receives a narrow explanation rather than disabling SQL safety
analysis for the complete integration project.

Code, comments, documentation and commit messages use US English and ASCII
unless an explicit content-specific exception applies. C# files use LF so
multiline raw-string values remain stable across checkout platforms. Git does
not track build outputs, caches, editor metadata or temporary verification files.

## Tests and Existing Project Baselines

Each test has one observable behavior and a meaningful, single sequence of
`// Arrange`, `// Act`, `// Assert`, separated by blank lines. An expected exception
is captured in Act; its details and final state are asserted afterward. Several
assertions on the same result are appropriate. A coherent transaction, retry or
concurrency scenario can require dependent steps within one Act phase.

Independent input variants use independently discoverable theory cases. Native
database tests cover relevant positive, negative, boundary, cancellation and
recovery behavior on the supported engine matrix. Shared servers do not remove
per-test state isolation or resource ownership requirements.

The following project-specific baselines remain deliberate:

| Area | Applied Doka contract |
| --- | --- |
| Test names | Existing names and inherited EF test identities remain unchanged. The owner explicitly retained this convention for this adoption; no bulk renaming to `Method_Scenario_ExpectedBehavior` is performed. |
| Test platform | The .NET 10 line retains xUnit 2 and VSTest. The shared template's xUnit 3/MTP setup is not an instruction to migrate this line. |
| Test configuration | The existing VSTest `.runsettings`, target-selection and runner configuration remain valid. MTP-specific behavior is not assumed for VSTest. |
| Parallelism | Independent tests run within the established fixture and database ownership limits. The integration suite's bounded class parallelism protects its shared servers; process-wide pool resets remain exclusive. |
| EF specifications | Inherited EF specification shape and generated fixtures retain their upstream contracts; local adapters and dispositions remain explicit. |
| API diagnostics | RS0026 stays enabled, with justified per-method exceptions for established optional-overload contracts. PublicAPI files remain authoritative. |
| Dependency audit | Normal local builds retain advisory warnings; CI and explicit release evaluation stay strict. Audit retrieval failures remain fatal. This preserves the existing security contract rather than adopting the template's separate audit-pipeline switch. |
| Release and repository checks | Existing quality, compatibility, package-consumer, coverage, ADR and release checks remain required. The template's initial-project sequencing does not remove mature repository obligations. |

Assembly-level declarations live in the owning project's
`Properties/AssemblyInfo.cs`; SDK-generated package and version metadata remains
SDK-managed. Each example project lists its imports explicitly and inherits
disabled implicit usings. Examples are executable scenarios with relevant
result assertions, not compile-only samples.

## Documentation and Adoption Review

Feature guides describe supported behavior, examples and failure boundaries.
ADRs explain alternatives and consequences without duplicating whole guides.
Repository links resolve inside this repository or to publicly accessible
primary sources; private working paths are not required to understand a rule.

ADR front matter contains current metadata. `date` matches the last dated
history entry, while the first recording entry retains its original date.
History stays chronological and does not invent approvals. Responsible people
are named, and `informed` is exactly `["@doka-labs/core-maintainers"]`; the group
name alone proves neither approval nor notification. The existing
[MADR profile](decisions/MADR-PROFILE.md) and validator enforce the project format.

This adoption records the applicable rules and explicit exceptions. It does not
claim that every historical implementation already satisfies every guideline.
Changed areas are reviewed against their responsibilities and actual evidence;
file length alone does not trigger a repository-wide rewrite. Existing source,
test and release contracts remain visible throughout an incremental alignment.
