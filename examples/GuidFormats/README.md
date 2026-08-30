# GUID Formats

Demonstrates a new-schema `binary(16)` GUID key alongside a legacy `char(36)`
GUID property. Client-side GUID generation is explicit on the key, and the
example verifies both values after a database round-trip.

```bash
dotnet run --project examples/GuidFormats/GuidFormats.csproj
```

Use `Char36` only for an existing textual schema contract; `Binary16` remains
the provider default. See [the shared example configuration](../README.md) for
engine selection.

Doka uses `GuidFormat=Binary16` as the connector transport for both model
formats. The provider normalizes ordinary connection strings itself. Advanced
callers that supply a `DbConnection` or `MySqlDataSource` must configure that
transport value explicitly; Doka validates the borrowed object without using
the connector option as a second column-format setting.
