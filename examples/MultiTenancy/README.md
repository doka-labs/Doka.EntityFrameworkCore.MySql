# Multi-Tenancy

Demonstrates row-level tenant isolation with:

- a context-bound global query filter
- automatic tenant assignment for new rows
- a write guard that rejects cross-tenant changes
- a tenant-local unique key

The example writes the same external identifier for two tenants, then proves
that one tenant sees only its own row while an administrative unfiltered query
sees both.

```bash
dotnet run --project examples/MultiTenancy/MultiTenancy.csproj
```

The pattern is application-owned; the provider supplies the relational and
query infrastructure but does not infer tenant boundaries. See the
[shared example configuration](../README.md) for prerequisites.
