namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static class ExampleContractValidator
{
    private static readonly ExampleContract[] s_contracts =
    [
        new("BulkOperations", "BulkOperations.csproj",
            ["MaxBatchSize", "ExecuteUpdateAsync", "ExecuteDeleteAsync"], true),
        new("CharSetAndCollation", "CharSetAndCollation.csproj",
            ["HasCharSet", "UseStorageEngine", "UseCollation", "HasPrefixLength"], true),
        new("CrudOperations", "CrudOperations.csproj",
            ["EnsureCreated", "SaveChanges", "EnsureDeleted"], false),
        new("DockerIntegration", "DockerIntegration.csproj",
            ["CanConnectAsync", "SELECT VERSION()", "SaveChangesAsync"], true),
        new("Doka.EntityFrameworkCore.MySql.HostExamples",
            "Doka.EntityFrameworkCore.MySql.HostExamples.csproj",
            ["AddOpenTelemetry", "AddSerilog", "HasMySqlGuidFormat"], false),
        new("GeneratedColumns", "GeneratedColumns.csproj",
            ["HasComputedColumnSql", "stored: true", "stored: false"], true),
        new("GettingStarted", "GettingStarted.csproj",
            ["UseMySql", "EnsureCreated", "SaveChanges"], false),
        new("GuidFormats", "GuidFormats.csproj",
            ["Binary16", "Char36", "UseMySqlClientGuidValueGeneration"], true),
        new("InheritancePatterns", "InheritancePatterns.csproj",
            ["HasDiscriminator", "OwnsOne", "OfType<Dog>"], false),
        new("JsonColumns", "JsonColumns.csproj",
            ["JsonObject", "JsonContains", "JsonDepth", "DeepEquals"], true),
        new("MigrationsWorkflow", "MigrationsWorkflow.csproj",
            ["MigrationWorkflowCommand", "MigrationWorkflowContext", "MigrationWorkflowPauseInterceptor"], false),
        new("MultiTenancy", "MultiTenancy.csproj",
            [
                "HasQueryFilter",
                "IgnoreQueryFilters",
                "EnforceTenantOwnership",
                "public override int SaveChanges(",
                "public override Task<int> SaveChangesAsync(",
                "AssertMismatchedTenantRejected",
            ], true),
        new("PerformanceBestPractices", "PerformanceBestPractices.csproj",
            ["MaxBatchSize", "AsNoTracking", "CompileAsyncQuery"], true),
        new("Relationships", "Relationships.csproj",
            ["Include", "HasMany", "WithMany"], false),
        new("RetryAndResilience", "RetryAndResilience.csproj",
            ["EnableRetryOnFailure", "maxRetryCount", "maxRetryDelay"], false),
        new("SpatialQueries", "SpatialQueries.csproj",
            ["UseNetTopologySuite", "HasSrid", "IsSpatial", "DistanceSphere"], true),
        new("TemporalTablesAndCtes", "TemporalTablesAndCtes.csproj",
            ["IsTemporal", "TemporalAll", "TemporalAsOf", "FromSql", "WITH RECURSIVE"], true),
    ];

    private static readonly string[] s_placeholderMarkers =
    [
        "see README.md for usage instructions",
        "This example demonstrates DockerIntegration patterns",
        "This example demonstrates GeneratedColumns patterns",
        "This example demonstrates JsonColumns patterns",
    ];

    private static readonly string[] s_invariantTokens =
    [
        "ExampleDatabaseConfiguration.Create",
        "EnsureDeletedAsync",
        "EnsureCreatedAsync",
        "InvalidOperationException",
    ];

    public static ExampleValidationResult Validate(
        string repositoryRoot
    )
    {
        var errors = new List<ContractError>();
        var examplesRoot = Path.Combine(repositoryRoot, "examples");
        var expected = s_contracts.Select(static contract => contract.Directory).ToHashSet(StringComparer.Ordinal);
        var actual = Directory.Exists(examplesRoot)
            ? Directory.GetFiles(examplesRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(path => Directory.GetParent(path)?.Parent?.FullName == examplesRoot)
                .Select(path => Directory.GetParent(path)!.Name)
                .ToHashSet(StringComparer.Ordinal)
            : [];

        foreach (var missing in expected.Except(actual, StringComparer.Ordinal))
        {
            errors.Add(new ContractError("examples", null, $"Declared example project is missing: {missing}."));
        }

        foreach (var undeclared in actual.Except(expected, StringComparer.Ordinal))
        {
            errors.Add(new ContractError("examples", null, $"Example project has no reviewed contract: {undeclared}."));
        }

        ValidateRootFiles(repositoryRoot, errors);
        foreach (var contract in s_contracts)
        {
            ValidateExample(repositoryRoot, contract, errors);
        }

        return new ExampleValidationResult(s_contracts.Length, errors);
    }

    private static void ValidateRootFiles(
        string repositoryRoot,
        List<ContractError> errors
    )
    {
        var readmePath = Path.Combine(repositoryRoot, "examples", "README.md");
        if (!File.Exists(readmePath))
        {
            errors.Add(new ContractError("examples/README.md", null, "File is missing."));
        }
        else
        {
            var readme = File.ReadAllText(readmePath);
            foreach (var contract in s_contracts.Where(contract =>
                         !readme.Contains(contract.Directory, StringComparison.Ordinal)))
            {
                errors.Add(new ContractError(
                    "examples/README.md",
                    null,
                    $"Omits {contract.Directory}."));
            }
        }

        var configurationPath = Path.Combine(
            repositoryRoot,
            "examples",
            "ExampleDatabaseConfiguration.cs");
        if (!File.Exists(configurationPath))
        {
            errors.Add(new ContractError(
                "examples/ExampleDatabaseConfiguration.cs",
                null,
                "Shared example database configuration is missing."));
            return;
        }

        var configuration = File.ReadAllText(configurationPath);
        foreach (var token in new[]
                 {
                     "DOKA_EXAMPLE_DATABASE_TARGET",
                     "DOKA_EXAMPLE_CONNECTION_STRING",
                     "Database = databaseName",
                     "\"mysql84\"",
                     "\"mariadb114\"",
                     "\"mariadb118\"",
                 })
        {
            if (!configuration.Contains(token, StringComparison.Ordinal))
            {
                errors.Add(new ContractError(
                    "examples/ExampleDatabaseConfiguration.cs",
                    null,
                    $"Omits {token}."));
            }
        }
    }

    private static void ValidateExample(
        string repositoryRoot,
        ExampleContract contract,
        List<ContractError> errors
    )
    {
        var relativeDirectory = $"examples/{contract.Directory}";
        var directory = Path.Combine(repositoryRoot, relativeDirectory);
        var requiredFiles = new[]
        {
            contract.Project,
            "README.md",
            "Program.cs",
        };
        foreach (var requiredFile in requiredFiles.Where(file =>
                     !File.Exists(Path.Combine(directory, file))))
        {
            errors.Add(new ContractError(
                relativeDirectory,
                null,
                $"Missing {requiredFile}."));
        }

        if (requiredFiles.Any(file => !File.Exists(Path.Combine(directory, file))))
        {
            return;
        }

        var readme = File.ReadAllText(Path.Combine(directory, "README.md"));
        if (!readme.Contains("dotnet run --project", StringComparison.Ordinal)
            || !readme.Contains(contract.Project, StringComparison.Ordinal))
        {
            errors.Add(new ContractError(
                $"{relativeDirectory}/README.md",
                null,
                "Omits its exact dotnet run command."));
        }

        var source = string.Join(
            '\n',
            Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
        foreach (var marker in s_placeholderMarkers.Where(marker =>
                     source.Contains(marker, StringComparison.Ordinal)))
        {
            errors.Add(new ContractError(
                relativeDirectory,
                null,
                $"Placeholder marker remains: {marker}."));
        }

        foreach (var token in contract.RequiredTokens.Where(token =>
                     !source.Contains(token, StringComparison.Ordinal)))
        {
            errors.Add(new ContractError(
                relativeDirectory,
                null,
                $"Required scenario token is missing: {token}."));
        }

        if (source.Contains("EnsureDeleted", StringComparison.Ordinal)
            && !source.Contains("ExampleDatabaseConfiguration.Create", StringComparison.Ordinal))
        {
            errors.Add(new ContractError(
                relativeDirectory,
                null,
                "Destructive lifecycle bypasses shared database isolation."));
        }

        if (!contract.InvariantChecking)
        {
            return;
        }

        foreach (var token in s_invariantTokens.Where(token =>
                     !source.Contains(token, StringComparison.Ordinal)))
        {
            errors.Add(new ContractError(
                relativeDirectory,
                null,
                $"Live invariant token is missing: {token}."));
        }
    }

    private sealed record ExampleContract(
        string Directory,
        string Project,
        IReadOnlyList<string> RequiredTokens,
        bool InvariantChecking
    );
}

internal sealed record ExampleValidationResult(
    int ExampleCount,
    IReadOnlyList<ContractError> Errors
);
