# Character Sets and Collations

Demonstrates the application defaults introduced in Doka 10.7.0 on .NET 10 /
EF Core 10 and carried forward to .NET 11 / EF Core 11, together with explicit
provider metadata:

- `DefaultCharSet("utf8mb4")` and `DefaultCollation("utf8mb4_unicode_ci")`
  configured once through `UseMySql`
- the `InnoDB` storage engine
- binary and case-insensitive column collations
- prefix-length indexes on long text keys

The example checks both canonical model defaults and actual native column
charset/collation facts. The unconfigured `FallbackText` property inherits
`utf8mb4_unicode_ci`: querying `alpha` matches both `Alpha` and `alpha`.
Explicit column overrides remain effective: binary `Title` matches only one
`alpha` and no `ALPHA`, while case-insensitive `SearchCode` matches `DOKA`
with `doka`. The assertions do not rely on the server's inherited default.

These options configure model/schema fallbacks. They do not change connection
encoding or convert existing text columns. See
[application text defaults](../../docs/default-text-options.md) for migration
and override boundaries.

```bash
dotnet run --project examples/CharSetAndCollation/CharSetAndCollation.csproj
```

See [the shared example configuration](../README.md) for prerequisites.
