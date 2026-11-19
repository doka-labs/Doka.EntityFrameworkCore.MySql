namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies retained physical text evidence without changing design-time migration intent.</summary>
public sealed class MySqlRuntimeColumnTextFacetsTests
{
    /// <summary>Provider-text storage retains explicit collation or its known absence regardless of CLR type.</summary>
    [Theory]
    [InlineData(nameof(FacetRow.First), "utf8mb4_unicode_ci")]
    [InlineData(nameof(FacetRow.Second), "")]
    [InlineData(nameof(FacetRow.Token), "")]
    [InlineData(nameof(FacetRow.State), "")]
    public void Runtime_property_retains_original_column_collation_evidence(
        string name,
        string expected
    )
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var runtime = context.Model.FindEntityType(typeof(FacetRow))!.FindProperty(name)!;
        var design = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(FacetRow))!.FindProperty(name)!;

        // Assert
        Assert.Equal(expected,
            runtime.FindAnnotation(MySqlRuntimeColumnTextFacets.RuntimeCollation)
                ?.Value);

        Assert.Null(runtime.FindAnnotation(RelationalAnnotationNames.Collation));
        Assert.Null(design.FindAnnotation(MySqlRuntimeColumnTextFacets.RuntimeCollation));
        Assert.Equal(expected.Length == 0
                ? null
                : expected,
            design.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value);
    }

    /// <summary>Native UUID and binary storage do not acquire a misleading character-column receipt.</summary>
    [Theory]
    [InlineData(nameof(FacetRow.NativeToken))]
    [InlineData(nameof(FacetRow.BinaryToken))]
    public void Non_character_storage_does_not_receive_text_evidence(
        string name
    )
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var property = context.Model.FindEntityType(typeof(FacetRow))!.FindProperty(name)!;

        // Assert
        Assert.Null(property.FindAnnotation(MySqlRuntimeColumnTextFacets.RuntimeCollation));
    }

    /// <summary>Different columns inherit the same actual model facets while a conflicting explicit column stays incompatible.</summary>
    [Theory]
    [InlineData(nameof(FacetRow.Second), true)]
    [InlineData(nameof(FacetRow.Latin), false)]
    public void Different_columns_require_compatible_retained_physical_facets(
        string second,
        bool expected
    )
    {
        // Arrange
        using var context = CreateContext();
        var table = context
            .Model
            .GetRelationalModel()
            .FindTable("FacetRows", null)!;

        var first = table.FindColumn(nameof(FacetRow.First))!;

        // Act
        var compatible = MySqlRuntimeColumnTextFacets.AreCompatible(first, table.FindColumn(second)!);

        // Assert
        Assert.Equal(expected, compatible);
        Assert.True(MySqlRuntimeColumnTextFacets.AreCompatible(first, first));
    }

    /// <summary>Known absence on the same actual table proves inheritance without guessing its unconfigured native default.</summary>
    [Fact]
    public void Same_table_known_absence_does_not_require_an_invented_default_name()
    {
        // Arrange
        using var context = CreateContext(configureModelDefaults: false);
        var table = context
            .Model
            .GetRelationalModel()
            .FindTable("FacetRows", null)!;

        var first = table.FindColumn(nameof(FacetRow.Second))!;
        var second = table.FindColumn(nameof(FacetRow.Third))!;

        // Act
        var compatible = MySqlRuntimeColumnTextFacets.AreCompatible(first, second);

        // Assert
        Assert.True(compatible);
        Assert.Null(MySqlTextDefaults.GetModelFacets(context.Model)
            .Collation);

        Assert.Null(MySqlTextDefaults.GetModelFacets(context.Model)
            .CharSet);
    }

    private static FacetContext CreateContext(
        bool configureModelDefaults = true
    )
    {
        var options = new DbContextOptionsBuilder<FacetContext>()
            .UseMySql("Server=localhost;Database=unused;User ID=unused;",
                MySqlServerVersion.MariaDb(new Version(12, 3, 3)))
            .EnableServiceProviderCaching(false)
            .Options;

        return new FacetContext(options, configureModelDefaults);
    }

    private sealed class FacetContext : DbContext
    {
        private readonly bool _configureModelDefaults;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public FacetContext(
            DbContextOptions<FacetContext> options,
            bool configureModelDefaults
        ) : base(options)
        {
            _configureModelDefaults = configureModelDefaults;
        }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            if (_configureModelDefaults)
            {
                modelBuilder.HasCharSet("utf8mb4");
                modelBuilder.UseCollation("utf8mb4_unicode_ci");
            }

            var row = modelBuilder.Entity<FacetRow>();
            row.ToTable("FacetRows");
            row
                .Property(value => value.First)
                .HasMaxLength(80)
                .UseCollation("utf8mb4_unicode_ci");

            row
                .Property(value => value.Second)
                .HasMaxLength(80);

            row
                .Property(value => value.Third)
                .HasMaxLength(80);

            row
                .Property(value => value.Latin)
                .HasMaxLength(80)
                .UseCollation("latin1_swedish_ci");

            row
                .Property(value => value.Token)
                .HasColumnType("char(36)");

            row
                .Property(value => value.State)
                .HasConversion<string>()
                .HasMaxLength(16);

            row
                .Property(value => value.NativeToken)
                .HasColumnType("uuid");

            row
                .Property(value => value.BinaryToken)
                .HasColumnType("binary(16)");
        }
    }

    private sealed class FacetRow
    {
        public int Id { get; set; }
        public string First { get; set; } = string.Empty;
        public string Second { get; set; } = string.Empty;
        public string Third { get; set; } = string.Empty;
        public string Latin { get; set; } = string.Empty;
        public Guid Token { get; set; }
        public Guid NativeToken { get; set; }
        public Guid BinaryToken { get; set; }
        public DayOfWeek State { get; set; }
    }
}
