namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Engine-matrix coverage for the RENAME COLUMN migration operation. Two paths
/// share the entry point but emit different DDL:
/// - MariaDB 11.8 (>= 10.5.2): native <c>ALTER TABLE ... RENAME COLUMN old TO new</c>.
/// - Capability-driven fallback: <c>ALTER TABLE ... CHANGE COLUMN old new &lt;definition&gt;</c>
///   exercised by forcing an older MariaDB version string so the engine profile drops
///   <see cref="EngineCapability.RenameColumnSyntax"/>.
/// Every active MySQL and MariaDB LTS target shares the modern path; the snapshot
/// DDL test pins both paths against live servers to keep the fallback honest.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlMigrationRenameColumnTests
{
    private const string ColumnComment = "rename\\comment 'quoted'";

    /// <summary>
    /// Verifies the native rename path and comment preservation on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_native_rename_column_persists_data()
    {
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MariaDb118,
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
            expectFallback: false);
    }

    /// <summary>
    /// Verifies the native rename path and comment preservation on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_native_rename_column_persists_data()
    {
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MariaDb114,
            MySqlServerVersion.MariaDb(new Version(11, 4, 0)),
            expectFallback: false);
    }

    /// <summary>
    /// Verifies the native rename path and comment preservation on MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_native_rename_column_persists_data()
    {
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb1011),
            expectFallback: false);
    }

    /// <summary>
    /// Verifies the native rename path and comment preservation on MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_native_rename_column_persists_data()
    {
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MariaDb123,
            IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb123),
            expectFallback: false);
    }

    /// <summary>
    /// Forces the legacy MariaDB capability profile and verifies its live fallback.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_legacy_version_uses_change_column_fallback_and_persists_data()
    {
        // Force a MariaDB version string that predates the RENAME COLUMN feature
        // so EngineProfile drops RenameColumnSyntax. The live server still
        // accepts CHANGE COLUMN, so the round-trip remains correctness-checked.
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MariaDb118,
            MySqlServerVersion.MariaDb(
                new Version(10, 5, 0),
                MySqlServerVersionCompatibilityMode.AllowUnsupported),
            expectFallback: true);
    }

    /// <summary>
    /// Verifies the native rename path and data preservation on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_native_rename_column_persists_data()
    {
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MySql84,
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            expectFallback: false);
    }

    /// <summary>
    /// Verifies the native rename path and data preservation on MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_native_rename_column_persists_data()
    {
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MySql97,
            IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MySql97),
            expectFallback: false);
    }

    private static async Task RunRenameRoundTrip(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion,
        bool expectFallback
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await using var context = new RenameContext(CreateOptions<RenameContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS `RenameItems`;",
            CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `RenameItems` ("
            + "`Id` int NOT NULL AUTO_INCREMENT, "
            + "`Old` varchar(64) NOT NULL COMMENT 'rename\\\\comment ''quoted''', "
            + "PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;",
            CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO `RenameItems` (`Old`) VALUES ('alpha'), ('beta'), ('gamma');",
            CancellationToken.None);

        try
        {
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var operation = new RenameColumnOperation
            {
                Table = "RenameItems",
                Name = "Old",
                NewName = "Renamed",
            };

            var designTimeModel = context.GetService<IDesignTimeModel>().Model;
            var commands = generator.Generate([operation], designTimeModel);

            Assert.Single(commands);

            var emitted = commands[0].CommandText;
            if (expectFallback)
            {
                Assert.Contains("CHANGE COLUMN", emitted, StringComparison.Ordinal);
                Assert.DoesNotContain("RENAME COLUMN", emitted, StringComparison.Ordinal);
                Assert.Contains("/*! SET @__doka_previous_sql_mode", emitted, StringComparison.Ordinal);
                Assert.Contains("NO_BACKSLASH_ESCAPES", emitted, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("RENAME COLUMN", emitted, StringComparison.Ordinal);
                Assert.DoesNotContain("CHANGE COLUMN", emitted, StringComparison.Ordinal);
            }

            await commands[0]
                .ExecuteNonQueryAsync(
                    context.GetService<IRelationalConnection>(),
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            await using var inspectionConnection = new MySqlConnection(connectionString);
            await inspectionConnection.OpenAsync(CancellationToken.None);
            await using var verifyCommand = inspectionConnection.CreateCommand();
            verifyCommand.CommandText =
                "SELECT `Renamed` FROM `RenameItems` ORDER BY `Id`;";

            var roundtripped = new List<string>();
            await using (var reader = await verifyCommand.ExecuteReaderAsync(CancellationToken.None))
            {
                while (await reader.ReadAsync(CancellationToken.None))
                {
                    roundtripped.Add(reader.GetString(0));
                }
            }

            Assert.Equal(["alpha", "beta", "gamma"], roundtripped);

            verifyCommand.CommandText = "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS "
                + "WHERE TABLE_SCHEMA = DATABASE() "
                + "AND TABLE_NAME = 'RenameItems' "
                + "AND COLUMN_NAME = 'Renamed';";

            Assert.Equal(
                ColumnComment,
                Assert.IsType<string>(
                    await verifyCommand
                        .ExecuteScalarAsync(CancellationToken.None)
                        .ConfigureAwait(false)));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "DROP TABLE IF EXISTS `RenameItems`;",
                CancellationToken.None);
        }
    }

    private static DbContextOptions<T> CreateOptions<T>(
        string connectionString,
        MySqlServerVersion serverVersion
    )
        where T : DbContext
    {
        var builder = IntegrationTestDbContextOptions.Create<T>();
        builder.UseMySql(connectionString, serverVersion);
        return builder.Options;
    }

    private sealed class RenameEntity
    {
        public int Id { get; set; }
        public string Renamed { get; set; } = string.Empty;
    }

    private sealed class RenameContext : DbContext
    {
        public RenameContext(
            DbContextOptions<RenameContext> options
        ) : base(options) { }

        public DbSet<RenameEntity> Items => Set<RenameEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<RenameEntity>(e =>
            {
                e.ToTable("RenameItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Renamed)
                    .HasColumnType("varchar(64)")
                    .HasComment(ColumnComment)
                    .IsRequired();
            });
        }
    }
}
