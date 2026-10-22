using System.IO;
using System.Security.Cryptography;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Shares the infrastructure responsibility without changing the test fixture.</summary>
public sealed partial class MySqlScaffoldingScaleTests
{
    private static ScaffoldingRunMetrics RunMeasuredScaffolding(
        string connectionString,
        string[] tableNames
    )
    {
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);
        GC.WaitForPendingFinalizers();

        var baselineHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
        using var heapSampler = new ManagedHeapSampler(baselineHeapBytes);
        var allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        ScaffoldedModel scaffoldedModel;

        using (var serviceProvider =
               ScaffoldingTestServices.CreateDesignTimeServiceProvider())
        using (var scope = serviceProvider.CreateScope())
        {
            scaffoldedModel = scope
                .ServiceProvider.GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    new DatabaseModelFactoryOptions(tableNames, Array.Empty<string>()),
                    new ModelReverseEngineerOptions(),
                    CreateCodeGenerationOptions());
        }

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore;
        var peakHeapGrowthBytes = heapSampler.Stop();
        var emittedPaths = scaffoldedModel
            .AdditionalFiles.Select(file => file.Path)
            .ToArray();

        Assert.Equal(
            emittedPaths.OrderBy(path => path, StringComparer.Ordinal),
            emittedPaths);

        return new ScaffoldingRunMetrics(
            stopwatch.Elapsed,
            allocatedBytes,
            peakHeapGrowthBytes,
            scaffoldedModel.AdditionalFiles.Count,
            CreateSignature(scaffoldedModel));
    }

    private static MetadataCommandMetrics MeasureMetadataCommands(
        string connectionString,
        string[] tableNames
    )
    {
        using var connection = new CountingDbConnection(
            new MySqlConnection(connectionString));

        connection.Open();

        var factory = new MySqlDatabaseModelFactory(
            new MySqlConnectorDriverFacade(),
            new MySqlScaffoldingContext());

        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(tableNames, Array.Empty<string>()));

        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(tableNames.Length, databaseModel.Tables.Count);

        return new MetadataCommandMetrics(
            connection.ExecutedCommandCount,
            connection.ExecutedCommandTexts);
    }

    private static void AssertMetadataCommandContract(
        MetadataCommandMetrics metrics,
        IntegrationDatabaseTarget target,
        int selectedTableCount,
        int maximumSetBasedMetadataCommands
    )
    {
        // Arrange
        var expectedDefinitionCommands = RequiresPerTableDefinitionFallback(target)
            ? selectedTableCount
            : 0;

        // Act
        var definitionCommands = metrics.CommandTexts
            .Count(IsTableDefinitionCommand);

        // Assert
        Assert.Equal(expectedDefinitionCommands, definitionCommands);
        Assert.InRange(
            metrics.CommandCount,
            1,
            maximumSetBasedMetadataCommands + expectedDefinitionCommands);
        Assert.All(
            metrics.CommandTexts,
            commandText => Assert.True(
                commandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                || IsTableDefinitionCommand(commandText),
                $"Unexpected scaffolding metadata command: {commandText}"));
    }

    private static bool RequiresPerTableDefinitionFallback(
        IntegrationDatabaseTarget target
    )
    {
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        // MariaDB exposes period-role catalog columns only from 11.4.1. Older
        // releases therefore require exactly one SHOW CREATE TABLE command for
        // every selected table; the set-based command cap remains unchanged.
        return serverVersion.IsMariaDb
            && serverVersion.Version < new Version(11, 4, 1);
    }

    private static bool IsTableDefinitionCommand(
        string commandText
    ) => commandText.TrimStart().StartsWith(
        "SET STATEMENT sql_mode = '', sql_quote_show_create = 1 FOR SHOW CREATE TABLE ",
        StringComparison.OrdinalIgnoreCase);

    private static async Task CreateLargeSchemaAsync(
        string connectionString,
        string[] tableNames
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder();

        for (var index = 0; index < tableNames.Length; index++)
        {
            var tableName = tableNames[index];

            sql
                .Append("CREATE TABLE ")
                .Append(DelimitIdentifier(tableName))
                .AppendLine(" (")
                .AppendLine("  `Id` INT NOT NULL AUTO_INCREMENT,")
                .AppendLine("  `ParentId` INT NULL,")
                .AppendLine("  `Code` VARCHAR(64) NOT NULL,")
                .AppendLine("  `Name` VARCHAR(191) NOT NULL,")
                .AppendLine("  `Amount` DECIMAL(20,2) NOT NULL DEFAULT 0,")
                .AppendLine("  `CreatedAt` DATETIME(3) NOT NULL,")
                .AppendLine("  `Payload` TEXT NULL,")
                .AppendLine(
                    "  `ComputedAmount` DECIMAL(20,2) GENERATED ALWAYS AS (`Amount` + 1) STORED,")
                .AppendLine("  PRIMARY KEY (`Id`),")
                .AppendLine("  UNIQUE KEY `UX_Code` (`Code`),")
                .Append("  KEY `IX_Name` (`Name`(32))");

            if (index > 0)
            {
                sql
                    .AppendLine(",")
                    .Append("  CONSTRAINT ")
                    .Append(DelimitIdentifier($"FK_{tableName}_parent"))
                    .Append(" FOREIGN KEY (`ParentId`) REFERENCES ")
                    .Append(DelimitIdentifier(tableNames[index - 1]))
                    .AppendLine(" (`Id`)");
            }
            else
            {
                sql.AppendLine();
            }

            sql.AppendLine(") ENGINE=InnoDB CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        }

        command.CommandTimeout = 120;
        command.CommandText = sql.ToString();

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task DropLargeSchemaAsync(
        string connectionString,
        string[] tableNames
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder();

        for (var index = tableNames.Length - 1; index >= 0; index--)
        {
            sql
                .Append("DROP TABLE IF EXISTS ")
                .Append(DelimitIdentifier(tableNames[index]))
                .AppendLine(";");
        }

        command.CommandTimeout = 120;
        command.CommandText = sql.ToString();

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static string[] CreateTableNames(
        int tableCount
    ) => Enumerable
        .Range(0, tableCount)
        .Select(index => TablePrefix + index.ToString("D3", CultureInfo.InvariantCulture))
        .ToArray();

    private static ModelCodeGenerationOptions CreateCodeGenerationOptions() => new()
    {
        ContextName = "LargeSchemaContext",
        ContextNamespace = "Doka.Scale",
        ModelNamespace = "Doka.Scale.Models",
        RootNamespace = "Doka.Scale",
        Language = "C#",
        ContextDir = "Generated",
        ProjectDir = "Generated",
        ConnectionString = "Server=localhost;Database=scale;",
        SuppressConnectionStringWarning = true,
        SuppressOnConfiguring = true,
        UseNullableReferenceTypes = true,
    };

    private static string CreateSignature(
        ScaffoldedModel scaffoldedModel
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendHashData(hash, scaffoldedModel.ContextFile.Path);
        AppendHashData(hash, scaffoldedModel.ContextFile.Code);

        foreach (var file in scaffoldedModel.AdditionalFiles)
        {
            AppendHashData(hash, file.Path);
            AppendHashData(hash, file.Code);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendHashData(
        IncrementalHash hash,
        string value
    )
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var bytes = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var written = Encoding.UTF8.GetBytes(value, bytes);
            hash.AppendData(bytes, 0, written);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static ScaffoldingPerformancePolicy LoadPolicy()
    {
        var policyPath = Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "scaffolding-performance-policy.json");

        return JsonSerializer.Deserialize<ScaffoldingPerformancePolicy>(
                File.ReadAllText(policyPath),
                s_policySerializerOptions)
            ?? throw new InvalidOperationException(
                "The scaffolding performance policy did not contain a JSON object.");
    }

    private static void ValidatePolicy(
        ScaffoldingPerformancePolicy policy
    )
    {
        Assert.Equal(ExpectedPolicySchemaVersion, policy.SchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(policy.ScenarioId));
        Assert.Equal(ExpectedColumnsPerTable, policy.ColumnsPerTable);
        Assert.True(policy.TableCount > policy.RestrictedVisibleTableCount);
        Assert.True(policy.RestrictedVisibleTableCount > 0);
        Assert.True(policy.MeasuredRuns >= 2);
        Assert.True(policy.Budgets.MaximumSetBasedMetadataCommandsPerRun > 0);
        Assert.True(policy.Budgets.MaximumElapsedMillisecondsPerRun > 0);
        Assert.True(policy.Budgets.MaximumAllocatedBytesPerRun > 0);
        Assert.True(policy.Budgets.MaximumManagedHeapGrowthBytesPerRun > 0);
        Assert.Contains(
            policy.CleanupAndCancellationTests,
            testId => testId.EndsWith(
                "Failed_or_cancelled_reverse_engineering_releases_operation_state",
                StringComparison.Ordinal));
    }

    private static void AssertMeasuredBudgets(
        ScaffoldingRunMetrics metrics,
        ScaffoldingPerformanceBudgets budgets,
        IntegrationDatabaseTarget target,
        int run
    )
    {
        Assert.True(
            metrics.Elapsed.TotalMilliseconds <= budgets.MaximumElapsedMillisecondsPerRun,
            $"{target} run {run + 1} elapsed {metrics.Elapsed.TotalMilliseconds:F0} ms; "
            + $"budget {budgets.MaximumElapsedMillisecondsPerRun} ms.");
        Assert.True(
            metrics.AllocatedBytes <= budgets.MaximumAllocatedBytesPerRun,
            $"{target} run {run + 1} allocated {metrics.AllocatedBytes} bytes; "
            + $"budget {budgets.MaximumAllocatedBytesPerRun} bytes.");
        Assert.True(
            metrics.ManagedHeapGrowthBytes <= budgets.MaximumManagedHeapGrowthBytesPerRun,
            $"{target} run {run + 1} grew the managed heap by "
            + $"{metrics.ManagedHeapGrowthBytes} bytes; "
            + $"budget {budgets.MaximumManagedHeapGrowthBytesPerRun} bytes.");
    }

    private static string DelimitIdentifier(
        string identifier
    ) => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the integration-test output path.");
    }
}
