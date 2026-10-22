namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// Verifies that a new auto-increment primary key exists before the column gains
    /// AUTO_INCREMENT.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_adds_primary_key_before_enabling_auto_increment()
    {
        // Arrange
        using var source = new KeylessPeopleContext(CreateOptions<KeylessPeopleContext>());
        using var target = new KeyedPeopleContext(CreateOptions<KeyedPeopleContext>());

        // Act
        var operations = GetDifferences(source, target);

        // Assert
        var addPrimaryKey = Assert.Single(operations.OfType<AddPrimaryKeyOperation>());
        var alterColumn = Assert.Single(operations.OfType<AlterColumnOperation>());

        Assert.True(operations.IndexOf(addPrimaryKey) < operations.IndexOf(alterColumn));
        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            alterColumn[MySqlAnnotationNames.ValueGenerationStrategy]);
    }

    /// <summary>
    /// Verifies that AUTO_INCREMENT is removed while the old primary key still exists.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_disables_auto_increment_before_dropping_primary_key()
    {
        // Arrange
        using var source = new KeyedPeopleContext(CreateOptions<KeyedPeopleContext>());
        using var target = new KeylessPeopleContext(CreateOptions<KeylessPeopleContext>());

        // Act
        var operations = GetDifferences(source, target);

        // Assert
        var alterColumn = Assert.Single(operations.OfType<AlterColumnOperation>());
        var dropPrimaryKey = Assert.Single(operations.OfType<DropPrimaryKeyOperation>());

        Assert.True(operations.IndexOf(alterColumn) < operations.IndexOf(dropPrimaryKey));
        Assert.Equal(MySqlValueGenerationStrategy.None, alterColumn[MySqlAnnotationNames.ValueGenerationStrategy]);
        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            alterColumn.OldColumn[MySqlAnnotationNames.ValueGenerationStrategy]);
    }

    /// <summary>
    /// Verifies that a table rename does not recreate MySQL's fixed-name PRIMARY key.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_removes_primary_key_churn_for_table_rename()
    {
        // Arrange
        using var source = new KeyedPeopleContext(CreateOptions<KeyedPeopleContext>());
        using var target = new KeyedPersonsContext(CreateOptions<KeyedPersonsContext>());

        // Act
        var operations = GetDifferences(source, target);

        // Assert
        Assert.Single(operations.OfType<RenameTableOperation>());
        Assert.Empty(operations.OfType<DropPrimaryKeyOperation>());
        Assert.Empty(operations.OfType<AddPrimaryKeyOperation>());
    }

    /// <summary>
    /// Verifies that dropping every primary-key column does not first leave an
    /// AUTO_INCREMENT column temporarily unkeyed.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_lets_primary_key_column_drop_remove_the_key()
    {
        // Arrange
        using var source = new KeyedPeopleWithReplacementContext(CreateOptions<KeyedPeopleWithReplacementContext>());
        using var target = new ReplacementPeopleContext(CreateOptions<ReplacementPeopleContext>());

        // Act
        var operations = GetDifferences(source, target);

        // Assert
        Assert.Contains(operations.OfType<DropColumnOperation>(), operation => operation.Name == "SomeField");
        Assert.Empty(operations.OfType<DropPrimaryKeyOperation>());
    }

    /// <summary>
    /// Verifies that EF mappings with different non-SQL metadata do not produce duplicate,
    /// locking DDL for the same physical JSON column transition.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_deduplicates_mysql_equivalent_json_column_alters()
    {
        // Arrange
        using var target = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var firstAlter = CreateJsonAlterColumn(typeof(string), isUnicode: true, maxLength: 255);
        var secondAlter = CreateJsonAlterColumn(typeof(JsonDocument), isUnicode: null, maxLength: null);
        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(firstAlter, secondAlter));

        // Act
        var operations = differ.GetDifferences(
            null,
            target
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());

        // Assert
        var alterColumn = Assert.Single(operations.OfType<AlterColumnOperation>());

        Assert.Equal("Entity", alterColumn.Table);
        Assert.Equal("Name", alterColumn.Name);
        Assert.Equal("json", alterColumn.ColumnType);
        Assert.Equal("longtext", alterColumn.OldColumn.ColumnType);
    }

    /// <summary>
    /// Verifies that an explicit SQL backfill makes different CLR projections
    /// equivalent when they alter the same physical shared column.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_deduplicates_equivalent_sql_backfills()
    {
        // Arrange
        using var target = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var firstAlter = CreateRequiredJsonAlterColumn(typeof(string), "JSON_OBJECT()");
        var secondAlter = CreateRequiredJsonAlterColumn(typeof(JsonDocument), "JSON_OBJECT()");
        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(firstAlter, secondAlter));

        // Act
        var operations = differ.GetDifferences(
            null,
            target
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel());

        // Assert
        var alterColumn = Assert.Single(operations.OfType<AlterColumnOperation>());

        Assert.Equal("JSON_OBJECT()", alterColumn.DefaultValueSql);
    }

    /// <summary>
    /// Verifies that distinct application-authored backfills remain distinct
    /// even when table-sharing maps them to the same physical column.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_preserves_distinct_sql_backfills()
    {
        // Arrange
        using var target = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var firstAlter = CreateRequiredJsonAlterColumn(typeof(string), "JSON_OBJECT()");
        var secondAlter = CreateRequiredJsonAlterColumn(typeof(JsonDocument), "JSON_ARRAY()");
        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(firstAlter, secondAlter));

        // Act
        var operations = differ.GetDifferences(
            null,
            target
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel());

        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        // Assert
        Assert.Equal(2, alterColumns.Length);
        Assert.Contains(alterColumns, operation => operation.DefaultValueSql == "JSON_OBJECT()");
        Assert.Contains(alterColumns, operation => operation.DefaultValueSql == "JSON_ARRAY()");
    }
}
