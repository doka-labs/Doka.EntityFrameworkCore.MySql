namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Executes provider-generated spatial seed operations and verifies their
/// materialization against every supported server.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlSpatialSeedIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_executes_spatial_seeds() =>
        AssertSpatialSeedAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_executes_spatial_seeds() =>
        AssertSpatialSeedAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_executes_spatial_seeds() =>
        AssertSpatialSeedAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_executes_spatial_seeds() =>
        AssertSpatialSeedAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_executes_spatial_seeds() =>
        AssertSpatialSeedAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_executes_spatial_seeds() =>
        AssertSpatialSeedAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertSpatialSeedAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await CleanupAsync(connection).ConfigureAwait(false);

        try
        {
            await using var context = new SpatialSeedIntegrationContext(
                IntegrationTestDbContextOptions
                    .Create<SpatialSeedIntegrationContext>()
                    .UseMySql(
                        connection,
                        serverVersion,
                        options => options.UseNetTopologySuite())
                    .Options);
            var model = context.GetService<IDesignTimeModel>().Model;
            var operations = context
                .GetService<IMigrationsModelDiffer>()
                .GetDifferences(null, model.GetRelationalModel());
            var table = Assert.Single(operations.OfType<CreateTableOperation>());
            var locationColumn = Assert.Single(
                table.Columns,
                column => column.Name == nameof(SpatialSeedIntegrationRecord.Location));
            var insert = Assert.Single(operations.OfType<InsertDataOperation>());
            var locationIndex = Array.IndexOf(insert.Columns, nameof(SpatialSeedIntegrationRecord.Location));

            Assert.Equal(typeof(Point), locationColumn.ClrType);
            Assert.True(locationIndex >= 0);
            Assert.IsType<Point>(insert.Values[0, locationIndex]);

            var generator = context.GetService<IMigrationsSqlGenerator>();
            var relationalConnection = context.GetService<IRelationalConnection>();

            foreach (var migrationCommand in generator.Generate(operations, model))
            {
                _ = await migrationCommand
                    .ExecuteNonQueryAsync(relationalConnection, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }

            context.ChangeTracker.Clear();
            var record = await context
                .Set<SpatialSeedIntegrationRecord>()
                .SingleAsync(entity => entity.Id == 1, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(13.4050, record.Location.X, 4);
            Assert.Equal(52.5200, record.Location.Y, 4);
            Assert.Equal(4326, record.Location.SRID);

            await using var readCommand = connection.CreateCommand();
            readCommand.CommandText =
                $"SELECT ST_SRID(`Location`) FROM `{SpatialSeedIntegrationContract.Table}` WHERE `Id` = 1;";
            Assert.Equal(4326, Convert.ToInt32(
                await readCommand.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture));
        }
        finally
        {
            await CleanupAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task CleanupAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{SpatialSeedIntegrationContract.Table}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

internal static class SpatialSeedIntegrationContract
{
    public const string Table = "DokaSpatialSeedRecords";
}

internal sealed class SpatialSeedIntegrationContext : DbContext
{
    public SpatialSeedIntegrationContext(
        DbContextOptions<SpatialSeedIntegrationContext> options
    ) : base(options) { }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<SpatialSeedIntegrationRecord>(entity =>
        {
            entity.ToTable(SpatialSeedIntegrationContract.Table);
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Location)
                .HasSrid(4326);
            entity.HasData(
                new SpatialSeedIntegrationRecord
                {
                    Id = 1,
                    Location = new Point(13.4050, 52.5200)
                    {
                        SRID = 4326,
                    },
                });
        });
    }
}

internal sealed class SpatialSeedIntegrationRecord
{
    public int Id { get; set; }

    public Point Location { get; set; } = null!;
}
