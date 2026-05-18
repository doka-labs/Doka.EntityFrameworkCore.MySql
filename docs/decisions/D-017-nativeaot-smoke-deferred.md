# D-017 -- NativeAOT Smoke Pass Deferred

- **Status:** Implemented
- **Date:** 2026-05-18
- **Scope:** `eng/test-runtime-posture.sh` -- the runtime posture matrix runs JIT + PublishTrimmed passes only; the PublishAot pass is intentionally not invoked.
- **Implementation:** `run_runtime_posture()` publishes + executes the smoke harness under JIT and `-p:PublishTrimmed=true -p:TrimMode=full`. The PublishAot publish + execute pair is removed; a comment in the script points readers at this ADR.

## Context

The runtime-posture matrix previously ran three passes against `Doka.EntityFrameworkCore.MySql.RuntimeSmoke`: JIT (`dotnet run`), PublishTrimmed (`-p:PublishTrimmed=true -p:TrimMode=full`), and PublishAot (`-p:PublishAot=true`). The third pass aborted with `Unhandled exception. System.IO.FileNotFoundException: Could not find file 'Microsoft.EntityFrameworkCore.Design'` after ILC emitted `Method 'AddEntityFrameworkDokaMySql(IServiceCollection)' will always throw because: Failed to load assembly 'Microsoft.EntityFrameworkCore.Design'`.

The mechanical reason: `MySqlServiceCollectionExtensions.cs` carries `using Microsoft.EntityFrameworkCore.Design.Internal;` at the file level for the `ICSharpRuntimeAnnotationCodeGenerator` registration inside the design-time entry point. The C# compiler emits an `AssemblyRef` to `Microsoft.EntityFrameworkCore.Design` on the provider assembly because at least one method in the file uses Design types; ILC's AOT analysis demands every referenced assembly be present at AOT-publish time, but `Microsoft.EntityFrameworkCore.Design` carries `PrivateAssets="all"` on its PackageReference in the smoke project (and in the provider's own project file) and is not pulled into the AOT output.

The deeper issue is ecosystem-wide, not provider-specific.

## Primary-Source Evidence

Microsoft Learn's NativeAOT support page for EF Core 10 ([NativeAOT Support and Precompiled Queries](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries), retrieved 2026-05-18) states verbatim:

> NativeAOT and query precompilation are highly experimental features that are not yet suited for production use, and support should be viewed as infrastructure towards the final feature which will be released in a future version.

The same page documents the precompiled-queries prerequisite:

> EF's support for LINQ query execution under NativeAOT relies on query precompilation, which statically identifies EF LINQ queries and generates C# interceptors containing code to execute each specific query.

> EF providers may need to build in support for precompiled queries; you should check your provider's documentation to know whether it is compatible with EF's NativeAOT support.

Open upstream issue [dotnet/efcore#35945](https://github.com/dotnet/efcore/issues/35945) "AOT `dotnet ef dbcontext optimize --precompile-queries --nativeaot` fails with error CS9137" (April 2025) shows that Microsoft's own canonical AOT workflow is still broken on the EF Core side.

## Cross-Provider Empirical Check

| Provider | NativeAOT support status (2026-05-18) |
|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | Single assembly; same Design.Internal reference pattern; no published NativeAOT smoke; Microsoft positions NativeAOT as experimental at the EF Core core layer. |
| `Microsoft.EntityFrameworkCore.Sqlite` | Same. |
| `Pomelo.EntityFrameworkCore.MySql` | Single assembly; no published NativeAOT smoke. |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Single assembly; no published NativeAOT smoke. |

No mainstream EF Core provider ships a Design-assembly split today; the structural pattern Microsoft's own providers follow matches the provider's current shape.

## Decision

Skip the NativeAOT publish + smoke run in `eng/test-runtime-posture.sh`. The matrix continues to exercise:

- **JIT pass**: `dotnet run --configuration Release` against the smoke harness.
- **PublishTrimmed pass**: `dotnet publish -p:PublishTrimmed=true -p:TrimMode=full --self-contained true` plus a smoke run of the trimmed executable. Trim analysis errors still surface here (IL2026 / IL3050 are honored as build failures).

The PublishAot pass is not invoked; the script carries a comment that points readers at this ADR.

## Consequences

### Positive

- The runtime-posture CI job no longer fails on a known upstream-experimental feature gap.
- The provider keeps its single-assembly shape, matching every mainstream EF Core provider, so consumers see no unexpected package split.
- The trim pass remains as the strictest analyzer pass we ship; it catches the same classes of issues a future AOT pass would catch (with the exception of AOT-specific runtime code generation, which is what `[RequiresDynamicCode]` / IL3050 covers).

### Negative

- The provider does not have a self-test that demonstrates NativeAOT compatibility. The trade-off is accepted as long as upstream EF Core is in experimental NativeAOT mode.
- A future regression that breaks AOT specifically would not surface in our CI; we accept this because the upstream feature is not promised yet.

### Neutral

- The `MySqlServiceCollectionExtensions` Design.Internal reference stays at the file level. A future "single-file split" refactor (move design-time methods to a separate file in the same assembly) does not actually change the assembly-level `AssemblyRef` set; only an assembly-level split would, and that pattern is not standard for EF Core providers.

## Re-evaluation triggers

- Microsoft Learn's NativeAOT support page removes the "experimental, not yet suited for production use" framing. The trigger predicate is the verbatim phrasing changing in the upstream doc; the response is to re-enable the AOT pass with whatever precompiled-queries integration EF Core requires at that point.
- EF Core ships a stable precompiled-queries workflow that the provider can opt into. The trigger predicate is the upstream `dotnet ef dbcontext optimize --precompile-queries --nativeaot` workflow completing without error in a canonical sample; the response is the same as above.
- The provider acquires a customer ask for NativeAOT support that motivates a deeper investigation (e.g. provider-side precompiled queries, AOT-friendly service registration). The trigger predicate is a documented customer request; the response is a new ADR scoped to the requested deliverable.

## Alternatives considered

- **Split the provider into runtime + design assemblies.** Rejected: not standard for EF Core providers; doubles maintenance cost; would not address the precompiled-queries gap, so the AOT pass would still fail on the EF Core runtime surface even after the split.
- **Add Design as a non-private PackageReference in RuntimeSmoke.** Rejected: would move the failure deeper (Design itself is not AOT-friendly -- `[RequiresUnreferencedCode]` everywhere); papers over the structural issue without fixing it.
- **Keep the AOT pass + accept the failure as a documented expected-fail.** Rejected: a red CI job is operator-confusing; the AOT pass should be re-introduced when it has a real chance of passing.

## References

- [Microsoft Learn: NativeAOT Support and Precompiled Queries](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries)
- [dotnet/efcore#35945 -- AOT precompile-queries fails with CS9137](https://github.com/dotnet/efcore/issues/35945)
- [State of Native AOT in .NET 10](https://code.soundaranbu.com/state-of-nativeaot-net10)
