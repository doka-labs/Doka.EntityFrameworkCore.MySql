# Character Sets and Collations

Demonstrates provider-specific model metadata for:

- model and table `utf8mb4` character sets
- the `InnoDB` storage engine
- binary and case-insensitive column collations
- prefix-length indexes on long text keys

The live assertions prove that `utf8mb4_bin` distinguishes `Alpha` from
`alpha`, while `utf8mb4_unicode_ci` matches `DOKA` with `doka`.

```bash
dotnet run --project examples/CharSetAndCollation/CharSetAndCollation.csproj
```

See [the shared example configuration](../README.md) for prerequisites.
