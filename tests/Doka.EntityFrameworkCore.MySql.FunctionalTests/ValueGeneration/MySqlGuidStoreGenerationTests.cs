namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Protects the boundary between implicit client GUIDs and explicit store-generated values.
/// </summary>
public sealed partial class MySqlGuidStoreGenerationTests
{
    /// <summary>SQL defaults use a temporary tracking key instead of replacing the database expression.</summary>
    /// <param name="format">The physical GUID representation.</param>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    [InlineData(MySqlGuidFormat.Uuid)]
    public void Sql_default_key_uses_temporary_generation(
        MySqlGuidFormat format
    )
    {
        // Arrange
        using var context = CreateStoreContext("sql", format);
        var row = new StoreRow();

        // Act
        var entry = context.Add(row);
        var key = entry.Property(value => value.Id);

        // Assert
        Assert.Null(key.Metadata.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, key.Metadata.ValueGenerated);
        Assert.Equal(DefaultSql(format), key.Metadata.GetDefaultValueSql());
        Assert.True(key.IsTemporary);
        Assert.NotEqual(Guid.Empty, key.CurrentValue);
        Assert.Equal(Guid.Empty, row.Id);
    }

    /// <summary>Ordinary keys and literal defaults retain EF's permanent client-generation semantics.</summary>
    /// <param name="configuration">The model configuration that does not request SQL generation.</param>
    [Theory]
    [InlineData("ordinary")]
    [InlineData("on-add")]
    [InlineData("literal")]
    public void Non_sql_key_retains_permanent_client_generation(
        string configuration
    )
    {
        // Arrange
        using var context = CreateStoreContext(configuration);
        var row = new StoreRow();

        // Act
        var entry = context.Add(row);

        // Assert
        Assert.Equal(
            MySqlValueGenerationStrategy.ClientGuid,
            entry.Property(value => value.Id).Metadata.GetMySqlValueGenerationStrategy());
        Assert.False(entry.Property(value => value.Id).IsTemporary);
        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.Equal(7, row.Id.Version);
    }

    /// <summary>Deliberate client strategies remain authoritative in either fluent-call order.</summary>
    /// <param name="configuration">The explicit strategy API and its order relative to the default.</param>
    [Theory]
    [InlineData("client-before")]
    [InlineData("client-after")]
    [InlineData("strategy-before")]
    [InlineData("strategy-after")]
    public void Explicit_client_strategy_overrides_sql_default(
        string configuration
    )
    {
        // Arrange
        using var context = CreateStoreContext(configuration);
        var row = new StoreRow();

        // Act
        var entry = context.Add(row);

        // Assert
        Assert.Equal("UUID()", entry.Property(value => value.Id).Metadata.GetDefaultValueSql());
        Assert.Equal(
            MySqlValueGenerationStrategy.ClientGuid,
            entry.Property(value => value.Id).Metadata.GetMySqlValueGenerationStrategy());
        Assert.False(entry.Property(value => value.Id).IsTemporary);
        Assert.Equal(7, row.Id.Version);
    }

    /// <summary>Application generator factories take precedence over both SQL defaults and provider strategies.</summary>
    /// <param name="configuration">The factory API and configuration order.</param>
    [Theory]
    [InlineData("factory-before")]
    [InlineData("factory-after")]
    [InlineData("factory-client-before")]
    [InlineData("factory-client-after")]
    public void Explicit_factory_retains_its_value(
        string configuration
    )
    {
        // Arrange
        using var context = CreateStoreContext(configuration);
        var row = new StoreRow();

        // Act
        var entry = context.Add(row);

        // Assert
        Assert.Equal(s_factoryGuid, row.Id);
        Assert.False(entry.Property(value => value.Id).IsTemporary);
        Assert.Equal("UUID()", entry.Property(value => value.Id).Metadata.GetDefaultValueSql());
    }

    /// <summary>An explicitly assigned key is not replaced or marked temporary by the SQL default.</summary>
    [Fact]
    public void Assigned_key_is_preserved()
    {
        // Arrange
        using var context = CreateStoreContext("sql");
        var row = new StoreRow { Id = s_factoryGuid };

        // Act
        var entry = context.Add(row);

        // Assert
        Assert.Equal(s_factoryGuid, row.Id);
        Assert.False(entry.Property(value => value.Id).IsTemporary);
    }

    /// <summary>Explicit generation opt-outs remain disabled even when the schema has a default.</summary>
    /// <param name="configuration">The opt-out API and its order relative to the default.</param>
    [Theory]
    [InlineData("never-before")]
    [InlineData("never-after")]
    [InlineData("none-before")]
    [InlineData("none-after")]
    public void Explicit_no_generation_preserves_empty_key(
        string configuration
    )
    {
        // Arrange
        using var context = CreateStoreContext(configuration);
        var row = new StoreRow();

        // Act
        var entry = context.Add(row);

        // Assert
        Assert.Equal(ValueGenerated.Never, entry.Property(value => value.Id).Metadata.ValueGenerated);
        Assert.Equal(Guid.Empty, row.Id);
        Assert.False(entry.Property(value => value.Id).IsTemporary);
    }

    /// <summary>Defaults on required and nullable non-key GUIDs do not select an implicit client strategy.</summary>
    /// <param name="nullable">Whether the generated non-key property is nullable.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Non_key_sql_default_preserves_store_generation(
        bool nullable
    )
    {
        // Arrange
        using var context = CreateStoreContext(nullable ? "nullable-default" : "required-default");

        // Act
        var property = context.Model.FindEntityType(typeof(StoreRow))!
            .FindProperty(nullable ? nameof(StoreRow.OptionalToken) : nameof(StoreRow.Token))!;

        // Assert
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
        Assert.Null(property.GetMySqlValueGenerationStrategy());
        Assert.Equal("UUID()", property.GetDefaultValueSql());
    }

    /// <summary>Computed GUIDs retain update generation and EF's ignore-before-save behavior.</summary>
    /// <param name="nullable">Whether the computed property is nullable.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Computed_guid_preserves_on_add_or_update(
        bool nullable
    )
    {
        // Arrange
        using var context = CreateStoreContext(nullable ? "nullable-computed" : "required-computed");

        // Act
        var property = context.Model.FindEntityType(typeof(StoreRow))!
            .FindProperty(nullable ? nameof(StoreRow.OptionalToken) : nameof(StoreRow.Token))!;

        // Assert
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.Null(property.GetMySqlValueGenerationStrategy());
        Assert.Equal(PropertySaveBehavior.Ignore, property.GetBeforeSaveBehavior());
        Assert.Equal(PropertySaveBehavior.Ignore, property.GetAfterSaveBehavior());
        Assert.Equal("`Source`", property.GetComputedColumnSql());
    }

    /// <summary>Application-owned conversions remain intact while SQL generation uses a temporary key.</summary>
    /// <param name="configuration">The application conversion shape.</param>
    [Theory]
    [InlineData("converted-text")]
    [InlineData("converted-bytes")]
    public void Application_converter_and_sql_default_are_preserved(
        string configuration
    )
    {
        // Arrange
        using var context = CreateStoreContext(configuration);
        var row = new StoreRow();

        // Act
        var entry = context.Add(row);
        var property = entry.Property(value => value.Id).Metadata;

        // Assert
        Assert.Null(property.GetMySqlGuidFormat());
        Assert.Null(property.GetMySqlValueGenerationStrategy());
        Assert.NotNull(property.GetTypeMapping().Converter);
        Assert.Equal(
            configuration == "converted-text" ? typeof(string) : typeof(byte[]),
            property.GetTypeMapping().Converter!.ProviderClrType);
        Assert.True(entry.Property(value => value.Id).IsTemporary);
    }

    /// <summary>Complex scalar GUID defaults retain EF's relational generation facet.</summary>
    [Fact]
    public void Complex_scalar_sql_default_preserves_store_generation()
    {
        // Arrange
        using var context = CreateStoreContext("complex-default");

        // Act
        var property = context.Model.FindEntityType(typeof(StoreRow))!
            .FindComplexProperty(nameof(StoreRow.Details))!.ComplexType.FindProperty(nameof(StoreDetails.Token))!;

        // Assert
        Assert.Equal("UUID()", property.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
        Assert.Null(property.GetMySqlValueGenerationStrategy());
    }
}
