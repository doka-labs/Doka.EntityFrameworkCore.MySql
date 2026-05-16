# D-013 -- EF-Core-11 / .NET 11 Major-Upgrade-Vorgehen

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** repository-wide major-version upgrade strategy
- **Implementation:** deferred to a follow-up commit (trigger-driven)

## Context

EF Core 11 preview builds are already available (the pre-plan probe
recorded `11.0.0-preview.4` at retrieval time). The Microsoft .NET 11
release is on the published roadmap. Both releases will land within the
v1.0.x lifetime of this provider.

The provider's foundation has explicit EF Core 10 commitments:

- D-001 (`EfCoreServiceDecorator`) reaches into `EF1001` internal
  surface whose constructor signatures and service-registration order
  are not stability-promised across majors.
- D-009 (patch-range pinning) caps the EF Core dependency at
  `[10.0.4, 11.0.0)`. The next major requires a deliberate ADR-driven
  widening or a parallel-target branch.
- The AOT and trim suppressions in the provider are validated against
  the .NET 10 analyzer set; .NET 11's trim analyzer may surface new
  diagnostics.
- The specification-test subclasses (D-011) target the
  `EntityFrameworkCore.Relational.Specification.Tests` package shipped
  with EF Core 10; an EF Core 11 jump requires re-running the suite
  against the EF Core 11 fixtures and triaging any new failures.

The premortem ranked the EF Core 11 / .NET 11 jump as the highest
medium-term risk to the provider's continuity.

## Decision

Adopt a trigger-driven multi-target strategy rather than a default
multi-target. The v1.0 line targets `net10.0` only. When the trigger
fires, a long-lived branch `next/ef-core-11` enables
`<TargetFrameworks>net10.0;net11.0</TargetFrameworks>` across the
solution and absorbs the work below.

### Trigger predicate (any of)

- Microsoft publishes EF Core 11 as a GA release on the NuGet
  registry (not a preview tag, not an RC tag).
- A v1.0 consumer (recorded via issue tracker) requires .NET 11
  consumption for a documented reason; the trigger fires at the first
  such request.
- The .NET 10 LTS-window end date enters the 18-month notice horizon.

### Branch-work scope when the trigger fires

1. **EF1001-surface revalidation.** Run the `EfCoreServiceDecorator`
   against the EF Core 11 internal-service registration. Capture
   constructor-signature deltas; fail loudly via the helper's
   diagnostic rather than silently degrading.
2. **`EngineProfile`-table extension.** Add lookup rows for MySQL 9.x
   and MariaDB 12 (assuming both ship within the EF Core 11 window).
   The static table append is the lightest-touch path; consumers
   downgrade gracefully when the runtime engine version does not
   match a registered row.
3. **AOT / trim revalidation.** Run the provider's full publish-AOT
   smoke against .NET 11 analyzers; resolve any new diagnostics by
   refactor rather than suppression per the workaround-discipline
   rule.
4. **Specification-test re-run.** Subclass the EF Core 11 specification
   fixtures; triage new failures per D-011's discipline.
5. **Patch-range widening.** Update `Directory.Packages.props` to
   `[11.0.0, 12.0.0)` (or the equivalent EF Core 11 baseline) and
   re-anchor the `EfCorePatchMatrixCI` to the new minor.
6. **Source-compatibility tail-plan.** Document for consumers what
   changes between provider-on-net10 and provider-on-net11 in a
   dedicated migration section of the changelog.

### Branch merge strategy

The branch lives in parallel until both target frameworks are green
across the full test matrix. At merge time the provider's release line
forks: the existing v1.0.x line continues on net10.0; a new v2.0.0
release ships the multi-target. Consumers on net10.0 stay on v1.0.x
indefinitely; consumers on net11.0 (or who consume both) adopt v2.0.

## Consequences

### Positive

- The v1.0 line stays focused; the EF Core 11 work does not destabilize
  v1.0 releases.
- The trigger predicate makes the decision data-driven rather than
  calendar-driven; the provider does not chase preview tags.
- The branch-work scope is fully enumerated in advance, so the actual
  branch work is execution rather than planning.

### Negative

- The provider carries two release lines during the overlap window.
  Backports for v1.0.x patches need explicit forward-port discipline.
- The trigger predicate is conservative; an early-adopter consumer on
  EF Core 11 preview does not get a supported path. The premortem
  accepts this trade-off as the cost of v1.0 stability.

### Neutral

- The `next/ef-core-11` branch is a planning artifact today; it does
  not exist in the repository until the trigger fires.

## Re-evaluation triggers

- EF Core 11 RC ships and the public API surface deltas land before
  the GA trigger fires; the branch-work scope may need to extend to
  cover an API change not yet anticipated.
- Microsoft announces a deferral of EF Core 11 beyond the .NET 11 LTS
  window; the trigger reverts to a calendar-driven shape.
- A different MySQL/MariaDB-protocol fork (Aurora, TiDB, Vitess)
  ships a release that requires EF Core 11 features; the trigger
  fires on consumer demand rather than upstream GA.

## Alternatives considered

- **EF Core 11 in a v1.1 minor release.** Rejected: the EF Core major
  jump touches EF1001 internals (D-001 service decorator), the public
  surface (PublicApiAnalyzers per D-008), and the AOT-suppression set.
  v1.1 cannot ship those changes without breaking SemVer for v1.0
  consumers.
- **Multi-target from v1.0.** Rejected: doubles CI cost and forces v1.0
  to absorb EF Core 11 preview churn during stabilization. v1.0's
  job is to ship a clean .NET 10 baseline.
- **Drop .NET 10 support when EF Core 11 ships.** Rejected: .NET 10 is
  an LTS release; consumers staying on .NET 10 deserve a continuing
  support line. The fork-at-trigger approach delivers both lines.
