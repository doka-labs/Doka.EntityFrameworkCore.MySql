using Microsoft.EntityFrameworkCore.Scaffolding;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies focused reverse-engineering metadata loading behavior.
/// </summary>
public sealed partial class MySqlDatabaseModelFactoryTests
{
    /// <summary>
    /// Verifies that descending index metadata backfills preceding ascending columns correctly.
    /// </summary>
    [Fact]
    public void Reverse_engineering_backfills_preceding_ascending_columns_for_mixed_direction_indexes()
    {
        // Arrange
        using var connection = new ScaffoldingDbConnection();
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        // Act
        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["mixed_index_table"], Array.Empty<string>()));

        // Assert
        var table = Assert.Single(databaseModel.Tables);
        var index = Assert.Single(table.Indexes);
        Assert.Collection(
            index.Columns,
            column => Assert.Equal("First", column.Name),
            column => Assert.Equal("Second", column.Name));
        Assert.NotNull(index.IsDescending);
        Assert.Equal(
            [
                false,
                true
            ],
            index.IsDescending);
    }

    /// <summary>
    /// Verifies that reverse engineering loads spatial-index metadata and MySQL SRID metadata.
    /// </summary>
    [Fact]
    public void Reverse_engineering_reads_spatial_index_and_srid_metadata()
    {
        // Arrange
        using var connection = new ScaffoldingDbConnection();
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        // Act
        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["spatial_feature_table"], Array.Empty<string>()));

        // Assert
        var table = Assert.Single(databaseModel.Tables);
        var spatialColumn = Assert.Single(table.Columns, column => column.Name == "Location");
        var spatialIndex = Assert.Single(table.Indexes);
        Assert.Equal(
            4326,
            spatialColumn.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
                ?.Value);
        Assert.True(
            spatialIndex.FindAnnotation(MySqlAnnotationNames.SpatialIndex)
                ?.Value as bool?);
    }

    /// <summary>
    /// Verifies that MariaDB SRID metadata shares the general CHECK catalog query,
    /// consumes only the provider-owned check, and preserves an adjacent user check.
    /// </summary>
    [Fact]
    public void Reverse_engineering_reads_mariadb_srid_and_user_checks_in_one_catalog_query()
    {
        // Arrange
        using var connection = new ScaffoldingDbConnection(ScaffoldingScenario.MariaDbSpatial);
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        // Act
        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["spatial_feature_table"], Array.Empty<string>()));

        // Assert
        var table = Assert.Single(databaseModel.Tables);
        var spatialColumn = Assert.Single(table.Columns, column => column.Name == "Location");
        var checkConstraints = Assert.IsType<MySqlScaffoldedCheckConstraint[]>(
            table.FindAnnotation(MySqlAnnotationNames.ScaffoldingCheckConstraints)
                ?.Value);

        var userCheck = Assert.Single(checkConstraints);
        Assert.Equal(
            4326,
            spatialColumn.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
                ?.Value);
        Assert.Equal("CK_spatial_location_not_null", userCheck.Name);
        Assert.Equal("`Location` IS NOT NULL", userCheck.Sql);
        Assert.Single(
            connection.ReaderCommands,
            commandText => commandText.Contains(
                    "FROM information_schema.CHECK_CONSTRAINTS",
                    StringComparison.Ordinal)
                && !commandText.Contains("JSON_VALID", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that MariaDB's intrinsic JSON-alias CHECK is represented by the
    /// canonical json store type while an adjacent caller-owned CHECK survives.
    /// </summary>
    [Fact]
    public void Reverse_engineering_consumes_only_the_intrinsic_mariadb_json_check()
    {
        // Arrange
        using var connection = new ScaffoldingDbConnection(ScaffoldingScenario.MariaDbJson);
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        // Act
        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["json_constraint_table"], Array.Empty<string>()));

        // Assert
        var table = Assert.Single(databaseModel.Tables);
        var payload = Assert.Single(table.Columns);
        var checkConstraints = Assert.IsType<MySqlScaffoldedCheckConstraint[]>(
            table.FindAnnotation(MySqlAnnotationNames.ScaffoldingCheckConstraints)
                ?.Value);

        var userCheck = Assert.Single(checkConstraints);
        Assert.Equal("json", payload.StoreType);
        Assert.Equal("CK_JsonConstraintTable_Payload", userCheck.Name);
        Assert.Equal(
            "`Payload` IS NULL OR json_valid(`Payload`)",
            userCheck.Sql);
    }

    /// <summary>
    /// Verifies that EF schema filters select the equivalent MySQL database,
    /// qualify the returned metadata, and restore the caller's active database.
    /// </summary>
    [Fact]
    public void Reverse_engineering_schema_filter_selects_and_qualifies_database()
    {
        // Arrange
        using var connection = new ScaffoldingDbConnection();
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        // Act
        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["mixed_index_table"], ["tenant_database"]));

        // Assert
        var table = Assert.Single(databaseModel.Tables);
        Assert.Equal("tenant_database", table.Schema);
        Assert.Equal("phase2", connection.Database);
        Assert.Equal(
            [
                "tenant_database",
                "phase2"
            ],
            connection.DatabaseChanges);
    }

    /// <summary>
    /// Verifies that a foreign key spanning two selected MySQL databases resolves
    /// after both database-qualified table and column sets have been loaded.
    /// </summary>
    [Fact]
    public void Reverse_engineering_resolves_cross_database_foreign_key()
    {
        // Arrange
        using var connection = new ScaffoldingDbConnection();
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        // Act
        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(
                [],
                [
                    "principal_database",
                    "dependent_database"
                ]));

        // Assert
        var principalTable = Assert.Single(
            databaseModel.Tables,
            table => table.Schema == "principal_database");

        var dependentTable = Assert.Single(
            databaseModel.Tables,
            table => table.Schema == "dependent_database");

        var foreignKey = Assert.Single(dependentTable.ForeignKeys);
        Assert.Same(principalTable, foreignKey.PrincipalTable);
        Assert.Same(
            dependentTable.Columns.Single(column => column.Name == "PrincipalId"),
            Assert.Single(foreignKey.Columns));
        Assert.Same(
            principalTable.Columns.Single(column => column.Name == "Id"),
            Assert.Single(foreignKey.PrincipalColumns));
        Assert.Equal(ReferentialAction.Cascade, foreignKey.OnDelete);
        Assert.Equal("phase2", connection.Database);
        Assert.Equal(
            [
                "principal_database",
                "dependent_database",
                "principal_database",
                "dependent_database",
                "phase2"
            ],
            connection.DatabaseChanges);
    }
}
