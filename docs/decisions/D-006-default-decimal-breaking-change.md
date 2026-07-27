---
id: D-006
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Default decimal type mapping and upgrade diagnostics"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-006 -- Use decimal(18,2) as the default mapping

## Context and Problem Statement

`MySqlTypeMappingSource` currently maps an unattributed CLR `decimal`
property to MySQL `decimal(65,30)`, which is the MySQL maximum precision
and scale. Two consequences in practice:

1. **Storage waste.** Every unattributed `decimal` column reserves the
   maximum row-storage footprint MySQL allows for a fixed-point number.
   Real applications almost never need 65 significant digits or 30
   fractional digits; the typical use case is currency (`(18,2)`),
   financial calculations (`(28,8)` for crypto), or accounting
   (`(19,4)`). `(65,30)` covers none of those better than `(18,2)` covers
   currency.
2. **Schema-drift surprise.** Existing schemata almost always declare
   their decimal columns with a tighter precision than `(65,30)`. A
   model-first project that omits `HasPrecision(...)` generates a
   migration that changes every decimal column to `(65,30)`. The
   migration runs against production data without warning and then has to
   be reverted manually.

The provider already emits a `MySqlEventId.ImplicitDecimalPrecisionDefaulted`
warning when the default applies; the warning is rarely observed in
practice because it is logged at `Warning` level once per column per
model build, easily lost in noisy startup logs.

## Decision Drivers

- The default should fit common financial values without excessive storage.
- Higher precision must remain explicitly configurable.
- A breaking default needs visible migration guidance.

## Considered Options

- Default to decimal(18,2)
- Keep decimal(65,30)
- Require explicit precision for every decimal

## Decision Outcome

Chosen option: "Default to decimal(18,2)", because decimal(18,2) is a practical default while explicit precision remains authoritative.

Change the default mapping for unattributed `decimal` to
`decimal(18,2)`. This is a deliberate breaking change at v1.0; the
warning continues to fire on first occurrence per `DbContext` so consumers
who genuinely need higher precision see a clear signal to add an explicit
`HasPrecision(p, s)`.

The change is announced in `CHANGELOG.md` as a breaking change with a
short migration recipe:

- For currency: no change required; `decimal(18,2)` already matches.
- For higher precision: add `[Precision(p, s)]` or
  `entity.Property(x => x.Amount).HasPrecision(p, s)`.
- For pre-existing schemata that already declare a higher precision: the
  `HasPrecision(...)` annotation is now mandatory; without it the next
  migration would attempt to narrow the column.

### Consequences

- Good, because common models get predictable storage and migration output.
- Bad, because some upgrades require an explicit precision annotation and data-range audit.

#### Positive

- Storage footprint of decimal columns shrinks by roughly the ratio of
  declared maximum precision to actual used precision for the typical
  consumer. The change is invisible to consumers who already annotated
  their properties.
- The "I forgot to annotate and now my migration wants to widen every
  column to `(65,30)`" surprise disappears.
- The default expresses the convention-over-configuration intent of the
  provider: the unattributed case is the common case, not the maximum
  case.

#### Negative

- It is a breaking change. Any consumer who genuinely relied on the
  `(65,30)` default (we have not found one in the wild, but the case is
  theoretically possible) needs to add an explicit annotation before
  upgrading to v1.0.
- The migration generated on the first run after upgrade narrows
  unannotated decimal columns from `(65,30)` to `(18,2)`. Existing data
  that exceeds `(18,2)` would fail the narrowing. The release notes flag
  the upgrade procedure: run a precision audit (`SELECT MAX(ABS(x))` per
  decimal column) before generating the post-upgrade migration.

#### Neutral

- The change is one-line in `MySqlTypeMappingSource`. The visible behavior
  change carries the weight.

### Confirmation

- Run default-decimal mapping and diagnostics tests.
- Verify the breaking-change migration guidance remains in the changelog.

## Pros and Cons of the Options

### Default to decimal(18,2)

- Good, because it matches common application and financial precision requirements.
- Bad, because existing consumers relying on wider implicit precision must opt in explicitly.

### Keep decimal(65,30)

- Good, because it preserves the widest engine-supported implicit range.
- Bad, because it over-allocates storage and hides domain precision decisions.

### Require explicit precision for every decimal

- Good, because every model would state its exact domain contract.
- Bad, because ordinary models become noisy and upgrades become unnecessarily disruptive.

## More Information

### Implementation Snapshot

- `DefaultDecimalPrecision = 18`, `DefaultDecimalScale = 2`; `s_decimalMapping` and `CreateDecimalStoreType` derive their store-type strings from the two constants. `MySqlModelValidator` warning message updated to surface the new default.

### Implementation Notes

- The default-decimal change lands as a single-line constant flip in `MySqlTypeMappingSource` plus a warning-message update in `MySqlModelValidator`. The `ImplicitDecimalPrecisionDefaulted` event continues to fire on the first unannotated decimal property per `DbContext` build so upgrading consumers see the new default before a migration is generated against it.
- The CHANGELOG ships the breaking-change entry under the v1.0 section with the migration recipe (no change required for currency / `(18,2)` consumers; add `HasPrecision(p, s)` or `[Precision(p, s)]` for higher precision; run a `SELECT MAX(ABS(x))` audit before generating the post-upgrade migration on existing schemata that had relied on the `(65,30)` default).
- The new mapping is verified by `MySqlTypeMappingBaselineTests.Default_decimal_resolves_to_18_2_when_unannotated` and the diagnostics-side `MySqlDiagnosticsTests` warning assertion was rewired to expect the new message text.

### Additional Alternative Rationale

- **Status quo (`decimal(65,30)` default).** Rejected: real-world cost
  documented above; the warning is too easy to miss.
- **Opt-in default via `DbContextOptionsBuilder` knob.** Rejected:
  convention-over-configuration violated. Anyone who needs to opt into a
  different default would just forget to do it for the next `DbContext`,
  and the dead-knob accumulates.
- **Multiple defaults keyed by `HasColumnType("money")` or similar.**
  Rejected: the provider does not own a MySQL `money` type; the right
  granularity is per-property annotation, not a fictitious column-type
  alias.

### Re-evaluation Triggers

- An operator report from the v1.0 beta cycle documents a real-world
  scenario where `(18,2)` is the wrong default; for example, a currency
  the provider serves predominantly that uses three fractional digits.
- A future EF Core change exposes default-decimal as a convention-bound
  knob that respects per-`DbContext` configuration; the default could
  then move from "type-mapping baked in" to "convention-configurable".
- A future MySQL release changes the on-disk layout for high-precision
  decimals in a way that makes the storage-waste argument weaker.
- Consumer evidence shows decimal(18,2) is not a safe majority default.
- EF Core introduces a provider-independent precision default contract.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Storage/MySqlTypeMappingSource.cs`
- `CHANGELOG.md`

### Sources

- No external sources; repository evidence only.
