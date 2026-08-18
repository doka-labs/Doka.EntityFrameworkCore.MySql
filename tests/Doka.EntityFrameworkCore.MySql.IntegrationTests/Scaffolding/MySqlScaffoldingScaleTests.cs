using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Xunit.Abstractions;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Enforces the declared large-schema, least-privilege, latency, allocation, managed
/// heap, query-count, determinism, and cleanup contract for reverse engineering.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlScaffoldingScaleTests
{
    private const string TablePrefix = "doka_scale_t";
    private const int ExpectedPolicySchemaVersion = 2;
    private const int ExpectedColumnsPerTable = 8;
    private static readonly JsonSerializerOptions s_policySerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly ITestOutputHelper _output;

    public MySqlScaffoldingScaleTests(
        ITestOutputHelper output
    )
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Verifies the scale and restricted-metadata policy against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_large_and_restricted_schemas_meet_declared_budgets()
    {
        await RunScaleContractAsync(IntegrationDatabaseTarget.MySql84)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the scale and restricted-metadata policy against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_large_and_restricted_schemas_meet_declared_budgets()
    {
        await RunScaleContractAsync(IntegrationDatabaseTarget.MySql97)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the scale and restricted-metadata policy against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_large_and_restricted_schemas_meet_declared_budgets()
    {
        await RunScaleContractAsync(IntegrationDatabaseTarget.MariaDb1011)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the scale and restricted-metadata policy against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_large_and_restricted_schemas_meet_declared_budgets()
    {
        await RunScaleContractAsync(IntegrationDatabaseTarget.MariaDb114)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the scale and restricted-metadata policy against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_large_and_restricted_schemas_meet_declared_budgets()
    {
        await RunScaleContractAsync(IntegrationDatabaseTarget.MariaDb118)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the scale and restricted-metadata policy against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_large_and_restricted_schemas_meet_declared_budgets()
    {
        await RunScaleContractAsync(IntegrationDatabaseTarget.MariaDb123)
            .ConfigureAwait(false);
    }

    private async Task RunScaleContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var policy = LoadPolicy();
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var tableNames = CreateTableNames(policy.TableCount);

        ValidatePolicy(policy);

        await DropLargeSchemaAsync(connectionString, tableNames)
            .ConfigureAwait(false);

        try
        {
            await CreateLargeSchemaAsync(connectionString, tableNames)
                .ConfigureAwait(false);

            string? expectedSignature = null;

            for (var run = 0; run < policy.MeasuredRuns; run++)
            {
                var metrics = RunMeasuredScaffolding(
                    connectionString,
                    tableNames);

                AssertMeasuredBudgets(metrics, policy.Budgets, target, run);
                Assert.Equal(policy.TableCount, metrics.EntityFileCount);
                _output.WriteLine(
                    "{0} run {1}: elapsed={2:F0}ms; allocated={3}B; "
                    + "managedHeapGrowth={4}B; entityFiles={5}; signature={6}",
                    target,
                    run + 1,
                    metrics.Elapsed.TotalMilliseconds,
                    metrics.AllocatedBytes,
                    metrics.ManagedHeapGrowthBytes,
                    metrics.EntityFileCount,
                    metrics.Signature);

                if (expectedSignature is null)
                {
                    expectedSignature = metrics.Signature;
                }
                else
                {
                    Assert.Equal(expectedSignature, metrics.Signature);
                }
            }

            var firstQueryRun = MeasureMetadataCommands(
                connectionString,
                tableNames);
            var secondQueryRun = MeasureMetadataCommands(
                connectionString,
                tableNames);

            Assert.Equal(firstQueryRun.CommandCount, secondQueryRun.CommandCount);
            AssertMetadataCommandContract(
                firstQueryRun,
                target,
                tableNames.Length,
                policy.Budgets.MaximumSetBasedMetadataCommandsPerRun);
            AssertMetadataCommandContract(
                secondQueryRun,
                target,
                tableNames.Length,
                policy.Budgets.MaximumSetBasedMetadataCommandsPerRun);
            _output.WriteLine(
                "{0} metadata commands: first={1}; second={2}; setBasedBudget={3}",
                target,
                firstQueryRun.CommandCount,
                secondQueryRun.CommandCount,
                policy.Budgets.MaximumSetBasedMetadataCommandsPerRun);

            var restrictedCommandCount = await VerifyRestrictedMetadataAsync(
                    connectionString,
                    tableNames,
                    policy.RestrictedVisibleTableCount,
                    policy.Budgets.MaximumSetBasedMetadataCommandsPerRun,
                    target)
                .ConfigureAwait(false);
            _output.WriteLine(
                "{0} restricted metadata: visibleTables={1}; commands={2}; setBasedBudget={3}",
                target,
                policy.RestrictedVisibleTableCount,
                restrictedCommandCount,
                policy.Budgets.MaximumSetBasedMetadataCommandsPerRun);
        }
        finally
        {
            await DropLargeSchemaAsync(connectionString, tableNames)
                .ConfigureAwait(false);
        }
    }

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

    private static async Task<int> VerifyRestrictedMetadataAsync(
        string rootConnectionString,
        string[] tableNames,
        int visibleTableCount,
        int maximumSetBasedMetadataCommands,
        IntegrationDatabaseTarget target
    )
    {
        var account = RestrictedAccount.Create();
        var executedCommandCount = 0;

        try
        {
            await CreateRestrictedAccountAsync(
                    rootConnectionString,
                    account,
                    tableNames.Take(visibleTableCount))
                .ConfigureAwait(false);

            var restrictedConnectionString = CreateRestrictedConnectionString(
                rootConnectionString,
                account);

            await AssertTableAccessDeniedAsync(
                    restrictedConnectionString,
                    tableNames[visibleTableCount])
                .ConfigureAwait(false);

            await using var connection = new CountingDbConnection(
                new MySqlConnection(restrictedConnectionString));

            await connection
                .OpenAsync()
                .ConfigureAwait(false);

            var factory = new MySqlDatabaseModelFactory(
                new MySqlConnectorDriverFacade(),
                new MySqlScaffoldingContext());
            var databaseModel = factory.Create(
                connection,
                new DatabaseModelFactoryOptions(Array.Empty<string>(), Array.Empty<string>()));
            var expectedVisibleTables = tableNames
                .Take(visibleTableCount)
                .OrderBy(tableName => tableName, StringComparer.Ordinal)
                .ToArray();
            var actualVisibleTables = databaseModel
                .Tables.Select(table => table.Name)
                .OrderBy(tableName => tableName, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedVisibleTables, actualVisibleTables);
            AssertMetadataCommandContract(
                new MetadataCommandMetrics(
                    connection.ExecutedCommandCount,
                    connection.ExecutedCommandTexts),
                target,
                visibleTableCount,
                maximumSetBasedMetadataCommands);
            Assert.Equal(ConnectionState.Open, connection.State);
            executedCommandCount = connection.ExecutedCommandCount;
        }
        finally
        {
            await DropRestrictedAccountAsync(rootConnectionString, account)
                .ConfigureAwait(false);
            await AssertRestrictedAccountRemovedAsync(rootConnectionString, account)
                .ConfigureAwait(false);
        }

        return executedCommandCount;
    }

    private static void AssertMetadataCommandContract(
        MetadataCommandMetrics metrics,
        IntegrationDatabaseTarget target,
        int selectedTableCount,
        int maximumSetBasedMetadataCommands
    )
    {
        var expectedDefinitionCommands = RequiresPerTableDefinitionFallback(target)
            ? selectedTableCount
            : 0;
        var definitionCommands = metrics.CommandTexts
            .Count(IsTableDefinitionCommand);

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

    private static async Task CreateRestrictedAccountAsync(
        string rootConnectionString,
        RestrictedAccount account,
        IEnumerable<string> visibleTableNames
    )
    {
        await using var connection = new MySqlConnection(rootConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        var databaseName = connection.Database;

        await using (var createUser = connection.CreateCommand())
        {
            createUser.CommandText =
                $"CREATE USER '{account.UserName}'@'%' IDENTIFIED BY '{account.Password}';";
            await createUser
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }

        foreach (var tableName in visibleTableNames)
        {
            await using var grant = connection.CreateCommand();
            grant.CommandText =
                $"GRANT SELECT ON {DelimitIdentifier(databaseName)}.{DelimitIdentifier(tableName)} "
                + $"TO '{account.UserName}'@'%';";
            await grant
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }
    }

    private static async Task DropRestrictedAccountAsync(
        string rootConnectionString,
        RestrictedAccount account
    )
    {
        await using var connection = new MySqlConnection(rootConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"DROP USER IF EXISTS '{account.UserName}'@'%';";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task AssertRestrictedAccountRemovedAsync(
        string rootConnectionString,
        RestrictedAccount account
    )
    {
        var restrictedConnectionString = CreateRestrictedConnectionString(
            rootConnectionString,
            account);
        await using var connection = new MySqlConnection(restrictedConnectionString);

        var exception = await Assert.ThrowsAsync<MySqlException>(
                () => connection.OpenAsync())
            .ConfigureAwait(false);

        Assert.Equal(1045, exception.Number);
    }

    private static async Task AssertTableAccessDeniedAsync(
        string restrictedConnectionString,
        string deniedTableName
    )
    {
        await using var connection = new MySqlConnection(restrictedConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT COUNT(*) FROM {DelimitIdentifier(deniedTableName)};";

        var exception = await Assert.ThrowsAsync<MySqlException>(
                () => command.ExecuteScalarAsync())
            .ConfigureAwait(false);

        Assert.Equal(1142, exception.Number);
    }

    private static string CreateRestrictedConnectionString(
        string rootConnectionString,
        RestrictedAccount account
    )
    {
        var builder = new MySqlConnectionStringBuilder(rootConnectionString)
        {
            UserID = account.UserName,
            Password = account.Password,
            Pooling = false,
        };

        return builder.ConnectionString;
    }

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

    private sealed class ManagedHeapSampler : IDisposable
    {
        private readonly long _baselineBytes;
        private readonly ManualResetEventSlim _stop = new();
        private readonly Thread _thread;
        private long _peakBytes;
        private int _stopped;

        public ManagedHeapSampler(
            long baselineBytes
        )
        {
            _baselineBytes = baselineBytes;
            _peakBytes = baselineBytes;
            _thread = new Thread(Sample)
            {
                IsBackground = true,
                Name = "Doka scaffolding managed-heap sampler",
            };
            _thread.Start();
        }

        public long Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
            {
                _stop.Set();
                _thread.Join();
            }

            return Math.Max(0, Volatile.Read(ref _peakBytes) - _baselineBytes);
        }

        public void Dispose()
        {
            Stop();
            _stop.Dispose();
        }

        private void Sample()
        {
            while (!_stop.Wait(TimeSpan.FromMilliseconds(1)))
            {
                UpdatePeak(GC.GetTotalMemory(forceFullCollection: false));
            }

            UpdatePeak(GC.GetTotalMemory(forceFullCollection: false));
        }

        private void UpdatePeak(
            long currentBytes
        )
        {
            var observedPeak = Volatile.Read(ref _peakBytes);

            while (currentBytes > observedPeak)
            {
                var replaced = Interlocked.CompareExchange(
                    ref _peakBytes,
                    currentBytes,
                    observedPeak);

                if (replaced == observedPeak)
                {
                    return;
                }

                observedPeak = replaced;
            }
        }
    }

    private sealed record RestrictedAccount(
        string UserName,
        string Password
    )
    {
        public static RestrictedAccount Create()
        {
            var token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12];

            return new RestrictedAccount(
                "doka_scale_" + token,
                "DokaRestricted_" + token);
        }
    }

    private sealed record ScaffoldingRunMetrics(
        TimeSpan Elapsed,
        long AllocatedBytes,
        long ManagedHeapGrowthBytes,
        int EntityFileCount,
        string Signature
    );

    private sealed record MetadataCommandMetrics(
        int CommandCount,
        IReadOnlyList<string> CommandTexts
    );

    private sealed class ScaffoldingPerformancePolicy
    {
        public required int SchemaVersion { get; init; }

        public required string ScenarioId { get; init; }

        public required int TableCount { get; init; }

        public required int ColumnsPerTable { get; init; }

        public required int RestrictedVisibleTableCount { get; init; }

        public required int MeasuredRuns { get; init; }

        public required ScaffoldingPerformanceBudgets Budgets { get; init; }

        public required string[] CleanupAndCancellationTests { get; init; }
    }

    private sealed class ScaffoldingPerformanceBudgets
    {
        public required int MaximumSetBasedMetadataCommandsPerRun { get; init; }

        public required int MaximumElapsedMillisecondsPerRun { get; init; }

        public required long MaximumAllocatedBytesPerRun { get; init; }

        public required long MaximumManagedHeapGrowthBytesPerRun { get; init; }
    }
}
