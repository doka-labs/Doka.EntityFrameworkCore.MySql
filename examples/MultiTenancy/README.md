# Multi-Tenancy

Demonstrates row-level tenant isolation with:

- a context-bound global query filter
- automatic tenant assignment for new rows
- one write guard shared by every synchronous and asynchronous save overload
- a tenant-local unique key

The example writes the same external identifiers for two tenants through all
four EF Core save overloads. It proves that one tenant sees only its own rows,
that an administrative unfiltered query sees all rows, and that every overload
rejects an explicit cross-tenant write.

```bash
dotnet run --project examples/MultiTenancy/MultiTenancy.csproj
```

The pattern is application-owned; the provider supplies the relational and
query infrastructure but does not infer tenant boundaries. See the
[shared example configuration](../README.md) for prerequisites.
