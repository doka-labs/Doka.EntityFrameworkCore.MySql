namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the capabilities cases and shared support.</summary>
public sealed partial class MySqlModelValidatorAndDdlTests
{
    // -- ServerCapabilities Boundaries --

    /// <summary>MySQL 5.7.7 does not support native JSON.</summary>
    [Fact]
    public void MySQL_5_7_7_does_not_support_native_json()
    {
        // Arrange
        var caps = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(5, 7, 7));

        // Act
        var actual = caps.Has(EngineCapability.NativeJsonType);

        // Assert
        Assert.False(actual);
    }

    /// <summary>MySQL 5.7.8 supports native JSON.</summary>
    [Fact]
    public void MySQL_5_7_8_supports_native_json()
    {
        // Arrange
        var caps = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(5, 7, 8));

        // Act
        var actual = caps.Has(EngineCapability.NativeJsonType);

        // Assert
        Assert.True(actual);
    }

    /// <summary>MySQL 8.0 supports native JSON.</summary>
    [Fact]
    public void MySQL_8_0_supports_native_json()
    {
        // Arrange
        var caps = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 0, 0));

        // Act
        var actual = caps.Has(EngineCapability.NativeJsonType);

        // Assert
        Assert.True(actual);
    }

    /// <summary>MariaDB 10.2 does not support native sequences.</summary>
    [Fact]
    public void MariaDB_10_2_does_not_support_native_sequences()
    {
        // Arrange
        var caps = EngineProfileTable.Resolve(EngineFamily.MariaDb, new Version(10, 2, 0));

        // Act
        var actual = caps.Has(EngineCapability.NativeSequences);

        // Assert
        Assert.False(actual);
    }

    /// <summary>MariaDB 10.3 supports native sequences.</summary>
    [Fact]
    public void MariaDB_10_3_supports_native_sequences()
    {
        // Arrange
        var caps = EngineProfileTable.Resolve(EngineFamily.MariaDb, new Version(10, 3, 0));

        // Act
        var actual = caps.Has(EngineCapability.NativeSequences);

        // Assert
        Assert.True(actual);
    }

    /// <summary>MariaDB 10.4 does not support RETURNING clause.</summary>
    [Fact]
    public void MariaDB_10_4_does_not_support_returning_clause()
    {
        // Arrange
        var caps = EngineProfileTable.Resolve(EngineFamily.MariaDb, new Version(10, 4, 0));

        // Act
        var actual = caps.Has(EngineCapability.ReturningClause);

        // Assert
        Assert.False(actual);
    }

    /// <summary>MariaDB 10.5 supports RETURNING clause.</summary>
    [Fact]
    public void MariaDB_10_5_supports_returning_clause()
    {
        // Arrange
        var caps = EngineProfileTable.Resolve(EngineFamily.MariaDb, new Version(10, 5, 0));

        // Act
        var actual = caps.Has(EngineCapability.ReturningClause);

        // Assert
        Assert.True(actual);
    }

    /// <summary>MySQL never supports native sequences.</summary>
    [Fact]
    public void MySQL_never_supports_native_sequences()
    {
        // Arrange
        var caps = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4, 0));

        // Act
        var actual = caps.Has(EngineCapability.NativeSequences);

        // Assert
        Assert.False(actual);
    }

    /// <summary>MariaDB never has native JSON (uses longtext alias).</summary>
    [Fact]
    public void MariaDB_never_supports_native_json()
    {
        var profile = MySqlServerVersion.MariaDb(new Version(11, 8, 0)).Profile;

        Assert.False(profile.Engine.Has(EngineCapability.NativeJsonType));
        Assert.Equal(ProviderSupportStatus.Emulated, profile.GetSupport(ProviderCapability.JsonColumns));
    }

    /// <summary>
    /// A direct index over a JSON complex value is rejected before invalid DDL
    /// reaches the server.
    /// </summary>
    [Fact]
    public void Direct_index_on_json_complex_property_is_rejected()
    {
        // Arrange
        using var context = new JsonIndexContext<JsonComplexPropertyScenario>(
            CreateOptions<JsonIndexContext<JsonComplexPropertyScenario>>());

        // Act
        var caught = Record.Exception(() => _ = context.Model);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(caught);

        Assert.Contains("targets JSON-mapped member 'Payload'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("generated column", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A direct index over a scalar path inside JSON storage is rejected rather
    /// than being emitted as an index over the container column.
    /// </summary>
    [Fact]
    public void Direct_index_on_json_scalar_path_is_rejected()
    {
        // Arrange
        using var context = new JsonIndexContext<JsonScalarPathScenario>(
            CreateOptions<JsonIndexContext<JsonScalarPathScenario>>());

        // Act
        var caught = Record.Exception(() => _ = context.Model);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(caught);

        Assert.Contains("targets JSON-mapped member 'Code'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("generated column", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An indexed scalar generated from JSON remains a supported, explicit
    /// representation of the engine's indirect-indexing contract.
    /// </summary>
    [Fact]
    public void Generated_scalar_index_for_json_path_is_accepted()
    {
        // Arrange
        using var context = new JsonIndexContext<JsonGeneratedColumnScenario>(
            CreateOptions<JsonIndexContext<JsonGeneratedColumnScenario>>());

        // Act
        var entityType = context.Model.FindEntityType(typeof(JsonIndexEntity));

        // Assert
        var index = Assert.Single(entityType!.GetIndexes());
        var property = Assert.IsAssignableFrom<IProperty>(Assert.Single(index.Properties));

        Assert.Equal(nameof(JsonIndexEntity.PayloadCode), property.Name);
        Assert.Equal(
            "JSON_UNQUOTE(JSON_EXTRACT(`Payload`, '$.Code'))",
            property.GetComputedColumnSql());
    }
}
