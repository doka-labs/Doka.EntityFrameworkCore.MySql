namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies low-priority model fallbacks and supplied-model isolation without database I/O.</summary>
public sealed partial class MySqlDefaultTextModelTests
{
    private const string ConnectionString = "Server=localhost;Database=unused;User ID=root;Password=unused;";

    /// <summary>Each optional facet is applied without inventing the other facet.</summary>
    /// <param name="charSet">The optional application charset.</param>
    /// <param name="collation">The optional application collation.</param>
    [Theory]
    [InlineData("utf8mb4", "utf8mb4_unicode_ci")]
    [InlineData("utf8mb4", null)]
    [InlineData(null, "utf8mb4_unicode_ci")]
    [InlineData(null, null)]
    public void Options_populate_only_the_missing_canonical_facets(
        string? charSet,
        string? collation
    )
    {
        // Arrange
        using var context = CreateContext(charSet, collation);

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        // Assert
        Assert.Equal(charSet, model.GetMySqlCharSet());
        Assert.Equal(collation, model.GetCollation());
        Assert.DoesNotContain(model.GetAnnotations(),
            annotation => annotation.Name.StartsWith(MySqlAnnotationNames.Prefix + "Runtime",
                StringComparison.Ordinal));

        Assert.Null(model.FindEntityType(typeof(TextRow))!.FindProperty(nameof(TextRow.Name))!.GetCollation());
    }

    /// <summary>Stronger model facets win, including when the opposite fallback is incompatible.</summary>
    /// <param name="explicitCharSet">The explicit model charset.</param>
    /// <param name="explicitCollation">The explicit model collation.</param>
    /// <param name="expectedCharSet">The resulting canonical charset.</param>
    /// <param name="expectedCollation">The resulting canonical collation.</param>
    [Theory]
    [InlineData("latin1", null, "latin1", null)]
    [InlineData(null, "latin1_swedish_ci", null, "latin1_swedish_ci")]
    [InlineData("latin1", "latin1_swedish_ci", "latin1", "latin1_swedish_ci")]
    [InlineData("utf8mb4", null, "utf8mb4", "utf8mb4_unicode_ci")]
    [InlineData(null, "utf8mb4_bin", "utf8mb4", "utf8mb4_bin")]
    public void Explicit_facets_retain_precedence_and_compatible_fallbacks(
        string? explicitCharSet,
        string? explicitCollation,
        string? expectedCharSet,
        string? expectedCollation
    )
    {
        // Arrange
        using var context = CreateContext("utf8mb4", "utf8mb4_unicode_ci", explicitCharSet, explicitCollation);

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        // Assert
        Assert.Equal(expectedCharSet, model.GetMySqlCharSet());
        Assert.Equal(expectedCollation, model.GetCollation());
    }

    /// <summary>Invalid explicit model facets cannot be hidden by a valid option pair.</summary>
    [Fact]
    public void Incompatible_explicit_facets_are_not_repaired_by_defaults()
    {
        // Arrange
        using var context = CreateContext("utf8mb4", "utf8mb4_unicode_ci", "latin1", "utf8mb4_bin");

        // Act
        var exception = Record.Exception(() => context.Model);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Table and column overrides remain local rather than being overwritten by the fallback.</summary>
    [Fact]
    public void Table_column_and_special_type_contracts_remain_local()
    {
        // Arrange
        using var context = CreateContext("utf8mb4", "utf8mb4_unicode_ci");

        // Act
        var script = context.Database.GenerateCreateScript();

        // Assert
        Assert.Contains("utf8mb4_unicode_ci", script, StringComparison.Ordinal);
        Assert.Contains("latin1_swedish_ci", script, StringComparison.Ordinal);
        Assert.Contains("utf8mb4_bin", script, StringComparison.Ordinal);
        Assert.Contains("JSON_VALID", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("binary(16)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SET NAMES", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONVERT TO", script, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Matching supplied models retain their metadata without rerunning model configuration.</summary>
    /// <param name="runtime">Whether to supply the runtime representation instead of the design model.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Matching_supplied_model_is_not_rebuilt_or_mutated(
        bool runtime
    )
    {
        // Arrange
        using var source = CreateContext("utf8mb4", "utf8mb4_unicode_ci");
        var model = runtime
            ? source.Model
            : source.GetService<IDesignTimeModel>()
                .Model;

        var annotations = model
            .GetAnnotations()
            .Select(annotation => (annotation.Name, annotation.Value))
            .ToArray();

        using var target = CreateContext("utf8mb4", "utf8mb4_unicode_ci", supplied: model);

        // Act
        var actual = target.Model;

        // Assert
        Assert.Equal(0, target.ModelBuilds);
        Assert.Equal(annotations,
            model
                .GetAnnotations()
                .Select(annotation => (annotation.Name, annotation.Value))
                .ToArray());

        Assert.NotNull(actual.FindEntityType(typeof(TextRow)));
    }

    /// <summary>A supplied model cannot silently keep a different fallback after an option change.</summary>
    /// <param name="runtime">Whether to supply the runtime representation instead of the design model.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supplied_model_with_another_fallback_is_rejected(
        bool runtime
    )
    {
        // Arrange
        using var source = CreateContext("utf8mb4", "utf8mb4_unicode_ci");
        var model = runtime
            ? source.Model
            : source.GetService<IDesignTimeModel>()
                .Model;

        void act()
        {
            using var target = CreateContext("utf8mb4", "utf8mb4_bin", supplied: model);

            _ = target.Model;
        }

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>A fixed model override remains stronger even when another runtime fallback is configured.</summary>
    [Fact]
    public void Explicit_supplied_model_facets_keep_priority_over_new_options()
    {
        // Arrange
        using var source = CreateContext("utf8mb4", "utf8mb4_unicode_ci", "latin1", "latin1_swedish_ci");
        var model = source.Model;
        using var target = CreateContext("utf8mb4", "utf8mb4_bin", supplied: model);

        // Act
        var actual = target.Model;

        // Assert
        Assert.Equal(0, target.ModelBuilds);
        Assert.NotNull(actual.FindEntityType(typeof(TextRow)));
    }

    private static TextContext CreateContext(
        string? charSet,
        string? collation,
        string? explicitCharSet = null,
        string? explicitCollation = null,
        IModel? supplied = null
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TextContext>();
        builder.UseMySql(ConnectionString,
            MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
            options => options
                .DefaultCharSet(charSet)
                .DefaultCollation(collation));

        if (supplied is not null)
        {
            builder.UseModel(supplied);
        }

        return new TextContext(builder.Options, explicitCharSet, explicitCollation);
    }

    /// <summary>Provides an accessible context type for generated compiled-model source.</summary>
    public sealed class TextContext : DbContext
    {
        private readonly string? _charSet;
        private readonly string? _collation;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        /// <param name="options">The provider options.</param>
        /// <param name="charSet">An optional stronger model charset.</param>
        /// <param name="collation">An optional stronger model collation.</param>
        public TextContext(
            DbContextOptions<TextContext> options,
            string? charSet,
            string? collation
        ) : base(options)
        {
            _charSet = charSet;
            _collation = collation;
        }

        /// <summary>Counts model callbacks to detect unintended supplied-model rebuilding.</summary>
        public int ModelBuilds { get; private set; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ModelBuilds++;
            modelBuilder.Entity<TextRow>(entity =>
            {
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Name)
                    .HasMaxLength(80);

                entity
                    .Property(row => row.Overridden)
                    .HasMaxLength(80)
                    .UseCollation("utf8mb4_bin");

                entity
                    .Property(row => row.Payload)
                    .HasColumnType("json");
            });

            modelBuilder
                .Entity<LatinRow>()
                .HasCharSet("latin1")
                .HasAnnotation(RelationalAnnotationNames.Collation, "latin1_swedish_ci");

            if (_charSet is not null)
            {
                modelBuilder.HasCharSet(_charSet);
            }

            if (_collation is not null)
            {
                modelBuilder.UseCollation(_collation);
            }
        }
    }

    /// <summary>Provides ordinary text, explicit text, JSON and GUID controls.</summary>
    public sealed class TextRow
    {
        /// <summary>Gets or sets the binary GUID key.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets ordinary inherited text.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets explicitly collated text.</summary>
        public string Overridden { get; set; } = string.Empty;

        /// <summary>Gets or sets native JSON text.</summary>
        public string Payload { get; set; } = "{}";
    }

    /// <summary>Provides an independent table-level charset and collation override.</summary>
    public sealed class LatinRow
    {
        /// <summary>Gets or sets the key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets table-inherited text.</summary>
        public string Name { get; set; } = string.Empty;
    }
}
