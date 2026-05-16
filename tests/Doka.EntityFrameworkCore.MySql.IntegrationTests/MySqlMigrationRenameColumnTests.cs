using Microsoft.EntityFrameworkCore.Metadata;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Engine-matrix coverage for the RENAME COLUMN migration operation. Two paths
/// share the entry point but emit different DDL:
/// - MariaDB 11.8 (>= 10.5.2): native <c>ALTER TABLE ... RENAME COLUMN old TO new</c>.
/// - Capability-driven fallback: <c>ALTER TABLE ... CHANGE COLUMN old new &lt;definition&gt;</c>
///   exercised by forcing an older MariaDB version string so the engine profile drops
///   <see cref="Capability.SupportsRenameColumnSyntax"/>.
/// MySQL 8.4 + MariaDB 11.4 share the modern path; the snapshot DDL test pins both
/// paths against the live server to keep the fallback honest.
/// </summary>
public sealed class MySqlMigrationRenameColumnTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_native_rename_column_persists_data()
    {
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MariaDb118,
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
            expectFallback: false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_legacy_version_uses_change_column_fallback_and_persists_data()
    {
        // Force a MariaDB version string that predates the RENAME COLUMN feature
        // so EngineProfile drops SupportsRenameColumnSyntax. The live server still
        // accepts CHANGE COLUMN, so the round-trip remains correctness-checked.
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MariaDb118,
            MySqlServerVersion.MariaDb(new Version(10, 5, 0)),
            expectFallback: true);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_native_rename_column_persists_data()
    {
        await RunRenameRoundTrip(
            IntegrationDatabaseTarget.MySql84,
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
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

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `RenameItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `RenameItems` (`Id` int NOT NULL AUTO_INCREMENT, `Old` varchar(64) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO `RenameItems` (`Old`) VALUES ('alpha'), ('beta'), ('gamma');");

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
            }
            else
            {
                Assert.Contains("RENAME COLUMN", emitted, StringComparison.Ordinal);
                Assert.DoesNotContain("CHANGE COLUMN", emitted, StringComparison.Ordinal);
            }

            await context.Database.ExecuteSqlRawAsync(emitted);

            await using var inspectionConnection = new MySqlConnection(connectionString);
            await inspectionConnection.OpenAsync();
            await using var verifyCommand = inspectionConnection.CreateCommand();
            verifyCommand.CommandText =
                "SELECT `Renamed` FROM `RenameItems` ORDER BY `Id`;";
            await using var reader = await verifyCommand.ExecuteReaderAsync();

            var roundtripped = new List<string>();
            while (await reader.ReadAsync())
            {
                roundtripped.Add(reader.GetString(0));
            }

            Assert.Equal(["alpha", "beta", "gamma"], roundtripped);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `RenameItems`;");
        }
    }

    private static DbContextOptions<T> CreateOptions<T>(
        string connectionString,
        MySqlServerVersion serverVersion
    )
        where T : DbContext
    {
        var builder = new DbContextOptionsBuilder<T>();
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
                    .IsRequired();
            });
        }
    }
}
