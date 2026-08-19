namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the MariaDB compatibility baseline against the supported MariaDB targets.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MariaDbCompatibilityBaselineTests
{
    private const string TableName = "Phase3MariaDbEntities";

    /// <summary>
    /// Verifies the approved capability-driven baseline against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_capability_driven_runtime_baseline_succeeds() =>
        VerifyMariaDbCompatibilityBaselineAsync(
            IntegrationDatabaseTarget.MariaDb1011,
            new Version(10, 11, 0));

    /// <summary>
    /// Verifies the approved capability-driven baseline against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_capability_driven_runtime_baseline_succeeds() => VerifyMariaDbCompatibilityBaselineAsync(
        IntegrationDatabaseTarget.MariaDb114,
        new Version(11, 4, 0));

    /// <summary>
    /// Verifies the approved capability-driven baseline against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_capability_driven_runtime_baseline_succeeds() => VerifyMariaDbCompatibilityBaselineAsync(
        IntegrationDatabaseTarget.MariaDb118,
        new Version(11, 8, 0));

    /// <summary>
    /// Verifies the approved capability-driven baseline against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_capability_driven_runtime_baseline_succeeds() =>
        VerifyMariaDbCompatibilityBaselineAsync(
            IntegrationDatabaseTarget.MariaDb123,
            new Version(12, 3, 0));

    private static async Task VerifyMariaDbCompatibilityBaselineAsync(
        IntegrationDatabaseTarget target,
        Version expectedVersion
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection
                .OpenAsync()
                .ConfigureAwait(false);

            var detectedServerVersion = MySqlServerVersion.AutoDetect(connection);

            Assert.True(detectedServerVersion.IsMariaDb);
            Assert.Equal(expectedVersion.Major, detectedServerVersion.Version.Major);
            Assert.Equal(expectedVersion.Minor, detectedServerVersion.Version.Minor);
            Assert.True(detectedServerVersion.Profile.Supports(ProviderCapability.Savepoints));
            Assert.Equal(
                ProviderSupportStatus.Native,
                detectedServerVersion.Profile.GetSupport(ProviderCapability.ReturningClause));
            Assert.Equal(
                ProviderSupportStatus.Emulated,
                detectedServerVersion.Profile.GetSupport(ProviderCapability.JsonColumns));

            await using var context = new MariaDbCompatibilityContext(CreateOptions(connectionString, expectedVersion));
            var strategy = context.Database.CreateExecutionStrategy();

            await strategy
                .ExecuteAsync(async () =>
                {
                    await using var transaction = await context
                        .Database.BeginTransactionAsync()
                        .ConfigureAwait(false);

                    Assert.True(transaction.SupportsSavepoints);

                    context.Entities.Add(
                        new MariaDbEntity
                        {
                            Name = "before-savepoint",
                            Payload = """{"kind":"alpha","items":[1,2]}""",
                        });
                    await context
                        .SaveChangesAsync()
                        .ConfigureAwait(false);

                    await transaction
                        .CreateSavepointAsync("before-second-row")
                        .ConfigureAwait(false);

                    context.Entities.Add(
                        new MariaDbEntity
                        {
                            Name = "after-savepoint",
                            Payload = """{"kind":"beta","items":[1,2,3]}""",
                        });
                    await context
                        .SaveChangesAsync()
                        .ConfigureAwait(false);

                    await transaction
                        .RollbackToSavepointAsync("before-second-row")
                        .ConfigureAwait(false);
                    await transaction
                        .CommitAsync()
                        .ConfigureAwait(false);
                })
                .ConfigureAwait(false);

            await using var verificationContext =
                new MariaDbCompatibilityContext(CreateOptions(connectionString, expectedVersion));

            var entity = Assert.Single(
                await verificationContext
                    .Entities.OrderBy(candidate => candidate.Id)
                    .ToListAsync()
                    .ConfigureAwait(false));

            Assert.Equal("before-savepoint", entity.Name);
            Assert.Equal("alpha", entity.VirtualKind);
            Assert.Equal(2, entity.StoredCount);

            await using var metadataCommand = connection.CreateCommand();
            metadataCommand.CommandText = """
                                          SELECT DATA_TYPE, COLLATION_NAME
                                          FROM information_schema.COLUMNS
                                          WHERE TABLE_SCHEMA = DATABASE()
                                            AND TABLE_NAME = @tableName
                                            AND COLUMN_NAME = 'Payload';
                                          """;

            var tableNameParameter = metadataCommand.CreateParameter();
            tableNameParameter.ParameterName = "@tableName";
            tableNameParameter.Value = TableName;
            metadataCommand.Parameters.Add(tableNameParameter);

            await using var reader = await metadataCommand
                .ExecuteReaderAsync()
                .ConfigureAwait(false);

            Assert.True(
                await reader
                    .ReadAsync()
                    .ConfigureAwait(false));
            Assert.Equal("longtext", reader.GetString(0));
            Assert.Equal("utf8mb4_bin", reader.GetString(1));
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static DbContextOptions<MariaDbCompatibilityContext> CreateOptions(
        string connectionString,
        Version mariaDbVersion
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<MariaDbCompatibilityContext>();

        builder.UseMySql(
            connectionString,
            MySqlServerVersion.MariaDb(mariaDbVersion),
            options => options.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(1)));

        return builder.Options;
    }

    private static async Task ResetDatabaseObjectsAsync(
        string connectionString
    )
    {
        await IntegrationDatabaseUtilities
            .EnsureDatabaseExistsAsync(connectionString)
            .ConfigureAwait(false);

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               DROP TABLE IF EXISTS `{TableName}`;
                               CREATE TABLE `{TableName}` (
                                   `Id` int NOT NULL AUTO_INCREMENT,
                                   `Name` longtext NOT NULL,
                                   `Payload` longtext COLLATE utf8mb4_bin NOT NULL CHECK (JSON_VALID(`Payload`)),
                                   `VirtualKind` varchar(64) GENERATED ALWAYS AS (JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))) VIRTUAL,
                                   `StoredCount` int GENERATED ALWAYS AS (JSON_LENGTH(`Payload`)) STORED,
                                   CONSTRAINT `PK_{TableName}` PRIMARY KEY (`Id`)
                               ) CHARACTER SET utf8mb4;
                               """;

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private sealed class MariaDbCompatibilityContext : DbContext
    {
        public MariaDbCompatibilityContext(
            DbContextOptions<MariaDbCompatibilityContext> options
        ) : base(options) { }

        public DbSet<MariaDbEntity> Entities => Set<MariaDbEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<MariaDbEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(candidate => candidate.Id);
                entity
                    .Property(candidate => candidate.Id)
                    .UseMySqlAutoIncrementColumn();
                entity.Property(candidate => candidate.Name);
                entity
                    .Property(candidate => candidate.Payload)
                    .HasColumnType("json");
                entity
                    .Property(candidate => candidate.VirtualKind)
                    .HasColumnType("varchar(64)")
                    .HasComputedColumnSql("JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.kind'))", stored: false);
                entity
                    .Property(candidate => candidate.StoredCount)
                    .HasColumnType("int")
                    .HasComputedColumnSql("JSON_LENGTH(`Payload`)", stored: true);
            });
        }
    }

    private sealed class MariaDbEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public string VirtualKind { get; set; } = string.Empty;

        public int StoredCount { get; set; }
    }
}
