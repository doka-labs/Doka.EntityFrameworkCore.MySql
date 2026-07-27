---
id: D-001
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Runtime and design-time EF Core service composition"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-001 -- Centralize EF Core service decoration

## Context and Problem Statement

`MySqlServiceCollectionExtensions.AddEntityFrameworkDokaMySql` and
`AddEntityFrameworkDokaMySqlDesignTime` wrap two EF Core internal services so
they can layer MySQL-specific behavior:

- `IMigrationsModelDiffer` is wrapped with `MySqlMigrationsModelDiffer`
  (additional spatial-index handling and CharSet annotation diffing).
- `IModelCodeGenerator` is wrapped with `MySqlModelCodeGenerator` (scaffolding
  output that emits MySQL annotations).

The current implementation walks the `IServiceCollection` with
`LastOrDefault(d => d.ServiceType == typeof(IMigrationsModelDiffer))`, captures
the existing descriptor, and re-instantiates the inner service via
`ActivatorUtilities.CreateInstance`. The pattern is repeated inline in both
extension methods. The wrapped types are part of EF Core's `EF1001` internal
surface; Microsoft does not promise binary stability for them across patch
releases.

The premortem identified this coupling as the highest-probability medium-impact
regression path for the v1.0 release line: an EF Core 10.0.x patch that
introduces a new required constructor argument on either inner service would
silently leave the decorators inert because `ActivatorUtilities.CreateInstance`
would fall back to a no-arg instantiation that no longer carries the original
service graph.

## Decision Drivers

- Fail explicitly when an expected EF Core service is absent.
- Keep EF1001 coupling in one reviewable implementation boundary.
- Preserve runtime and design-time registration order.

## Considered Options

- Centralized service decorator
- Inline decoration at each registration site
- Replace provider services without preserving the inner service

## Decision Outcome

Chosen option: "Centralized service decorator", because the provider needs one fail-fast and testable boundary around unstable EF Core internals.

Consolidate the inline `LastOrDefault` + `ActivatorUtilities.CreateInstance`
pattern behind a single helper:

```csharp
internal static class EfCoreServiceDecorator
{
    public static void Decorate<TService, TDecorator>(
        IServiceCollection services,
        Func<TService, IServiceProvider, TService> factory)
        where TService : class
        where TDecorator : class, TService;
}
```

The helper:

- Captures the existing `ServiceDescriptor` for `TService`.
- Re-instantiates the inner service through `ActivatorUtilities.CreateInstance`
  *and* hard-fails with an actionable diagnostic when the inner constructor
  cannot be satisfied -- instead of silently returning a no-arg fallback.
- Carries the single `#pragma warning disable EF1001` for the entire decorator
  surface, so the pragma is not sprinkled across consumers.
- Is exercised by a `RuntimeSmoke` test that resolves the decorated service
  from `BuildServiceProvider()` and asserts the resolved instance is the Doka
  decorator type (and its inner reference is the EF Core default).

`MySqlServiceCollectionExtensions` then reduces to two `Decorate<...>(...)` calls.

### Consequences

- Good, because service composition and diagnostics stay consistent across runtime and design time.
- Bad, because major EF Core upgrades require explicit revalidation of the decorator boundary.

#### Positive

- Single point of EF1001 contact -- every patch-coupled call site lives in one
  helper that the EF-Core-Patch-Matrix-CI exercises explicitly.
- Hard-fail diagnostic on constructor mismatch replaces the silent no-op
  fallback that the current inline pattern degrades to.
- Reduces inline `#pragma` density and the per-call-site pragma audit cost.
- Pre-positions the decorator surface for the EF Core 11 / .NET 11 jump
  (see D-013): the validation that the wrap is still active becomes one test,
  not several.

#### Negative

- Adds one indirection between the registration call site and the actual
  `services.Replace(...)` invocation. Stack traces during DI resolution include
  the helper frame.
- The helper itself is `EF1001` surface -- a patch release can in principle
  invalidate the helper just as easily as the inline pattern. The
  `efcore-patch-matrix-ci` foundation (id=9) is the structural mitigation.

#### Neutral

- The helper is `internal` and exposed to tests only through
  `InternalsVisibleTo`; it is not part of the public API contract.

### Confirmation

- Run `eng/test.sh` and the design-time scaffolding integration tests.
- Build against the floor and latest supported EF Core patch matrix.

## Pros and Cons of the Options

### Centralized service decorator

- Good, because it gives all EF1001 service replacement one tested implementation path.
- Bad, because it remains coupled to EF Core internal registration details.

### Inline decoration at each registration site

- Good, because each call site can be changed independently.
- Bad, because registration behavior and failure handling would drift across services.

### Replace provider services without preserving the inner service

- Good, because the implementation would contain less composition code.
- Bad, because provider wrappers would lose required EF Core base behavior.

## More Information

### Implementation Snapshot

- `src/Doka.EntityFrameworkCore.MySql/Internal/Infrastructure/EfCoreServiceDecorator.cs`; design-time bootstrap fix in `MySqlServiceCollectionExtensions.AddEntityFrameworkDokaMySqlDesignTime`.

### Implementation Notes

- `EntityFrameworkRelationalDesignServicesBuilder.TryAddCoreServices` in EF Core 10.0.4 registers only `IAnnotationCodeGenerator` + `ICSharpRuntimeAnnotationCodeGenerator` + the relational annotation dependencies. The remaining design-time core (`IModelCodeGenerator`, `IModelCodeGeneratorSelector`, `ICompiledModelCodeGenerator`, `IMigrationsCodeGenerator`, `ICSharpHelper`, ...) lives in `IServiceCollection.AddEntityFrameworkDesignTimeServices`. The `dotnet ef` tooling pipeline calls that helper through `DesignTimeServicesBuilder` BEFORE invoking provider-specific design services; stand-alone consumers (integration tests, custom scaffolders that build the service collection themselves) skip the tooling path and previously caused `EfCoreServiceDecorator.Decorate<IModelCodeGenerator, MySqlModelCodeGenerator>` to fail with `No inner 'IModelCodeGenerator' registration was found to decorate`. Fix: `AddEntityFrameworkDokaMySqlDesignTime` now invokes `serviceCollection.AddEntityFrameworkDesignTimeServices()` before the decorator runs, making the entry point self-contained without breaking the `dotnet ef` flow (the inner `TryAddSingletonEnumerable` calls are idempotent).
- Live integration test `MySqlComprehensiveCoverageTests.Scaffolding_roundtrip_on_mysql84` + `Scaffolding_roundtrip_on_mariadb118` pin the fix: both build the service collection via `services.AddEntityFrameworkDokaMySqlDesignTime()` and resolve `IDatabaseModelFactory` + `IModelCodeGenerator` directly, which is the structural shape the previous setup could not satisfy.

### Additional Alternative Rationale

- **Status quo (inline `LastOrDefault` + `ActivatorUtilities.CreateInstance`
  in both extension methods).** Rejected: drift risk on each EF Core patch
  release; `#pragma` scoping spreads across call sites; tests for the wrap are
  decentralized.
- **Full re-implementation of `MigrationsModelDiffer` and `ModelCodeGenerator`
  without wrapping.** Rejected: ~600 LOC per service; the maintenance debt to
  track EF Core's internal changes outweighs the patch-coupling risk that the
  decorator helper closes structurally.
- **Reflection-based late binding without `ActivatorUtilities`.** Rejected:
  same `EF1001` surface, less informative diagnostics, slower cold path on the
  first resolve.

### Re-evaluation Triggers

- The EF-Core-Patch-Matrix-CI (Backbone 9) reports a build or runtime failure
  for either decorator on any tested patch version.
- A new EF Core internal service surfaces in the provider that needs the same
  wrap pattern, and either grows the helper API or warrants its own decorator
  variant.
- EF Core 11 changes the internal-service registration order or constructor
  signatures in a way that requires a fundamentally different wrap strategy.
  In that case this ADR is superseded by D-013.
- An EF Core patch or major changes the decorated service registration shape.
- A provider service can move from EF1001 internals to a stable public extension point.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Infrastructure/EfCoreServiceDecorator.cs`
- `src/Doka.EntityFrameworkCore.MySql/Extensions/MySqlServiceCollectionExtensions.cs`

### Sources

- No external sources; repository evidence only.
