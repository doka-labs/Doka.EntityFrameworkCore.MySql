# D-003 -- HiLo-State Cache-Lifetime

- **Status:** Implemented
- **Date:** 2026-05-16
- **Scope:** `MySqlValueGeneratorSelector` + `MySqlSequenceValueGenerator` runtime
- **Implementation:** `src/Doka.EntityFrameworkCore.MySql/Internal/Metadata/MySqlHiLoStateCache.cs` (commit `bc5d4ea5d6a4`); block-claim correctness + connection-isolation follow-up in `MySqlSequenceHiLoValueGenerator.cs` + `MySqlValueGeneratorSelector.cs`.

## Implementation notes

- The original `MySqlSequenceHiLoValueGenerator.GetNewLowValue` invoked `MySqlSequenceValueGenerator.GetNextValue` with hard-coded `increment = 1` and returned the post-increment server value unchanged as the block LOW. The cache shared the state across contexts but the underlying server-side row only advanced by 1 per fetch, so consecutive block claims overlapped (block N = `[k..k+blockSize-1]`; block N+1 = `[k+1..k+blockSize]`). Live concurrency test `MySqlHiLoConcurrencyTests.HiLo_inserts_across_parallel_contexts_yield_unique_ids` surfaced as `Duplicate entry '2' for key 'PRIMARY'`. Fix: pass `blockSize` as the server-side increment so each claim advances the sequence by a full block, and compute the LOW client-side from the returned HIGH (`low = newValue - blockSize + 1`) for the emulation path; the native MariaDB path returns the LOW directly because the sequence DDL is created with `INCREMENT BY blockSize`.
- The generator's previous flow shared the `IRelationalConnection` instance with the surrounding `SaveChanges` operation. Under parallel-context load the underlying `MySqlConnection` could surface a "This MySqlConnection is already in use" error when EF Core's command pipeline and the HiLo sequence-claim tried to run on the same physical session concurrently. Fix: open a dedicated short-lived `MySqlConnection` per block claim using the connection string from the active `IRelationalConnection`. Cost is one extra connection-pool rental per block-exhaustion (rare under typical blockSize=10 usage); benefit is structural isolation from EF Core's connection-state machinery, removing a class of latent races. Sequence-claim is a session-independent operation -- the dedicated-connection shape matches the SqlServer provider's HiLo pattern.
- Native MariaDB sequences must be created with `INCREMENT BY blockSize` for the LOW-direct return path to stay correct; the migration generator's `CreateSequenceOperation` translation must propagate the `HasHiLoSequence` block size into the DDL. The current `MySqlMigrationsSqlGenerator` honors the `IncrementBy` annotation; operators using `UseHiLo("name", blockSize: 10)` get a sequence with `INCREMENT BY 10` automatically.

## Context

The previous `MySqlValueGeneratorSelector` instantiated a fresh
`HiLoValueGeneratorState` on every `FindForType(...)` lookup. The state object
holds the in-memory remainder of the currently-leased Hi/Lo block; recreating
it per resolve meant that the block-caching effect EF Core was designed to
deliver never landed in the running process. 100 inserts cost 100 round-trips
to the sequence row instead of `ceil(100 / blockSize)`.

The pre-plan probe additionally surfaced that `MySqlSequenceValueGeneratorFactory`
was a dead-knob (zero fan-in, never wired through DI). The class existed but
no part of the provider actually used it.

## Decision

Introduce a static, process-wide `ConcurrentDictionary` keyed by
`(sequenceName, blockSize)` that hands back the same
`HiLoValueGeneratorState` instance for every resolve that targets the same
sequence:

```csharp
private static readonly ConcurrentDictionary<
    (string SequenceName, int BlockSize),
    HiLoValueGeneratorState> s_states = new();

public static HiLoValueGeneratorState GetOrCreate(
    string sequenceName,
    int blockSize)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
    return s_states.GetOrAdd(
        (sequenceName, blockSize),
        static key => new HiLoValueGeneratorState(key.BlockSize));
}
```

The dead `MySqlSequenceValueGeneratorFactory` was folded into a single live
`MySqlSequenceValueGenerator` file. A `ResetForTesting()` entrypoint clears
the cache between integration test runs (exposed via `InternalsVisibleTo`).

## Consequences

### Positive

- Hi/Lo bulk-insert throughput improves by roughly an order of magnitude
  because the in-process block is now reused across `DbContext` lifetimes.
- The cache key is bound to the sequence name (and its server-side DDL), not
  to a per-context resolve, so the cache survives `DbContext` disposal without
  leaking provider-internal state into application code.
- Removing the dead factory clears a real source of reviewer confusion -- the
  one consumer that did exist now lives in a file whose name matches its
  class.

### Negative

- The cache is process-wide. Two separate logical applications running inside
  the same process and pointing at the same sequence name but at different
  physical databases would share a block. The pre-plan probe found no real
  consumer in that shape, and the cost is documented here so an operator can
  decide whether a wider-keyed variant is needed later.
- Concurrent inserts across `Parallel.ForEachAsync` workloads now exercise the
  same in-memory state object. The atomic-increment pattern inside
  `HiLoValueGeneratorState` handles this correctly; the live concurrency test
  `MySqlHiLoConcurrencyTests` pins the absence of duplicate ids at 10
  concurrent contexts x 25 inserts each.

### Neutral

- The benchmark suite (`HiLoBenchmarks`) tracks both the cache-hit cost and
  the round-trip-amortized bulk-insert path, so regressions surface in the
  durable scorecard rather than only on operator reports.

## Re-evaluation triggers

- An operator-reported scenario where two logically separate applications
  share a process and need distinct Hi/Lo blocks against the same sequence
  name; the cache key would gain a database-identity component.
- A future EF Core change that makes `HiLoValueGeneratorState` itself
  process-shared upstream; the cache then becomes redundant and can be
  retired in favor of the upstream behavior.
- A future Hi/Lo strategy that needs per-`DbContext` state (for example,
  a tenant-scoped block-size override); the current cache key would need to
  grow a tenant component and the dead-knob risk would resurface.

## Alternatives considered

- **Per-`DbContext` cache.** Rejected: block sharing across contexts is the
  whole point of this change. A per-context cache only deduplicates inserts
  inside one context lifetime, which barely helps the typical short-lived
  request/`DbContext` pairing.
- **Drop the factory entirely without a cache.** Rejected: that would be a
  functional regression. `HiLo` is the EF-Core-standard value-generation
  strategy and consumers explicitly opt into it via `UseHiLo(...)`.
- **Cache key on `(sequenceName)` only, ignoring `blockSize`.** Rejected:
  two different consumers could reasonably configure the same sequence with
  different block sizes, and silently coercing both to the first observed
  block size would be a latent bug.
