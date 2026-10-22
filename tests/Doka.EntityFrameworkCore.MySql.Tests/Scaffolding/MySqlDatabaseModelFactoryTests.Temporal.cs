using Microsoft.EntityFrameworkCore.Scaffolding;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDatabaseModelFactoryTests
{
    /// <summary>
    /// Verifies that MariaDB's authoritative SYSTEM VERSIONED table type and
    /// system-time period flags reconstruct the provider temporal contract.
    /// </summary>
    [Fact]
    public void Reverse_engineering_recognizes_native_mariadb_temporal_table()
    {
        // Arrange
        using var connection = new ScaffoldingDbConnection(ScaffoldingScenario.NativeMariaDbTemporal);
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        // Act
        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["audit_entries"], Array.Empty<string>()));

        // Assert
        var table = Assert.Single(databaseModel.Tables);
        Assert.Equal("audit_entries", table.Name);
        Assert.True(table.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)?.Value as bool?);
        Assert.Equal(
            "ValidFrom",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodStartColumn)?.Value);
        Assert.Equal(
            "ValidTo",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodEndColumn)?.Value);
    }

    /// <summary>
    /// Verifies that the complete provider-owned MySQL trigger and storage
    /// contract reconstructs one temporal table and suppresses its history table.
    /// </summary>
    [Fact]
    public void Reverse_engineering_recognizes_complete_mysql_temporal_emulation()
    {
        // Arrange
        using var connection = new ScaffoldingDbConnection(ScaffoldingScenario.MySqlTemporalEmulation);
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        // Act
        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions([], Array.Empty<string>()));

        // Assert
        var table = Assert.Single(databaseModel.Tables);
        Assert.Equal("audit_entries", table.Name);
        Assert.True(table.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)?.Value as bool?);
        Assert.Equal(
            "audit_entries_history",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistoryTable)?.Value);
        Assert.Equal(
            "ValidFrom",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodStartColumn)?.Value);
        Assert.Equal(
            "ValidTo",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodEndColumn)?.Value);
    }

    /// <summary>
    /// Verifies that an incomplete provider trigger set is not guessed to be
    /// temporal and therefore keeps both current and history tables visible.
    /// </summary>
    [Fact]
    public void Reverse_engineering_does_not_recognize_incomplete_mysql_temporal_emulation()
    {
        // Arrange
        using var connection = new ScaffoldingDbConnection(
            ScaffoldingScenario.IncompleteMySqlTemporalEmulation);

        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        // Act
        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions([], Array.Empty<string>()));

        var tables = databaseModel.Tables.OrderBy(table => table.Name).ToArray();

        // Assert
        Assert.Collection(
            tables,
            table =>
            {
                Assert.Equal("audit_entries", table.Name);
                Assert.Null(table.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal));
            },
            table => Assert.Equal("audit_entries_history", table.Name));
    }
}
