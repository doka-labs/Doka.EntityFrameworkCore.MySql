# D-001 — EF-Core-Service-Decorator-Coupling

- **Status:** Accepted — implementation planned in Welle 1 / PR 1.5 (workflow phase id=14)
- **Date:** 2026-05-16
- **Scope:** `MySqlServiceCollectionExtensions` runtime + design-time service composition
- **Related foundation:** `efcore-service-decorator` (foundation id=3)

## Context

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

## Decision

Consolidate the inline `LastOrDefault` + `ActivatorUtilities.CreateInstance`
pattern behind a single helper:

```csharp
internal static class EFCoreServiceDecorator
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
  cannot be satisfied — instead of silently returning a no-arg fallback.
- Carries the single `#pragma warning disable EF1001` for the entire decorator
  surface, so the pragma is not sprinkled across consumers.
- Is exercised by a `RuntimeSmoke` test that resolves the decorated service
  from `BuildServiceProvider()` and asserts the resolved instance is the Doka
  decorator type (and its inner reference is the EF Core default).

`MySqlServiceCollectionExtensions` then reduces to two `Decorate<…>(…)` calls.

## Consequences

### Positive

- Single point of EF1001 contact — every patch-coupled call site lives in one
  helper that the EF-Core-Patch-Matrix-CI exercises explicitly.
- Hard-fail diagnostic on constructor mismatch replaces the silent no-op
  fallback that the current inline pattern degrades to.
- Reduces inline `#pragma` density and the per-call-site pragma audit cost.
- Pre-positions the decorator surface for the EF Core 11 / .NET 11 jump
  (see D-013): the validation that the wrap is still active becomes one test,
  not several.

### Negative

- Adds one indirection between the registration call site and the actual
  `services.Replace(...)` invocation. Stack traces during DI resolution include
  the helper frame.
- The helper itself is `EF1001` surface — a patch release can in principle
  invalidate the helper just as easily as the inline pattern. The
  `efcore-patch-matrix-ci` foundation (id=9) is the structural mitigation.

### Neutral

- The helper is `internal` and exposed to tests only through
  `InternalsVisibleTo`; it is not part of the public API contract.

## Re-evaluation triggers

- The EF-Core-Patch-Matrix-CI (Backbone 9) reports a build or runtime failure
  for either decorator on any tested patch version.
- A new EF Core internal service surfaces in the provider that needs the same
  wrap pattern, and either grows the helper API or warrants its own decorator
  variant.
- EF Core 11 changes the internal-service registration order or constructor
  signatures in a way that requires a fundamentally different wrap strategy.
  In that case this ADR is superseded by D-013.

## Alternatives considered

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
