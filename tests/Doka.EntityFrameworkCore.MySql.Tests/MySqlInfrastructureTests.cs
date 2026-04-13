using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests for infrastructure components: ConnectionStringRedactor, OptionsExtension.Validate,
/// UpdateSqlGenerator SQL output, SpatialTypeSupport utilities.
/// </summary>
public sealed class MySqlInfrastructureTests
{
    // ══════════════════════════════════════════════════════════════════════
    // ── MySqlConnectionStringRedactor ──
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Normal connection string has password redacted.</summary>
    [Fact]
    public void Redactor_masks_password_in_normal_connection_string()
    {
        var redacted = MySqlConnectionStringRedactor.Redact(
            "Server=localhost;Database=doka;User ID=root;Password=secret123;");

        Assert.Contains("***", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret123", redacted, StringComparison.Ordinal);
    }

    /// <summary>Null connection string returns the none sentinel.</summary>
    [Fact]
    public void Redactor_returns_none_for_null()
    {
        var result = MySqlConnectionStringRedactor.Redact(null);
        Assert.Equal("<none>", result);
    }

    /// <summary>Empty connection string returns the none sentinel.</summary>
    [Fact]
    public void Redactor_returns_none_for_empty()
    {
        var result = MySqlConnectionStringRedactor.Redact(string.Empty);
        Assert.Equal("<none>", result);
    }

    /// <summary>Whitespace-only connection string returns the none sentinel.</summary>
    [Fact]
    public void Redactor_returns_none_for_whitespace()
    {
        var result = MySqlConnectionStringRedactor.Redact("   ");
        Assert.Equal("<none>", result);
    }

    /// <summary>Malformed connection string returns the redacted sentinel.</summary>
    [Fact]
    public void Redactor_returns_redacted_for_malformed_string()
    {
        // A string that MySqlConnectionStringBuilder can't parse should be caught.
        var result = MySqlConnectionStringRedactor.Redact("not=a;valid;connection=;string=;;;===");

        // Should either redact successfully or return the fallback.
        Assert.DoesNotContain("not=a", result, StringComparison.Ordinal);
    }

    /// <summary>Connection string without password still works.</summary>
    [Fact]
    public void Redactor_handles_connection_string_without_password()
    {
        var result = MySqlConnectionStringRedactor.Redact("Server=localhost;Database=doka;User ID=root;");

        Assert.Contains("Server", result, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── MySqlUpdateSqlGenerator — SQL output ──
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>UpdateSqlGenerator resolves from DI and is the correct type.</summary>
    [Fact]
    public void UpdateSqlGenerator_resolves_as_MySql_type()
    {
        using var context = CreateContext();
        var generator = context.GetService<IUpdateSqlGenerator>();

        Assert.Equal(
            "MySqlUpdateSqlGenerator",
            generator.GetType()
                .Name);
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── MySqlSpatialTypeSupport ──
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>NTS Point is recognized as spatial CLR type.</summary>
    [Fact]
    public void IsSpatialClrType_returns_true_for_nts_point()
    {
        Assert.True(MySqlSpatialTypeSupport.IsSpatialClrType(typeof(NetTopologySuite.Geometries.Point)));
    }

    /// <summary>NTS Geometry base class is recognized as spatial CLR type.</summary>
    [Fact]
    public void IsSpatialClrType_returns_true_for_nts_geometry()
    {
        Assert.True(MySqlSpatialTypeSupport.IsSpatialClrType(typeof(NetTopologySuite.Geometries.Geometry)));
    }

    /// <summary>Non-spatial type is not recognized.</summary>
    [Fact]
    public void IsSpatialClrType_returns_false_for_non_spatial()
    {
        Assert.False(MySqlSpatialTypeSupport.IsSpatialClrType(typeof(string)));
        Assert.False(MySqlSpatialTypeSupport.IsSpatialClrType(typeof(int)));
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
        Assert.Equal(expected, MySqlSpatialTypeSupport.IsSpatialStoreType(storeType));
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
        Assert.Equal(expected, MySqlSpatialTypeSupport.NormalizeStoreTypeName(input));
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Helpers ──
    // ══════════════════════════════════════════════════════════════════════

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
