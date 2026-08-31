namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers the optional NetTopologySuite bootstrap contract.
/// </summary>
public sealed class MySqlNetTopologySuiteBootstrapTests
{
    public static TheoryData<Type, string> SupportedGeometryTypes => new()
    {
        { typeof(Geometry), "POINT (1 2)" },
        { typeof(Point), "POINT (1 2)" },
        { typeof(LineString), "LINESTRING (0 0, 1 1)" },
        { typeof(Polygon), "POLYGON ((0 0, 0 1, 1 1, 0 0))" },
        { typeof(GeometryCollection), "GEOMETRYCOLLECTION (POINT (1 2))" },
        { typeof(MultiPoint), "MULTIPOINT ((0 0), (1 1))" },
        { typeof(MultiLineString), "MULTILINESTRING ((0 0, 1 1))" },
        { typeof(MultiPolygon), "MULTIPOLYGON (((0 0, 0 1, 1 1, 0 0)))" },
    };

    /// <summary>
    /// Verifies that the approved spatial seam adds its own options extension.
    /// </summary>
    [Fact]
    public void UseNetTopologySuite_adds_the_optional_spatial_extension()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.UseNetTopologySuite());

        var extension = builder.Options.FindExtension<MySqlNetTopologySuiteOptionsExtension>();

        Assert.NotNull(extension);
    }

    /// <summary>
    /// Verifies that the spatial seam preserves the same builder instance for chaining.
    /// </summary>
    [Fact]
    public void UseNetTopologySuite_returns_the_same_builder_instance()
    {
        var builder = new DbContextOptionsBuilder();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        MySqlDbContextOptionsBuilder? returnedBuilder = null;

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => returnedBuilder = options.UseNetTopologySuite());

        Assert.NotNull(returnedBuilder);
    }

    /// <summary>
    /// Verifies that the main provider assembly stays free of direct NetTopologySuite references.
    /// </summary>
    [Fact]
    public void Main_provider_assembly_does_not_reference_nettopologysuite()
    {
        var referencedAssemblies = typeof(MySqlDbContextOptionsBuilder).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            referencedAssemblies,
            assemblyName => string.Equals(assemblyName.Name, "NetTopologySuite", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that every supported spatial mapping opts into model-side
    /// design-time values instead of leaking MySqlGeometry into saved models.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedGeometryTypes))]
    public void Supported_spatial_mappings_declare_provider_owned_value_conversion(
        Type geometryType,
        string wkt
    )
    {
        var plugin = new MySqlNetTopologySuiteTypeMappingSourcePlugin(
            [new MySqlSingletonOptions()]);
        var mapping = plugin.FindMapping(new RelationalTypeMappingInfo(geometryType));
        var geometry = new WKTReader().Read(wkt);
        geometry.SRID = 4326;
        var providerValue = MySqlGeometry.FromWkb(geometry.SRID, new WKBWriter().Write(geometry));
        var providerOwnedMapping = Assert.IsAssignableFrom<IMySqlProviderOwnedModelTypeMapping>(mapping);
        var modelValue = providerOwnedMapping.ConvertToModelValue(providerValue);

        Assert.Equal(geometryType, mapping?.ClrType);
        Assert.Equal(typeof(MySqlGeometry), providerOwnedMapping.ProviderClrType);
        Assert.True(geometryType.IsInstanceOfType(modelValue));
        Assert.Equal(4326, Assert.IsAssignableFrom<Geometry>(modelValue).SRID);
    }

    /// <summary>
    /// Pins the engine-specific constructor syntax that preserves X as longitude
    /// and Y as latitude for geographic MySQL spatial reference systems.
    /// </summary>
    [Theory]
    [InlineData(false, "ST_GeomFromText('POINT (13.405 52.52)', 4326, 'axis-order=long-lat')")]
    [InlineData(true, "ST_GeomFromText('POINT (13.405 52.52)', 4326)")]
    public void Spatial_sql_literals_preserve_the_model_coordinate_order(
        bool mariaDb,
        string expectedSql
    )
    {
        var serverVersion = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 4, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));
        var options = new DbContextOptionsBuilder<SpatialLiteralContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                serverVersion,
                mySqlOptions => mySqlOptions.UseNetTopologySuite())
            .Options;

        using var context = new SpatialLiteralContext(options);
        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(Point));
        var point = new Point(13.405, 52.52)
        {
            SRID = 4326,
        };

        Assert.Equal(expectedSql, mapping?.GenerateSqlLiteral(point));
    }

    private sealed class SpatialLiteralContext(
        DbContextOptions<SpatialLiteralContext> options
    ) : DbContext(options);
}
