# JSON Columns

Demonstrates two complementary JSON paths:

- a native `JsonObject` property with deep round-trip comparison
- a JSON text property queried with `EF.Functions.JsonContains` and
  `EF.Functions.JsonDepth`

The same model runs against native MySQL JSON and the provider-managed MariaDB
JSON alias.

```bash
dotnet run --project examples/JsonColumns/JsonColumns.csproj
```

See [the shared example configuration](../README.md) for prerequisites.
