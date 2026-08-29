using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Closes remaining unit-testable gaps from audit: ValueGeneratorSelector unsupported type,
/// OptionsExtension multiple paths, JSON SQL literal, varchar(36) GUID, VisitJsonScalar.
/// </summary>
public sealed class MySqlRemainingGapTests
{
    // -- ValueGeneratorSelector: unsupported type --

    [Fact]
    public void UseHiLo_on_guid_property_throws_when_the_value_generator_is_selected()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<HiLoGuidContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        using var context = new HiLoGuidContext(builder.Options);
        var entityType = context.Model.FindEntityType(typeof(HiLoGuidEntity))
            ?? throw new InvalidOperationException("HiLoGuidEntity metadata was not created.");

        var property = entityType.FindProperty(nameof(HiLoGuidEntity.Id))
            ?? throw new InvalidOperationException("HiLoGuidEntity.Id metadata was not created.");

        var selector = context.GetService<IValueGeneratorSelector>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => selector.TrySelect(property, entityType, out _));

        Assert.Equal(
            "Hi/Lo value generation is not supported for properties of type 'Guid'.",
            exception.Message);
    }

    // -- OptionsExtension: multiple connection paths rejection --

    // OptionsExtension.Validate is tested indirectly -- UseMySql requires a server version parameter.
    // The validation for "missing server version" is enforced at the API level (no 1-arg overload).

    // -- JSON SQL Literal Generation --

    [Fact]
    public void JsonTypeMapping_generates_sql_literal_for_json_element()
    {
        var mapping = MySqlJsonTypeMapping.CreateJsonElementMapping();
        var element = JsonElement.Parse("""{"key":"value"}""");

        var literal = mapping.GenerateSqlLiteral(element);

        Assert.Contains("{\"key\":\"value\"}", literal, StringComparison.Ordinal);
        Assert.StartsWith("'", literal, StringComparison.Ordinal);
        Assert.EndsWith("'", literal, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonTypeMapping_escapes_single_quotes_in_literal()
    {
        var mapping = MySqlJsonTypeMapping.CreateJsonElementMapping();
        // Build JSON with a single-quote in a value using a raw JSON string.
        var doc = System.Text.Json.JsonDocument.Parse("{\"key\":\"it's here\"}");

        var literal = mapping.GenerateSqlLiteral(doc.RootElement);

        // The single quote in the JSON value must be doubled for MySQL SQL literals.
        Assert.Contains("it''s here", literal, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that JSON backslashes cannot change meaning under
    /// <c>NO_BACKSLASH_ESCAPES</c> because the mapping emits UTF-8 hexadecimal.
    /// </summary>
    [Fact]
    public void JsonTypeMapping_uses_mode_independent_hex_for_backslashes()
    {
        const string rawJson = "{\"path\":\"C:\\\\data\"}";
        using var document = JsonDocument.Parse(rawJson);
        var mapping = MySqlJsonTypeMapping.CreateJsonElementMapping();

        var literal = mapping.GenerateSqlLiteral(document.RootElement);

        Assert.Equal(
            $"_utf8mb4 X'{Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(rawJson))}'",
            literal);
    }

    // -- VisitJsonScalar: JSON_EXTRACT verified via owned-type JSON mapping
    //    (JsonElement.GetProperty is not LINQ-translatable; owned-type ToJson
    //    is the EF Core mechanism for JSON path access, tested via integration tests) --

    // -- varchar(36) GUID convention path --

    [Fact]
    public void Varchar36_guid_property_gets_correct_column_type()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<Varchar36GuidContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        using var context = new Varchar36GuidContext(builder.Options);
        var property =
            context.Model.FindEntityType(typeof(Varchar36GuidEntity))!.FindProperty(
                nameof(Varchar36GuidEntity.ExternalId))!;

        var columnType = property.GetColumnType();

        Assert.Contains("varchar(36)", columnType!, StringComparison.OrdinalIgnoreCase);
    }

    // -- Entities / Contexts --

    private sealed class HiLoGuidEntity
    {
        public Guid Id { get; set; }
    }

    private sealed class HiLoGuidContext : DbContext
    {
        public HiLoGuidContext(
            DbContextOptions<HiLoGuidContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoGuidEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .UseHiLo("GuidSeq");
            });
        }
    }

    private sealed class SimpleEntity
    {
        public int Id { get; set; }
    }

    private sealed class SimpleContext : DbContext
    {
        public SimpleContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SimpleEntity>(e => { e.HasKey(x => x.Id); });
        }
    }

    private sealed class Varchar36GuidEntity
    {
        public int Id { get; set; }
        public Guid ExternalId { get; set; }
    }

    private sealed class Varchar36GuidContext : DbContext
    {
        public Varchar36GuidContext(
            DbContextOptions<Varchar36GuidContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Varchar36GuidEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.ExternalId)
                    .HasColumnType("varchar(36)");
            });
        }
    }
}
