# Migrations Workflow

Demonstrates the operational migration lifecycle used by the repository:
script generation, bundle generation, apply, verification, and rollback-aware
execution.

The checked-in migration chain proves three distinct states: initial schema
creation, adding temporal defaults to a populated table, and changing those
defaults without rewriting existing values. Runtime readback verifies the exact
migration history and the values produced before and after the default change.

Show the supported commands:

```bash
dotnet run --project examples/MigrationsWorkflow/MigrationsWorkflow.csproj -- --help
```

The workflow is also exercised by `eng/test-migration-deployment.sh` across the
supported engine matrix before a release candidate can complete.
