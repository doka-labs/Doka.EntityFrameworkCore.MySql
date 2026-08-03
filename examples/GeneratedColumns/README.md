# Generated Columns

Demonstrates both generated-column storage modes:

- stored `LOWER(Name)` materialization
- virtual `CHAR_LENGTH(Name)` evaluation

The example inserts one row, reloads the server-generated values, and fails if
either generated value differs from the expected result.

```bash
dotnet run --project examples/GeneratedColumns/GeneratedColumns.csproj
```

See [the shared example configuration](../README.md) for prerequisites.
