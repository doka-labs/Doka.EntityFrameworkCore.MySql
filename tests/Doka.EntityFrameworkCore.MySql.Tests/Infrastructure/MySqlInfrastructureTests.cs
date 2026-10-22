using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests provider update SQL generation and spatial type-support utilities.
/// </summary>
public sealed class MySqlInfrastructureTests
{
    /// <summary>UpdateSqlGenerator resolves from DI and is the correct type.</summary>
    [Fact]
    public void UpdateSqlGenerator_resolves_as_MySql_type()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var generator = context.GetService<IUpdateSqlGenerator>();
        var actual = generator.GetType()
                .Name;

        // Assert
        Assert.Equal(
            "MySqlUpdateSqlGenerator",
            actual);
    }

    /// <summary>NTS Point is recognized as spatial CLR type.</summary>
    [Fact]
    public void IsSpatialClrType_returns_true_for_nts_point()
    {
        // Arrange
        var type = typeof(NetTopologySuite.Geometries.Point);

        // Act
        var actual = MySqlSpatialTypeSupport.IsSpatialClrType(type);

        // Assert
        Assert.True(actual);
    }

    /// <summary>NTS Geometry base class is recognized as spatial CLR type.</summary>
    [Fact]
    public void IsSpatialClrType_returns_true_for_nts_geometry()
    {
        // Arrange
        var type = typeof(NetTopologySuite.Geometries.Geometry);

        // Act
        var actual = MySqlSpatialTypeSupport.IsSpatialClrType(type);

        // Assert
        Assert.True(actual);
    }

    /// <summary>Non-spatial type is not recognized.</summary>
    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int))]
    public void IsSpatialClrType_returns_false_for_non_spatial(
        Type type
    )
    {
        // Arrange: The theory supplies type.

        // Act
        var actual = MySqlSpatialTypeSupport.IsSpatialClrType(type);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Known MySQL spatial store types are recognized.</summary>
    [Theory]
    [InlineData("geometry", true)]
    [InlineData("point", true)]
    [InlineData("linestring", true)]
    [InlineData("polygon", true)]
    [InlineData("geometrycollection", true)]
    [InlineData("multipoint", true)]
    [InlineData("multilinestring", true)]
    [InlineData("multipolygon", true)]
    [InlineData("POINT", true)]
    [InlineData("Geometry", true)]
    [InlineData("varchar", false)]
    [InlineData("int", false)]
    [InlineData("json", false)]
    public void IsSpatialStoreType_identifies_spatial_types(
        string storeType,
        bool expected
    )
    {
        // Arrange: The theory supplies storeType, expected.

        // Act
        var actual = MySqlSpatialTypeSupport.IsSpatialStoreType(storeType);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>NormalizeStoreTypeName strips parentheses and whitespace.</summary>
    [Theory]
    [InlineData("point", "point")]
    [InlineData("POINT", "point")]
    [InlineData("geometry(4326)", "geometry")]
    [InlineData("  polygon  ", "polygon")]
    public void NormalizeStoreTypeName_strips_extra_content(
        string input,
        string expected
    )
    {
        // Arrange: The theory supplies input, expected.

        // Act
        var actual = MySqlSpatialTypeSupport.NormalizeStoreTypeName(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    // ======================================================================
    // -- Helpers --
    // ======================================================================

    private static InfraContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<InfraContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new InfraContext(builder.Options);
    }

    private sealed class InfraEntity
    {
        public int Id { get; set; }
    }

    private sealed class InfraContext : DbContext
    {
        public InfraContext(
            DbContextOptions<InfraContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InfraEntity>(e => { e.HasKey(x => x.Id); });
        }
    }
}
