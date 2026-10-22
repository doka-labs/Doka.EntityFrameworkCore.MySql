namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Executes provider-generated column defaults against every supported server.
/// </summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlColumnDefaultIntegrationTests
{
    private const string GuidDefaultHex = "0198BFE2557370008000000000000001";

    private static readonly Guid s_guidDefault = Guid.Parse("0198bfe2-5573-7000-8000-000000000001");

    private static readonly TimeSpan s_maximumTime = TimeSpan.FromHours(838)
        + TimeSpan.FromMinutes(59)
        + TimeSpan.FromSeconds(59);

    /// <summary>
    /// Verifies my sql84 executes column default matrix.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 11)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies my sql97 executes column default matrix.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MySql97,
                MySqlServerVersion.MySql(new Version(9, 7, 2)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies maria db1011 executes column default matrix.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                MySqlServerVersion.MariaDb(new Version(10, 11, 18)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies maria db114 executes column default matrix.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 12)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies maria db118 executes column default matrix.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 8)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies maria db123 executes column default matrix.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MariaDb123,
                MySqlServerVersion.MariaDb(new Version(12, 3, 2)))
            .ConfigureAwait(false);
    }

    private static MySqlConnection CreateConnection(
        IntegrationDatabaseTarget target
    ) => new(new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target))
    {
        GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        Pooling = false,
    }.ConnectionString);

    private static async Task AssertColumnDefaultMatrixAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var tableName = $"ColumnDefaults_{Guid.NewGuid():N}";
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = new ColumnDefaultContext(
            IntegrationTestDbContextOptions.Create<ColumnDefaultContext>()
                .UseMySql(connection, serverVersion)
                .Options);

        var generator = context.GetService<IMigrationsSqlGenerator>();
        var relationalConnection = context.GetService<IRelationalConnection>();

        try
        {
            var createTable = CreateTableOperation(tableName);
            await ExecuteAsync(generator, context.Model, relationalConnection, [createTable])
                .ConfigureAwait(false);

            await ExecuteRawAsync(connection, $"INSERT INTO `{tableName}` (`Id`) VALUES (1);")
                .ConfigureAwait(false);
            await AssertInitialDefaultsAsync(connection, tableName)
                .ConfigureAwait(false);
            await AssertJsonMetadataAsync(connection, tableName)
                .ConfigureAwait(false);
            await AssertGuidDefaultMetadataAsync(connection, tableName, serverVersion)
                .ConfigureAwait(false);

            var addedDate = CreateColumn(
                tableName,
                "AddedDate",
                typeof(DateOnly),
                "date",
                new DateOnly(2027, 1, 2));

            var addedTime = CreateColumn(
                tableName,
                "AddedTime",
                typeof(TimeOnly),
                "time(6)",
                new TimeOnly(3, 4, 5).Add(TimeSpan.FromTicks(7_654_321)));

            await ExecuteAsync(generator, context.Model, relationalConnection, [addedDate, addedTime])
                .ConfigureAwait(false);
            await AssertAddedDefaultsAsync(connection, tableName, 1, "03:04:05.765432")
                .ConfigureAwait(false);

            var alterTime = new AlterColumnOperation
            {
                Table = tableName,
                Name = addedTime.Name,
                ClrType = typeof(TimeOnly),
                ColumnType = "time(6)",
                IsNullable = false,
                DefaultValue = new TimeOnly(23, 59, 58).Add(TimeSpan.FromTicks(1_234_567)),
                OldColumn =
                {
                    ClrType = typeof(TimeOnly),
                    ColumnType = "time(6)",
                    IsNullable = false,
                    DefaultValue = addedTime.DefaultValue,
                },
            };

            await ExecuteAsync(generator, context.Model, relationalConnection, [alterTime])
                .ConfigureAwait(false);
            await ExecuteRawAsync(connection, $"INSERT INTO `{tableName}` (`Id`) VALUES (2);")
                .ConfigureAwait(false);

            await AssertAddedDefaultsAsync(connection, tableName, 1, "03:04:05.765432")
                .ConfigureAwait(false);
            await AssertAddedDefaultsAsync(connection, tableName, 2, "23:59:58.123456")
                .ConfigureAwait(false);
            await AssertNullableTemporalRepairAsync(
                    generator,
                    context.Model,
                    relationalConnection,
                    connection,
                    tableName)
                .ConfigureAwait(false);
            await AssertConstrainedNullableRepairAsync(
                    generator,
                    context.Model,
                    relationalConnection,
                    connection,
                    tableName)
                .ConfigureAwait(false);
        }
        finally
        {
            await ExecuteRawAsync(connection, $"DROP TABLE IF EXISTS `{tableName}`;")
                .ConfigureAwait(false);
        }
    }
}
