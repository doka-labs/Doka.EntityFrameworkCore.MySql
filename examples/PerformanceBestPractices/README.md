# Performance Best Practices

Demonstrates provider usage for a stable hot read path:

- bounded `SaveChanges` batching
- indexed predicates
- `AsNoTracking` for read-only work
- projection instead of full-entity materialization
- a compiled async query for a repeatedly executed query shape

The example verifies both the result size and the absence of tracked entities.
Compiled queries should be reserved for measured hot paths, not applied to
every query by default.

```bash
dotnet run --project examples/PerformanceBestPractices/PerformanceBestPractices.csproj
```

See [the shared example configuration](../README.md) for prerequisites.
