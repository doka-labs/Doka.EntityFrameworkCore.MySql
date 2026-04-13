namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies representative spatial runtime queries against live local servers.
/// </summary>
public sealed class MySqlNetTopologySuiteIntegrationTests
{
    private const string PointTableName = "Phase3SpatialPointEntities";
    private const string PolygonTableName = "Phase3SpatialPolygonEntities";

    /// <summary>
    /// Verifies that the MySQL 8.4 runtime can persist, query, and materialize the spatial baseline.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_spatial_queries_and_materialization_succeed()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await ResetSpatialObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new SpatialDbContext(CreateMySqlOptions(connectionString));

            await context
                .Database.ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{PointTableName}` (
                         `Id` int NOT NULL,
                         `Location` point NOT NULL,
                         CONSTRAINT `PK_{PointTableName}` PRIMARY KEY (`Id`)
                     );
                     """)
                .ConfigureAwait(false);

            context.PointEntities.AddRange(
                new SpatialPointEntity
                {
                    Id = 1,
                    Location = CreatePoint(13.4050, 52.5200),
                },
                new SpatialPointEntity
                {
                    Id = 2,
                    Location = CreatePoint(2.3522, 48.8566),
                });

            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var berlin = CreatePoint(13.4050, 52.5200);
            var matchingIds = await context
                .PointEntities.Where(entity => EF.Functions.DistanceSphere(entity.Location, berlin) < 500)
                .Select(entity => entity.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var loadedBerlin = await context
                .PointEntities.SingleAsync(entity => entity.Id == 1)
                .ConfigureAwait(false);

            Assert.Equal([1], matchingIds);
            Assert.Equal(4326, loadedBerlin.Location.SRID);
            Assert.Equal(13.4050, loadedBerlin.Location.X, 3);
            Assert.Equal(52.5200, loadedBerlin.Location.Y, 3);
        }
        finally
        {
            await ResetSpatialObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies that the MariaDB 11.8 runtime executes the approved spatial helper baseline.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_spatial_helpers_succeed()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);

        await ResetSpatialObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new SpatialDbContext(CreateMariaDbOptions(connectionString));

            await context
                .Database.ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{PolygonTableName}` (
                         `Id` int NOT NULL,
                         `Region` polygon NOT NULL,
                         CONSTRAINT `PK_{PolygonTableName}` PRIMARY KEY (`Id`)
                     );
                     """)
                .ConfigureAwait(false);

            context.PolygonEntities.AddRange(
                new SpatialPolygonEntity
                {
                    Id = 1,
                    Region = CreatePolygon(13.4040, 52.5190, 13.4060, 52.5210),
                },
                new SpatialPolygonEntity
                {
                    Id = 2,
                    Region = CreatePolygon(2.3510, 48.8550, 2.3530, 48.8570),
                });

            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var searchPolygon = CreatePolygon(13.4045, 52.5195, 13.4055, 52.5205);

            var matchingIds = await context
                .PolygonEntities.Where(entity => EF.Functions.MbrIntersects(entity.Region, searchPolygon))
                .Select(entity => entity.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var loadedRegion = await context
                .PolygonEntities.SingleAsync(entity => entity.Id == 1)
                .ConfigureAwait(false);

            Assert.Equal([1], matchingIds);
            Assert.Equal(4326, loadedRegion.Region.SRID);
            Assert.False(loadedRegion.Region.IsEmpty);
        }
        finally
        {
            await ResetSpatialObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static DbContextOptions<SpatialDbContext> CreateMySqlOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<SpatialDbContext>();

        builder.UseMySql(
            connectionString,
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.UseNetTopologySuite());

        return builder.Options;
    }

    private static DbContextOptions<SpatialDbContext> CreateMariaDbOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<SpatialDbContext>();

        builder.UseMySql(
            connectionString,
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
            options => options.UseNetTopologySuite());

        return builder.Options;
    }

    private static async Task ResetSpatialObjectsAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               DROP TABLE IF EXISTS `{PointTableName}`;
                               DROP TABLE IF EXISTS `{PolygonTableName}`;
                               """;

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static Point CreatePoint(
        double x,
        double y
    )
    {
        return new Point(x, y)
        {
            SRID = 4326,
        };
    }

    private static Polygon CreatePolygon(
        double minX,
        double minY,
        double maxX,
        double maxY
    )
    {
        var polygon = new Polygon(
            new LinearRing(
            [
                new Coordinate(minX, minY),
                new Coordinate(maxX, minY),
                new Coordinate(maxX, maxY),
                new Coordinate(minX, maxY),
                new Coordinate(minX, minY),
            ]))
        {
            SRID = 4326,
        };

        return polygon;
    }

    private sealed class SpatialDbContext : DbContext
    {
        public SpatialDbContext(
            DbContextOptions<SpatialDbContext> options
        ) : base(options) { }

        public DbSet<SpatialPointEntity> PointEntities => Set<SpatialPointEntity>();

        public DbSet<SpatialPolygonEntity> PolygonEntities => Set<SpatialPolygonEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialPointEntity>(entity =>
            {
                entity.ToTable(PointTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Location)
                    .HasColumnType("point");
            });

            modelBuilder.Entity<SpatialPolygonEntity>(entity =>
            {
                entity.ToTable(PolygonTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Region)
                    .HasColumnType("polygon");
            });
        }
    }

    private sealed class SpatialPointEntity
    {
        public int Id { get; set; }

        public Point Location { get; set; } = null!;
    }

    private sealed class SpatialPolygonEntity
    {
        public int Id { get; set; }

        public Polygon Region { get; set; } = null!;
    }
}
