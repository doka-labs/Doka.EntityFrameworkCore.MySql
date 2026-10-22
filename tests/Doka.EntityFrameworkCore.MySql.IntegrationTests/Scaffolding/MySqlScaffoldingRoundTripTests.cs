namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that core relational metadata survives live reverse engineering and
/// provider code generation on every supported database family and release line.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlScaffoldingRoundTripTests
{
    private const string ParentTable = "doka_scaffold_core_parent";
    private const string ChildTable = "doka_scaffold_core_child";
    private const string SummaryView = "doka_scaffold_core_summary";
    private const string CheckConstraint = "CK_doka_scaffold_optional";
    private const string UniqueConstraint = "UQ_doka_scaffold_code";
    private const string ForeignKey = "FK_doka_scaffold_child_parent";
    private const string IndexStoreTable = "doka_scaffold_index_store";
    private const string IndexStoreSequence = "doka_scaffold_sequence";
    private const string EmulatedSequenceTable = "__efsequence_" + IndexStoreSequence;
    private const string PrefixIndex = "IX_doka_scaffold_name_code";
    private const string FullTextIndex = "IX_doka_scaffold_body";
    private const string SpatialIndex = "IX_doka_scaffold_location";
    private const string UniqueIndex = "UX_doka_scaffold_code";
    private const string FunctionalIndex = "IX_doka_scaffold_lower_name";
    private static readonly int[] s_prefixLengths = [32, 0];
    private static readonly bool[] s_indexDirections = [false, true];

    /// <summary>
    /// Verifies core schema-metadata fidelity against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_core_schema_metadata_roundtrips()
    {
        await RunCoreSchemaRoundTripAsync(IntegrationDatabaseTarget.MySql84)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies core schema-metadata fidelity against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_core_schema_metadata_roundtrips()
    {
        await RunCoreSchemaRoundTripAsync(IntegrationDatabaseTarget.MySql97)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies core schema-metadata fidelity against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_core_schema_metadata_roundtrips()
    {
        await RunCoreSchemaRoundTripAsync(IntegrationDatabaseTarget.MariaDb1011)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies core schema-metadata fidelity against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_core_schema_metadata_roundtrips()
    {
        await RunCoreSchemaRoundTripAsync(IntegrationDatabaseTarget.MariaDb114)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies core schema-metadata fidelity against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_core_schema_metadata_roundtrips()
    {
        await RunCoreSchemaRoundTripAsync(IntegrationDatabaseTarget.MariaDb118)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies core schema-metadata fidelity against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_core_schema_metadata_roundtrips()
    {
        await RunCoreSchemaRoundTripAsync(IntegrationDatabaseTarget.MariaDb123)
            .ConfigureAwait(false);
    }

    private static async Task RunCoreSchemaRoundTripAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await CreateSchemaAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var serviceProvider =
                ScaffoldingTestServices.CreateDesignTimeServiceProvider(
                    includeSpatialServices: true);

            using var scope = serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var databaseOptions = new DatabaseModelFactoryOptions(
                [
                    ParentTable,
                    ChildTable,
                    SummaryView,
                ],
                Array.Empty<string>());

            // Act
            var databaseModel = scopedServices
                .GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, databaseOptions);

            var scaffoldedModel = scopedServices
                .GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    databaseOptions,
                    new ModelReverseEngineerOptions(),
                    ScaffoldingTestServices.CreateCodeGenerationOptions(connectionString));

            // Assert
            AssertDatabaseModel(databaseModel);
            AssertGeneratedModel(scaffoldedModel);
        }
        finally
        {
            await DropSchemaAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static bool IsMariaDb(
        IntegrationDatabaseTarget target
    ) => IntegrationTestEnvironment.GetServerVersion(target).IsMariaDb;
}
