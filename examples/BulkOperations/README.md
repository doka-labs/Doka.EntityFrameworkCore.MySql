# Bulk Operations

Demonstrates the provider's set-based and batched write paths without relying
on a third-party bulk extension.

The example:

- inserts 100 entities through `SaveChangesAsync` with `MaxBatchSize(25)`
- updates 60 rows with `ExecuteUpdateAsync`
- deletes the updated rows with `ExecuteDeleteAsync`
- verifies the affected-row and remaining-row invariants

Run from the repository root after starting a supported target:

```bash
dotnet run --project examples/BulkOperations/BulkOperations.csproj
```

See [the shared example configuration](../README.md) for engine selection and
connection-string overrides.
