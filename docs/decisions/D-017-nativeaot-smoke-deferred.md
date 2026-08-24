---
id: D-017
status: implemented
date: 2026-05-18
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Runtime posture and NativeAOT release gate"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-017 -- Defer NativeAOT until the EF Core path is supportable

## Context and Problem Statement

The runtime-posture matrix previously ran three passes against `Doka.EntityFrameworkCore.MySql.RuntimeSmoke`: JIT (`dotnet run`), PublishTrimmed (`-p:PublishTrimmed=true -p:TrimMode=full`), and PublishAot (`-p:PublishAot=true`). The third pass aborted with `Unhandled exception. System.IO.FileNotFoundException: Could not find file 'Microsoft.EntityFrameworkCore.Design'` after ILC emitted `Method 'AddEntityFrameworkDokaMySql(IServiceCollection)' will always throw because: Failed to load assembly 'Microsoft.EntityFrameworkCore.Design'`.

The mechanical reason: `MySqlServiceCollectionExtensions.cs` carries `using Microsoft.EntityFrameworkCore.Design.Internal;` at the file level for the `ICSharpRuntimeAnnotationCodeGenerator` registration inside the design-time entry point. The C# compiler emits an `AssemblyRef` to `Microsoft.EntityFrameworkCore.Design` on the provider assembly because at least one method in the file uses Design types; ILC's AOT analysis demands every referenced assembly be present at AOT-publish time, but `Microsoft.EntityFrameworkCore.Design` carries `PrivateAssets="all"` on its PackageReference in the smoke project (and in the provider's own project file) and is not pulled into the AOT output.

The deeper issue is ecosystem-wide, not provider-specific.

## Decision Drivers

- Runtime posture gates must represent supported EF Core behavior.
- A release gate must not fail on an upstream experimental path by design.
- Deferral needs primary evidence and a concrete trigger.

## Considered Options

- Gate JIT and trimming now, trigger NativeAOT later
- Force NativeAOT into the current release gate
- Remove runtime posture validation

## Decision Outcome

Chosen option: "Gate JIT and trimming now, trigger NativeAOT later", because strict supported-path gates plus a trigger are more honest than a knowingly impossible green gate.

Skip the NativeAOT publish + smoke run in `eng/test-runtime-posture.sh`. The matrix continues to exercise:

- **JIT pass**: `dotnet run --configuration Release` against the smoke harness.
- **PublishTrimmed pass**: `dotnet publish -p:PublishTrimmed=true -p:TrimMode=full --self-contained true` plus a smoke run of the trimmed executable. Trim analysis errors still surface here (IL2026 / IL3050 are honored as build failures).

The runtime-smoke project also enables the trim analyzer during an ordinary
Release build. Generic service registration preserves public constructors for
the handler implementation, matching the
[`ServiceDescriptor` factory contract][dotnet-service-descriptor]. The
migration-context construction path is explicitly marked as requiring
unreferenced and dynamic code because this smoke intentionally exercises EF
Core's runtime migrations service graph; the compiled-model basic and spatial
paths remain the supported trimmed execution proof. The
[.NET trim-warning guidance][dotnet-trim-warnings] defines the annotation and
call-site analysis applied by this gate.

The PublishAot pass is not invoked; the script carries a comment that points readers at this ADR.

### Consequences

- Good, because JIT and full trimming remain release blockers with explicit evidence.
- Bad, because NativeAOT compatibility remains unclaimed until the trigger is satisfied.

#### Positive

- The runtime-posture CI job no longer fails on a known upstream-experimental feature gap.
- The provider keeps its single-assembly shape, matching every mainstream EF Core provider, so consumers see no unexpected package split.
- The trim pass remains as the strictest analyzer pass we ship; it catches the same classes of issues a future AOT pass would catch (with the exception of AOT-specific runtime code generation, which is what `[RequiresDynamicCode]` / IL3050 covers).

#### Negative

- The provider does not have a self-test that demonstrates NativeAOT compatibility. The trade-off is accepted as long as upstream EF Core is in experimental NativeAOT mode.
- A future regression that breaks AOT specifically would not surface in our CI; we accept this because the upstream feature is not promised yet.

#### Neutral

- The `MySqlServiceCollectionExtensions` Design.Internal reference stays at the file level. A future "single-file split" refactor (move design-time methods to a separate file in the same assembly) does not actually change the assembly-level `AssemblyRef` set; only an assembly-level split would, and that pattern is not standard for EF Core providers.

### Confirmation

- Run `eng/test-runtime-posture.sh` for JIT and PublishTrimmed.
- Re-probe the official EF Core NativeAOT workflow when its support status
  changes.

## Pros and Cons of the Options

### Gate JIT and trimming now, trigger NativeAOT later

- Good, because supported runtime paths remain strict while NativeAOT has an explicit re-entry condition.
- Bad, because the release does not claim NativeAOT compatibility.

### Force NativeAOT into the current release gate

- Good, because the provider would discover AOT failures continuously.
- Bad, because the gate would remain permanently red on an upstream unsupported path.

### Remove runtime posture validation

- Good, because the repository avoids AOT-specific branching.
- Bad, because JIT and trimming regressions would also escape release validation.

## More Information

### Implementation Snapshot

- `run_runtime_posture()` publishes + executes the smoke harness under JIT and `-p:PublishTrimmed=true -p:TrimMode=full`. The PublishAot publish + execute pair is removed; a comment in the script points readers at this ADR.
- `Doka.EntityFrameworkCore.MySql.RuntimeSmoke.csproj` enables the trim analyzer
  for ordinary Release builds, so new call-site warnings fail before the
  hosted full-trim stage.

### Primary-Source Evidence

The official [EF Core NativeAOT documentation][efcore-nativeaot], retrieved on
2026-08-24, still classifies NativeAOT and query precompilation as highly
experimental and unsuitable for production. It explains that NativeAOT query
execution depends on statically discovered LINQ queries and generated C#
interceptors, and that each provider must document whether it supports that
workflow.

### Cross-Provider Empirical Check

| Provider | NativeAOT support status (2026-05-18) |
|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | Single assembly; same Design.Internal reference pattern; no published NativeAOT smoke; Microsoft positions NativeAOT as experimental at the EF Core core layer. |
| `Microsoft.EntityFrameworkCore.Sqlite` | Same. |
| `Pomelo.EntityFrameworkCore.MySql` | Single assembly; no published NativeAOT smoke. |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Single assembly; no published NativeAOT smoke. |

No mainstream EF Core provider ships a Design-assembly split today; the structural pattern Microsoft's own providers follow matches the provider's current shape.

### Additional Alternative Rationale

- **Split the provider into runtime + design assemblies.** Rejected: not standard for EF Core providers; doubles maintenance cost; would not address the precompiled-queries gap, so the AOT pass would still fail on the EF Core runtime surface even after the split.
- **Add Design as a non-private PackageReference in RuntimeSmoke.** Rejected: would move the failure deeper (Design itself is not AOT-friendly -- `[RequiresUnreferencedCode]` everywhere); papers over the structural issue without fixing it.
- **Keep the AOT pass + accept the failure as a documented expected-fail.** Rejected: a red CI job is operator-confusing; the AOT pass should be re-introduced when it has a real chance of passing.

### Re-evaluation Triggers

- Microsoft Learn's NativeAOT support page removes the "experimental, not yet suited for production use" framing. The trigger predicate is the verbatim phrasing changing in the upstream doc; the response is to re-enable the AOT pass with whatever precompiled-queries integration EF Core requires at that point.
- EF Core ships a stable precompiled-queries workflow that the provider can opt into. The trigger predicate is the upstream `dotnet ef dbcontext optimize --precompile-queries --nativeaot` workflow completing without error in a canonical sample; the response is the same as above.
- The provider acquires a customer ask for NativeAOT support that motivates a deeper investigation (e.g. provider-side precompiled queries, AOT-friendly service registration). The trigger predicate is a documented customer request; the response is a new ADR scoped to the requested deliverable.
- EF Core documents NativeAOT as supported for the provider workflow.

### Decision History

- 2026-05-18: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.
- 2026-08-16: Enabled trim analysis in the ordinary runtime-smoke build and
  bound migration-handler construction and runtime migrations to their exact
  trimming contracts after release qualification exposed IL2091 and IL2026.

### Implementation References

- `eng/test-runtime-posture.sh`
- `tests/Doka.EntityFrameworkCore.MySql.RuntimeSmoke/`

### Sources

- [EF Core NativeAOT and precompiled queries][efcore-nativeaot]
  (primary source; retrieved 2026-08-24)
- [.NET trim warnings][dotnet-trim-warnings]
  (primary source; retrieved 2026-08-24)
- [.NET dependency injection service descriptors][dotnet-service-descriptor]
  (primary source; retrieved 2026-08-24)

[efcore-nativeaot]:
  https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries
[dotnet-trim-warnings]:
  https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings
[dotnet-service-descriptor]:
  https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.DependencyInjection.Abstractions/src/ServiceDescriptor.cs
