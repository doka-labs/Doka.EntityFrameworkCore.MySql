using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

var database = ExampleDatabaseConfiguration.Create("doka_example_spatial_queries");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<SpatialContext>()
    .UseMySql(
        database.ConnectionString,
        database.ServerVersion,
        provider => provider.UseNetTopologySuite())
    .Options;

await using var context = new SpatialContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);

    context.Places.AddRange(
        new Place { Name = "Berlin", Location = Point(13.4050, 52.5200) },
        new Place { Name = "Potsdam", Location = Point(13.0645, 52.3906) },
        new Place { Name = "Hamburg", Location = Point(9.9937, 53.5511) });
    await context.SaveChangesAsync(cancellationToken);

    var berlin = Point(13.4050, 52.5200);
    // Rider's generic EF inspection does not know this provider extension.
    // The provider translates DistanceSphere to ST_DISTANCE_SPHERE on both
    // supported engine families; the functional and live suites cover it.
    // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
    var nearby = await context.Places
        .AsNoTracking()
        .Where(place => EF.Functions.DistanceSphere(place.Location, berlin) < 50_000d)
        .OrderBy(place => place.Name)
        .Select(place => place.Name)
        .ToListAsync(cancellationToken);

    if (nearby is not ["Berlin", "Potsdam"])
    {
        throw new InvalidOperationException("The spherical-distance query returned unexpected places.");
    }

    Console.WriteLine($"{database.Target}: within 50 km of Berlin: {string.Join(", ", nearby)}");
}
finally
{
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

static Point Point(
    double longitude,
    double latitude
) => new(longitude, latitude) { SRID = 4326 };

internal sealed class SpatialContext : DbContext
{
    public SpatialContext(
        DbContextOptions<SpatialContext> options
    ) : base(options) { }

    public DbSet<Place> Places => Set<Place>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Place>(entity =>
        {
            entity.ToTable("Places");
            entity.HasKey(place => place.Id);
            entity.Property(place => place.Name).HasMaxLength(200);
            entity.Property(place => place.Location)
                .HasColumnType("point")
                .HasSrid(4326);
            entity.HasIndex(place => place.Location).IsSpatial();
        });
    }
}

internal sealed class Place
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Point Location { get; set; } = null!;
}
