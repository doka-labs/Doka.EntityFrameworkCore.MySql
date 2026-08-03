# Inheritance Patterns

Demonstrates table-per-hierarchy inheritance with a discriminator and an owned
address mapped into its owner's table.

```bash
dotnet run --project examples/InheritancePatterns/InheritancePatterns.csproj
```

The example creates an isolated schema, verifies polymorphic and owned-entity
queries, and removes the schema after a successful run.
