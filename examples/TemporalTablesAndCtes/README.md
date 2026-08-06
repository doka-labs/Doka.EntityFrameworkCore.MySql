# Temporal Tables and CTEs

Demonstrates the portable system-versioned temporal contract and composable
common table expressions on every supported engine:

- one `IsTemporal` model works with native MariaDB versioning and the
  provider's transactional MySQL history-table emulation
- `TemporalAll` returns repeated historical keys without tracking them
- `TemporalAsOf` selects the version current at a UTC instant
- a recursive `WITH RECURSIVE` query binds values as parameters and composes
  with LINQ on the server

The short delays separate the example's history versions at microsecond
precision. Application code does not need delays; it should use the UTC
boundaries produced by its actual writes.

```bash
dotnet run --project examples/TemporalTablesAndCtes/TemporalTablesAndCtes.csproj
```

See [the shared example configuration](../README.md) for prerequisites and
engine selection. See [Temporal tables](../../docs/temporal-tables.md) and
[Common table expressions](../../docs/ctes.md) for the complete support and
schema-safety contracts.
