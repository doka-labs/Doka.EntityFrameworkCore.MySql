using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Enforces the declared large-schema, least-privilege, latency, allocation, managed
/// heap, query-count, determinism, and cleanup contract for reverse engineering.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlScaffoldingScaleTests
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
        // Arrange
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

            var measuredRuns = new List<ScaffoldingRunMetrics>(policy.MeasuredRuns);

            // Act: Repeated samples qualify one fixed schema and must retain the same signature.
            for (var run = 0; run < policy.MeasuredRuns; run++)
            {
                var metrics = RunMeasuredScaffolding(
                    connectionString,
                    tableNames);

                measuredRuns.Add(metrics);
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

            }

            var firstQueryRun = MeasureMetadataCommands(
                connectionString,
                tableNames);

            var secondQueryRun = MeasureMetadataCommands(
                connectionString,
                tableNames);

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

            // Assert
            for (var run = 0; run < measuredRuns.Count; run++)
            {
                var metrics = measuredRuns[run];

                AssertMeasuredBudgets(metrics, policy.Budgets, target, run);
                Assert.Equal(policy.TableCount, metrics.EntityFileCount);
                Assert.Equal(measuredRuns[0].Signature, metrics.Signature);
            }

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
        }
        finally
        {
            await DropLargeSchemaAsync(connectionString, tableNames)
                .ConfigureAwait(false);
        }
    }
}
