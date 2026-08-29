using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests that JSON CLR types (JsonElement, JsonDocument, JsonNode, JsonObject, JsonArray)
/// preserve their native type through the EF Core pipeline with correct ValueConverters
/// and ValueComparers.
/// </summary>
public sealed class MySqlJsonTypeMappingTests
{
    // -- ValueConverter Round-Trip Tests --

    /// <summary>
    /// JsonElement property preserves json column type, CLR type, and has an embedded converter.
    /// </summary>
    [Fact]
    public void JsonElement_property_has_value_converter_and_json_column_type()
    {
        using var context = CreateContext<JsonElementContext>();
        var property =
            context.Model.FindEntityType(typeof(JsonElementEntity))!.FindProperty(nameof(JsonElementEntity.Data))!;

        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonElement), property.ClrType);

        var typeMapping = property.GetRelationalTypeMapping();

        Assert.NotNull(typeMapping.Converter);
        Assert.Equal(typeof(JsonElement), typeMapping.Converter.ModelClrType);
        Assert.Equal(typeof(string), typeMapping.Converter.ProviderClrType);
    }

    /// <summary>
    /// JsonDocument property preserves json column type, CLR type, and has an embedded converter.
    /// </summary>
    [Fact]
    public void JsonDocument_property_has_value_converter_and_json_column_type()
    {
        using var context = CreateContext<JsonDocumentContext>();
        var property =
            context.Model.FindEntityType(typeof(JsonDocumentEntity))!.FindProperty(nameof(JsonDocumentEntity.Data))!;

        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonDocument), property.ClrType);

        var typeMapping = property.GetRelationalTypeMapping();

        Assert.NotNull(typeMapping.Converter);
        Assert.Equal(typeof(JsonDocument), typeMapping.Converter.ModelClrType);
    }

    /// <summary>
    /// JsonNode property preserves json column type, CLR type, and has an embedded converter.
    /// </summary>
    [Fact]
    public void JsonNode_property_has_value_converter_and_json_column_type()
    {
        using var context = CreateContext<JsonNodeContext>();
        var property = context.Model.FindEntityType(typeof(JsonNodeEntity))!.FindProperty(nameof(JsonNodeEntity.Data))!;

        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonNode), property.ClrType);

        var typeMapping = property.GetRelationalTypeMapping();

        Assert.NotNull(typeMapping.Converter);
        Assert.Equal(typeof(JsonNode), typeMapping.Converter.ModelClrType);
    }

    /// <summary>
    /// JsonObject property preserves json column type, CLR type, and has an embedded converter.
    /// </summary>
    [Fact]
    public void JsonObject_property_has_value_converter_and_json_column_type()
    {
        using var context = CreateContext<JsonObjectContext>();
        var property =
            context.Model.FindEntityType(typeof(JsonObjectEntity))!.FindProperty(nameof(JsonObjectEntity.Data))!;

        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonObject), property.ClrType);

        var typeMapping = property.GetRelationalTypeMapping();

        Assert.NotNull(typeMapping.Converter);
        Assert.Equal(typeof(JsonObject), typeMapping.Converter.ModelClrType);
    }

    /// <summary>
    /// JsonArray property preserves json column type, CLR type, and has an embedded converter.
    /// </summary>
    [Fact]
    public void JsonArray_property_has_value_converter_and_json_column_type()
    {
        using var context = CreateContext<JsonArrayContext>();
        var property =
            context.Model.FindEntityType(typeof(JsonArrayEntity))!.FindProperty(nameof(JsonArrayEntity.Data))!;

        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonArray), property.ClrType);

        var typeMapping = property.GetRelationalTypeMapping();

        Assert.NotNull(typeMapping.Converter);
        Assert.Equal(typeof(JsonArray), typeMapping.Converter.ModelClrType);
    }

    // -- ValueConverter Serialization/Deserialization Tests --

    /// <summary>
    /// JsonElement ValueConverter round-trips through string correctly.
    /// </summary>
    [Fact]
    public void JsonElement_converter_round_trips_through_string()
    {
        using var context = CreateContext<JsonElementContext>();
        var converter =
            context.Model.FindEntityType(typeof(JsonElementEntity))!.FindProperty(nameof(JsonElementEntity.Data))!
                .GetRelationalTypeMapping()
                .Converter!;

        var original = JsonElement.Parse("""{"key":"value","num":42}""");

        var serialized = (string)converter.ConvertToProvider(original)!;
        var deserialized = (JsonElement)converter.ConvertFromProvider(serialized)!;

        Assert.Equal(original.GetRawText(), deserialized.GetRawText());
    }

    /// <summary>
    /// JsonNode ValueConverter round-trips through string correctly.
    /// </summary>
    [Fact]
    public void JsonNode_converter_round_trips_through_string()
    {
        using var context = CreateContext<JsonNodeContext>();
        var converter = context.Model.FindEntityType(typeof(JsonNodeEntity))!.FindProperty(nameof(JsonNodeEntity.Data))!
            .GetRelationalTypeMapping()
            .Converter!;

        var original = JsonNode.Parse("""{"nested":{"a":1},"arr":[1,2,3]}""");
        var serialized = (string)converter.ConvertToProvider(original)!;
        var deserialized = (JsonNode?)converter.ConvertFromProvider(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(original!.ToJsonString(), deserialized.ToJsonString());
    }

    // -- ValueComparer Deep-Equality Tests --

    /// <summary>
    /// JsonElement ValueComparer detects content equality, not reference equality.
    /// </summary>
    [Fact]
    public void JsonElement_comparer_uses_content_equality()
    {
        var a = JsonElement.Parse("""{"x":1}""");
        var b = JsonElement.Parse("""{"x":1}""");
        var c = JsonElement.Parse("""{"x":2}""");

        var comparer = MySqlJsonValueComparers.JsonElementComparer;

        Assert.True(comparer.Equals(a, b));
        Assert.False(comparer.Equals(a, c));
        Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
    }

    /// <summary>
    /// JsonNode ValueComparer detects content equality, not reference equality.
    /// </summary>
    [Fact]
    public void JsonNode_comparer_uses_content_equality()
    {
        var a = JsonNode.Parse("""{"x":1}""");
        var b = JsonNode.Parse("""{"x":1}""");
        var c = JsonNode.Parse("""{"x":2}""");

        var comparer = MySqlJsonValueComparers.JsonNodeComparer;

        Assert.True(comparer.Equals(a, b));
        Assert.False(comparer.Equals(a, c));
        Assert.Equal(comparer.GetHashCode(a!), comparer.GetHashCode(b!));
    }

    [Fact]
    public void JsonNode_change_tracking_uses_structural_snapshot_equality()
    {
        using var context = CreateContext<JsonNodeContext>();
        var entity = new JsonNodeEntity
        {
            Id = 1,
            Data = JsonNode.Parse("""{"id":1,"nested":{"value":1}}"""),
        };

        var entry = context.Attach(entity);

        entity.Data = JsonNode.Parse("""{"nested":{"value":1},"id":1}""");
        context.ChangeTracker.DetectChanges();

        Assert.False(
            entry.Property(candidate => candidate.Data)
                .IsModified);

        entity.Data!["nested"]!["value"] = 2;
        context.ChangeTracker.DetectChanges();

        Assert.True(
            entry.Property(candidate => candidate.Data)
                .IsModified);
    }

    [Fact]
    public void JsonObject_change_tracking_uses_structural_snapshot_equality()
    {
        using var context = CreateContext<JsonObjectContext>();
        var entity = new JsonObjectEntity
        {
            Id = 1,
            Data = JsonNode.Parse("""{"id":1,"nested":{"value":1}}""")!.AsObject(),
        };
        var entry = context.Attach(entity);

        entity.Data = JsonNode.Parse("""{"nested":{"value":1},"id":1}""")!.AsObject();
        context.ChangeTracker.DetectChanges();

        Assert.False(entry.Property(candidate => candidate.Data).IsModified);

        entity.Data["nested"]!["value"] = 2;
        context.ChangeTracker.DetectChanges();

        Assert.True(entry.Property(candidate => candidate.Data).IsModified);
    }

    [Fact]
    public void JsonArray_change_tracking_uses_structural_snapshot_equality()
    {
        using var context = CreateContext<JsonArrayContext>();
        var entity = new JsonArrayEntity
        {
            Id = 1,
            Data = JsonNode.Parse("""[1,{"value":1},3]""")!.AsArray(),
        };
        var entry = context.Attach(entity);

        entity.Data = JsonNode.Parse("""[1,{"value":1},3]""")!.AsArray();
        context.ChangeTracker.DetectChanges();

        Assert.False(entry.Property(candidate => candidate.Data).IsModified);

        entity.Data[1]!["value"] = 2;
        context.ChangeTracker.DetectChanges();

        Assert.True(entry.Property(candidate => candidate.Data).IsModified);
    }

    /// <summary>
    /// JsonDocument ValueComparer handles null correctly.
    /// </summary>
    [Fact]
    public void JsonDocument_comparer_handles_null()
    {
        var comparer = MySqlJsonValueComparers.JsonDocumentComparer;

        Assert.True(comparer.Equals(null, null));
        Assert.False(comparer.Equals(null, JsonDocument.Parse("{}")));
        Assert.False(comparer.Equals(JsonDocument.Parse("{}"), null));
    }

    /// <summary>
    /// JsonElement type mapping has a ValueComparer for deep-equality comparison.
    /// </summary>
    [Fact]
    public void JsonElement_type_mapping_has_value_comparer()
    {
        using var context = CreateContext<JsonElementContext>();
        var typeMapping =
            context.Model.FindEntityType(typeof(JsonElementEntity))!.FindProperty(nameof(JsonElementEntity.Data))!
                .GetRelationalTypeMapping();

        Assert.NotNull(typeMapping.Comparer);
        Assert.Equal(typeof(JsonElement), typeMapping.Comparer.Type);
    }

    /// <summary>
    /// JsonNode type mapping has a ValueComparer for deep-equality comparison.
    /// </summary>
    [Fact]
    public void JsonNode_type_mapping_has_value_comparer()
    {
        using var context = CreateContext<JsonNodeContext>();
        var typeMapping =
            context.Model.FindEntityType(typeof(JsonNodeEntity))!.FindProperty(nameof(JsonNodeEntity.Data))!
                .GetRelationalTypeMapping();

        Assert.NotNull(typeMapping.Comparer);
        Assert.Equal(typeof(JsonNode), typeMapping.Comparer.Type);
    }

    // -- string property still works with json store type --

    /// <summary>
    /// A string property with json column type still works without a converter.
    /// </summary>
    [Fact]
    public void String_property_with_json_column_type_has_no_converter()
    {
        using var context = CreateContext<JsonStringContext>();
        var property =
            context.Model.FindEntityType(typeof(JsonStringEntity))!.FindProperty(nameof(JsonStringEntity.Data))!;

        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(string), property.ClrType);
    }

    // -- Helpers --

    private static TContext CreateContext<TContext>()
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
    }

    // -- Entity + Context definitions --

    private sealed class JsonElementEntity
    {
        public int Id { get; set; }
        public JsonElement Data { get; set; }
    }

    private sealed class JsonElementContext : DbContext
    {
        public JsonElementContext(
            DbContextOptions<JsonElementContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonElementEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonDocumentEntity
    {
        public int Id { get; set; }
        public JsonDocument? Data { get; set; }
    }

    private sealed class JsonDocumentContext : DbContext
    {
        public JsonDocumentContext(
            DbContextOptions<JsonDocumentContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonDocumentEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonNodeEntity
    {
        public int Id { get; set; }
        public JsonNode? Data { get; set; }
    }

    private sealed class JsonNodeContext : DbContext
    {
        public JsonNodeContext(
            DbContextOptions<JsonNodeContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonNodeEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonObjectEntity
    {
        public int Id { get; set; }
        public JsonObject? Data { get; set; }
    }

    private sealed class JsonObjectContext : DbContext
    {
        public JsonObjectContext(
            DbContextOptions<JsonObjectContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonObjectEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonArrayEntity
    {
        public int Id { get; set; }
        public JsonArray? Data { get; set; }
    }

    private sealed class JsonArrayContext : DbContext
    {
        public JsonArrayContext(
            DbContextOptions<JsonArrayContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonArrayEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonStringEntity
    {
        public int Id { get; set; }
        public string Data { get; set; } = "{}";
    }

    private sealed class JsonStringContext : DbContext
    {
        public JsonStringContext(
            DbContextOptions<JsonStringContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonStringEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }
}
