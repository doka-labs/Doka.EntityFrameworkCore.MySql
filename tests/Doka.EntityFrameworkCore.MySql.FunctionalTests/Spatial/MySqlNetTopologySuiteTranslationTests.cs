namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the spatial type-mapping and translation baseline.
/// </summary>
public sealed class MySqlNetTopologySuiteTranslationTests
{
    /// <summary>
    /// Verifies that the supported spatial CLR types resolve to the approved geometry-family store types.
    /// </summary>
    [Fact]
    public void Spatial_clr_types_resolve_to_the_expected_geometry_store_types()
    {
        using var context = new SpatialTranslationContext(CreateOptions());

        var entityType = context.Model.FindEntityType(typeof(SpatialEntity))!;

        Assert.Equal("point", entityType.FindProperty(nameof(SpatialEntity.Location))!.GetColumnType());
        Assert.Equal("linestring", entityType.FindProperty(nameof(SpatialEntity.Route))!.GetColumnType());
        Assert.Equal("polygon", entityType.FindProperty(nameof(SpatialEntity.Region))!.GetColumnType());
        Assert.Equal(
            "geometrycollection",
            entityType.FindProperty(nameof(SpatialEntity.ShapeCollection))!.GetColumnType());
        Assert.Equal("geometry", entityType.FindProperty(nameof(SpatialEntity.Shape))!.GetColumnType());
    }

    /// <summary>
    /// Verifies that the approved spatial members, methods, and helper functions translate server-side.
    /// </summary>
    [Fact]
    public void Spatial_members_methods_and_helpers_translate_server_side()
    {
        using var context = new SpatialTranslationContext(CreateOptions());
        var referencePoint = CreatePoint(13.4050, 52.5200);
        var searchPolygon = CreatePolygon(13.4040, 52.5190, 13.4060, 52.5210);

        var sql = context
            .Entities.Where(entity =>
                entity.Region.Contains(searchPolygon)
                && EF.Functions.MbrIntersects(entity.Region, searchPolygon)
                && EF.Functions.DistanceSphere(entity.Location, referencePoint) < 1000)
            .Select(entity => new
            {
                entity.Location.X,
                entity.Location.Y,
                entity.Location.SRID,
                entity.Region.Area,
                RouteText = entity.Route.AsText(),
                RouteBinary = entity.Route.AsBinary(),
                StartPointText = entity.Route.StartPoint.AsText(),
                EndPointText = entity.Route.EndPoint.AsText(),
                PointCount = entity.Route.NumPoints,
                ExteriorRingText = entity.Region.ExteriorRing.AsText(),
                InteriorCount = entity.Region.NumInteriorRings,
                FirstGeometryText = entity
                    .ShapeCollection.GetGeometryN(0)
                    .AsText(),
                FirstPointText = entity
                    .Route.GetPointN(0)
                    .AsText(),
            })
            .ToQueryString();

        Assert.Contains("ST_Contains(", sql, StringComparison.Ordinal);
        Assert.Contains("MBRIntersects(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Distance_Sphere(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_X(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Y(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_SRID(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Area(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_AsText(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_AsBinary(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_StartPoint(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_EndPoint(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_NumPoints(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_ExteriorRing(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_NumInteriorRing(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_GeometryN(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_PointN(", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that unsupported spatial operations still fail translation explicitly.
    /// </summary>
    [Fact]
    public void Unsupported_spatial_operations_fail_explicitly()
    {
        using var context = new SpatialTranslationContext(CreateOptions());

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Entities.Where(entity => entity.Location.Reverse()
                .IsEmpty)
            .ToQueryString());

        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DbContextOptions<SpatialTranslationContext> CreateOptions()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<SpatialTranslationContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.UseNetTopologySuite());

        return builder.Options;
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

    private sealed class SpatialTranslationContext : DbContext
    {
        public SpatialTranslationContext(
            DbContextOptions<SpatialTranslationContext> options
        ) : base(options) { }

        public DbSet<SpatialEntity> Entities => Set<SpatialEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialEntity>(entity =>
            {
                entity.ToTable("Phase3SpatialEntities");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Location);
                entity.Property(item => item.Route);
                entity.Property(item => item.Region);
                entity.Property(item => item.ShapeCollection);
                entity.Property(item => item.Shape);
            });
        }
    }

    private sealed class SpatialEntity
    {
        public int Id { get; set; }

        public Point Location { get; set; } = default!;

        public LineString Route { get; set; } = default!;

        public Polygon Region { get; set; } = default!;

        public GeometryCollection ShapeCollection { get; set; } = default!;

        public Geometry Shape { get; set; } = default!;
    }
}
