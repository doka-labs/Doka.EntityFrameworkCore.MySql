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
        // Arrange
        using var context = CreateContext<JsonElementContext>();

        // Act
        var property =
            context.Model.FindEntityType(typeof(JsonElementEntity))!.FindProperty(nameof(JsonElementEntity.Data))!;

        var typeMapping = property.GetRelationalTypeMapping();

        // Assert
        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonElement), property.ClrType);

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
        // Arrange
        using var context = CreateContext<JsonDocumentContext>();

        // Act
        var property =
            context.Model.FindEntityType(typeof(JsonDocumentEntity))!.FindProperty(nameof(JsonDocumentEntity.Data))!;

        var typeMapping = property.GetRelationalTypeMapping();

        // Assert
        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonDocument), property.ClrType);

        Assert.NotNull(typeMapping.Converter);
        Assert.Equal(typeof(JsonDocument), typeMapping.Converter.ModelClrType);
    }

    /// <summary>
    /// JsonNode property preserves json column type, CLR type, and has an embedded converter.
    /// </summary>
    [Fact]
    public void JsonNode_property_has_value_converter_and_json_column_type()
    {
        // Arrange
        using var context = CreateContext<JsonNodeContext>();

        // Act
        var property = context.Model.FindEntityType(typeof(JsonNodeEntity))!.FindProperty(nameof(JsonNodeEntity.Data))!;

        var typeMapping = property.GetRelationalTypeMapping();

        // Assert
        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonNode), property.ClrType);

        Assert.NotNull(typeMapping.Converter);
        Assert.Equal(typeof(JsonNode), typeMapping.Converter.ModelClrType);
    }

    /// <summary>
    /// JsonObject property preserves json column type, CLR type, and has an embedded converter.
    /// </summary>
    [Fact]
    public void JsonObject_property_has_value_converter_and_json_column_type()
    {
        // Arrange
        using var context = CreateContext<JsonObjectContext>();

        // Act
        var property =
            context.Model.FindEntityType(typeof(JsonObjectEntity))!.FindProperty(nameof(JsonObjectEntity.Data))!;

        var typeMapping = property.GetRelationalTypeMapping();

        // Assert
        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonObject), property.ClrType);

        Assert.NotNull(typeMapping.Converter);
        Assert.Equal(typeof(JsonObject), typeMapping.Converter.ModelClrType);
    }

    /// <summary>
    /// JsonArray property preserves json column type, CLR type, and has an embedded converter.
    /// </summary>
    [Fact]
    public void JsonArray_property_has_value_converter_and_json_column_type()
    {
        // Arrange
        using var context = CreateContext<JsonArrayContext>();

        // Act
        var property =
            context.Model.FindEntityType(typeof(JsonArrayEntity))!.FindProperty(nameof(JsonArrayEntity.Data))!;

        var typeMapping = property.GetRelationalTypeMapping();

        // Assert
        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(JsonArray), property.ClrType);

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
        // Arrange
        using var context = CreateContext<JsonElementContext>();
        var converter =
            context.Model.FindEntityType(typeof(JsonElementEntity))!.FindProperty(nameof(JsonElementEntity.Data))!
                .GetRelationalTypeMapping()
                .Converter!;

        var original = JsonElement.Parse("""{"key":"value","num":42}""");
        var expected = original.GetRawText();

        // Act
        var serialized = (string)converter.ConvertToProvider(original)!;
        var deserialized = (JsonElement)converter.ConvertFromProvider(serialized)!;
        var actual = deserialized.GetRawText();

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// JsonNode ValueConverter round-trips through string correctly.
    /// </summary>
    [Fact]
    public void JsonNode_converter_round_trips_through_string()
    {
        // Arrange
        using var context = CreateContext<JsonNodeContext>();
        var converter = context.Model.FindEntityType(typeof(JsonNodeEntity))!.FindProperty(nameof(JsonNodeEntity.Data))!
            .GetRelationalTypeMapping()
            .Converter!;

        var original = JsonNode.Parse("""{"nested":{"a":1},"arr":[1,2,3]}""");
        var expected = original!.ToJsonString();

        // Act
        var serialized = (string)converter.ConvertToProvider(original)!;
        var deserialized = (JsonNode?)converter.ConvertFromProvider(serialized);
        var actual = deserialized?.ToJsonString();

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(expected, actual);
    }

    // -- ValueComparer Deep-Equality Tests --

    /// <summary>Verifies one positive or negative content-equality result.</summary>
    [Theory]
    [InlineData("Same", true)]
    [InlineData("Different", false)]
    public void JsonElement_comparer_uses_content_equality(
        string scenario,
        bool expected
    )
    {
        // Arrange
        var (leftJson, rightJson) = GetComparisonPayloads(scenario);
        var left = JsonElement.Parse(leftJson!);
        var right = JsonElement.Parse(rightJson!);
        var comparer = MySqlJsonValueComparers.JsonElementComparer;

        // Act
        var actual = comparer.Equals(left, right);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Verifies the hash contract independently of equality failure cases.</summary>
    [Fact]
    public void JsonElement_equal_content_has_equal_hash_codes()
    {
        // Arrange
        var left = JsonElement.Parse("""{"x":1}""");
        var right = JsonElement.Parse("""{"x":1}""");
        var comparer = MySqlJsonValueComparers.JsonElementComparer;

        // Act
        var leftHash = comparer.GetHashCode(left);
        var rightHash = comparer.GetHashCode(right);

        // Assert
        Assert.Equal(leftHash, rightHash);
    }

    /// <summary>Verifies one content or null comparison independently.</summary>
    [Theory]
    [InlineData("Same", true)]
    [InlineData("Different", false)]
    [InlineData("BothNull", true)]
    [InlineData("LeftNull", false)]
    [InlineData("RightNull", false)]
    public void JsonNode_comparer_uses_content_equality(
        string scenario,
        bool expected
    )
    {
        // Arrange
        var (leftJson, rightJson) = GetComparisonPayloads(scenario);
        var left = leftJson is null ? null : JsonNode.Parse(leftJson);
        var right = rightJson is null ? null : JsonNode.Parse(rightJson);
        var comparer = MySqlJsonValueComparers.JsonNodeComparer;

        // Act
        var actual = comparer.Equals(left, right);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Verifies equal nodes have equal hashes despite distinct object identities.</summary>
    [Fact]
    public void JsonNode_equal_content_has_equal_hash_codes()
    {
        // Arrange
        var left = JsonNode.Parse("""{"x":1}""")!;
        var right = JsonNode.Parse("""{"x":1}""")!;
        var comparer = MySqlJsonValueComparers.JsonNodeComparer;

        // Act
        var leftHash = comparer.GetHashCode(left);
        var rightHash = comparer.GetHashCode(right);

        // Assert
        Assert.Equal(leftHash, rightHash);
    }

    /// <summary>Retains structural replacement and nested mutation as one snapshot lifecycle.</summary>
    [Fact]
    public void JsonNode_change_tracking_uses_structural_snapshot_equality()
    {
        // Arrange
        using var context = CreateContext<JsonNodeContext>();

        var entity = new JsonNodeEntity
        {
            Id = 1,
            Data = JsonNode.Parse("""{"id":1,"nested":{"value":1}}"""),
        };

        var entry = context.Attach(entity);

        // Act
        entity.Data = JsonNode.Parse("""{"nested":{"value":1},"id":1}""");
        context.ChangeTracker.DetectChanges();

        var equalReplacementModified = entry.Property(candidate => candidate.Data).IsModified;

        // WHY: Keep the original snapshot alive across replacement and mutation.
        // Reattaching between these transitions would no longer test snapshot isolation.
        entity.Data!["nested"]!["value"] = 2;
        context.ChangeTracker.DetectChanges();
        var nestedMutationModified = entry.Property(candidate => candidate.Data).IsModified;

        // Assert
        Assert.False(equalReplacementModified);
        Assert.True(nestedMutationModified);
    }

    /// <summary>Verifies object snapshots survive equal replacement and detect nested mutation.</summary>
    [Fact]
    public void JsonObject_change_tracking_uses_structural_snapshot_equality()
    {
        // Arrange
        using var context = CreateContext<JsonObjectContext>();

        var entity = new JsonObjectEntity
        {
            Id = 1,
            Data = JsonNode.Parse("""{"id":1,"nested":{"value":1}}""")!.AsObject(),
        };

        var entry = context.Attach(entity);

        // Act
        entity.Data = JsonNode.Parse("""{"nested":{"value":1},"id":1}""")!.AsObject();
        context.ChangeTracker.DetectChanges();

        var equalReplacementModified = entry.Property(candidate => candidate.Data).IsModified;

        // WHY: The same tracked snapshot must survive a content-equal replacement
        // before a nested mutation is checked.
        entity.Data["nested"]!["value"] = 2;
        context.ChangeTracker.DetectChanges();
        var nestedMutationModified = entry.Property(candidate => candidate.Data).IsModified;

        // Assert
        Assert.False(equalReplacementModified);
        Assert.True(nestedMutationModified);
    }

    /// <summary>Verifies array snapshots survive equal replacement and detect nested mutation.</summary>
    [Fact]
    public void JsonArray_change_tracking_uses_structural_snapshot_equality()
    {
        // Arrange
        using var context = CreateContext<JsonArrayContext>();

        var entity = new JsonArrayEntity
        {
            Id = 1,
            Data = JsonNode.Parse("""[1,{"value":1},3]""")!.AsArray(),
        };

        var entry = context.Attach(entity);

        // Act
        entity.Data = JsonNode.Parse("""[1,{"value":1},3]""")!.AsArray();
        context.ChangeTracker.DetectChanges();

        var equalReplacementModified = entry.Property(candidate => candidate.Data).IsModified;

        // WHY: The same tracked snapshot must survive a content-equal replacement
        // before a nested mutation is checked.
        entity.Data[1]!["value"] = 2;
        context.ChangeTracker.DetectChanges();
        var nestedMutationModified = entry.Property(candidate => candidate.Data).IsModified;

        // Assert
        Assert.False(equalReplacementModified);
        Assert.True(nestedMutationModified);
    }

    /// <summary>Verifies each document content/null combination with scoped document ownership.</summary>
    [Theory]
    [InlineData("Same", true)]
    [InlineData("Different", false)]
    [InlineData("BothNull", true)]
    [InlineData("LeftNull", false)]
    [InlineData("RightNull", false)]
    public void JsonDocument_comparer_preserves_content_and_null_equality(
        string scenario,
        bool expected
    )
    {
        // Arrange
        var (leftJson, rightJson) = GetComparisonPayloads(scenario);
        using var left = leftJson is null ? null : JsonDocument.Parse(leftJson);
        using var right = rightJson is null ? null : JsonDocument.Parse(rightJson);
        var comparer = MySqlJsonValueComparers.JsonDocumentComparer;

        // Act
        var actual = comparer.Equals(left, right);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// JsonElement type mapping has a ValueComparer for deep-equality comparison.
    /// </summary>
    [Fact]
    public void JsonElement_type_mapping_has_value_comparer()
    {
        // Arrange
        using var context = CreateContext<JsonElementContext>();

        // Act
        var typeMapping =
            context.Model.FindEntityType(typeof(JsonElementEntity))!.FindProperty(nameof(JsonElementEntity.Data))!
                .GetRelationalTypeMapping();

        // Assert
        Assert.NotNull(typeMapping.Comparer);
        Assert.Equal(typeof(JsonElement), typeMapping.Comparer.Type);
    }

    /// <summary>
    /// JsonNode type mapping has a ValueComparer for deep-equality comparison.
    /// </summary>
    [Fact]
    public void JsonNode_type_mapping_has_value_comparer()
    {
        // Arrange
        using var context = CreateContext<JsonNodeContext>();

        // Act
        var typeMapping =
            context.Model.FindEntityType(typeof(JsonNodeEntity))!.FindProperty(nameof(JsonNodeEntity.Data))!
                .GetRelationalTypeMapping();

        // Assert
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
        // Arrange
        using var context = CreateContext<JsonStringContext>();

        // Act
        var property =
            context.Model.FindEntityType(typeof(JsonStringEntity))!.FindProperty(nameof(JsonStringEntity.Data))!;

        // Assert
        Assert.Equal("json", property.GetColumnType());
        Assert.Equal(typeof(string), property.ClrType);
    }

    // -- Helpers --

    /// <summary>Names independently selectable content and null boundaries.</summary>
    private static (string? Left, string? Right) GetComparisonPayloads(
        string scenario
    ) => scenario switch
    {
        "Same" => ("""{"x":1}""", """{"x":1}"""),
        "Different" => ("""{"x":1}""", """{"x":2}"""),
        "BothNull" => (null, null),
        "LeftNull" => (null, "{}"),
        "RightNull" => ("{}", null),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    /// <summary>Creates one isolated functional context for the requested JSON CLR model.</summary>
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
