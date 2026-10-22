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
}
