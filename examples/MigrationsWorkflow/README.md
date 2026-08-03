# Migrations Workflow

Demonstrates the operational migration lifecycle used by the repository:
script generation, bundle generation, apply, verification, and rollback-aware
execution.

Show the supported commands:

```bash
dotnet run --project examples/MigrationsWorkflow/MigrationsWorkflow.csproj -- --help
```

The workflow is also exercised by `eng/test-migration-deployment.sh` across the
supported engine matrix before a release candidate can complete.
