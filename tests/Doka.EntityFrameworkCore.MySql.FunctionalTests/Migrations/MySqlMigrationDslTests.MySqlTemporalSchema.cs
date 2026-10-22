namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// Verifies that an emulated MySQL temporal table keeps its current and
    /// history schemas synchronized while its triggers are being rebuilt.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_mirrors_temporal_column_additions_on_mysql()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        using var targetContext = new TemporalSchemaWithDescriptionContext(
            CreateOptions<TemporalSchemaWithDescriptionContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        var dropTrigger = sql.IndexOf("DROP TRIGGER", StringComparison.Ordinal);
        var alterCurrent = sql.IndexOf(
            "ALTER TABLE `TemporalRecords` ADD `Description`",
            StringComparison.Ordinal);

        var alterHistory = sql.IndexOf(
            "ALTER TABLE `TemporalRecordsHistory` ADD `Description`",
            StringComparison.Ordinal);

        var createTrigger = sql.IndexOf("CREATE TRIGGER", StringComparison.Ordinal);

        // Assert
        Assert.True(dropTrigger >= 0);
        Assert.True(alterCurrent > dropTrigger);
        Assert.True(alterHistory > alterCurrent);
        Assert.True(createTrigger > alterHistory);
    }

    /// <summary>
    /// Verifies that an emulated temporal column rename remains atomic from the
    /// provider contract's perspective: triggers are detached, both physical
    /// tables are renamed, and the rebuilt triggers use the new column name.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_mirrors_temporal_column_renames_on_mysql()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        using var targetContext = new TemporalSchemaWithRenamedColumnContext(
            CreateOptions<TemporalSchemaWithRenamedColumnContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        // Assert
        Assert.Contains(
            "ALTER TABLE `TemporalRecords` RENAME COLUMN `Name` TO `DisplayName`",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER TABLE `TemporalRecordsHistory` RENAME COLUMN `Name` TO `DisplayName`",
            sql,
            StringComparison.Ordinal);

        var rebuiltTriggers = sql[sql.LastIndexOf("CREATE TRIGGER", StringComparison.Ordinal)..];

        Assert.Contains("`DisplayName`", rebuiltTriggers, StringComparison.Ordinal);
        Assert.DoesNotContain("`Name`", rebuiltTriggers, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that regular column alterations stay identical between the
    /// current and history tables before provider-owned triggers are rebuilt.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_mirrors_temporal_column_alterations_on_mysql()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        using var targetContext = new TemporalSchemaWithBoundedNameContext(
            CreateOptions<TemporalSchemaWithBoundedNameContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        // Assert
        Assert.Contains(
            "ALTER TABLE `TemporalRecords` MODIFY COLUMN `Name` varchar(128) NOT NULL",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER TABLE `TemporalRecordsHistory` MODIFY COLUMN `Name` varchar(128) NOT NULL",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that dropping a column from an emulated temporal table removes
    /// the same column from retained history instead of leaving schema drift.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_mirrors_temporal_column_drops_on_mysql()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaWithDescriptionContext(
            CreateOptions<TemporalSchemaWithDescriptionContext>(serverVersion));

        using var targetContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        var commands = targetContext
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, targetContext.Model);

        // Assert
        var dropColumn = Assert.Single(operations.OfType<DropColumnOperation>());

        Assert.True(
            dropColumn.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)?.Value as bool?);
        Assert.True(dropColumn.FindAnnotation(MySqlAnnotationNames.IsTemporal)?.Value as bool?);
        Assert.Equal(
            "TemporalRecordsHistory",
            dropColumn.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistoryTable)?.Value);
        Assert.Equal(
            "TemporalRecordsHistory",
            dropColumn.FindAnnotation(MySqlAnnotationNames.TemporalHistoryTable)?.Value);

        var sql = string.Join(
            Environment.NewLine,
            commands.Select(command => command.CommandText));

        Assert.Contains(
            "ALTER TABLE `TemporalRecords` DROP COLUMN `Description`",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER TABLE `TemporalRecordsHistory` DROP COLUMN `Description`",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated columns are reproduced in retained history but
    /// omitted from trigger projections because MySQL forbids OLD/NEW references
    /// to generated columns.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_preserves_generated_columns_without_trigger_references()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        using var targetContext = new TemporalSchemaWithGeneratedColumnContext(
            CreateOptions<TemporalSchemaWithGeneratedColumnContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        // Assert
        Assert.Contains(
            "ALTER TABLE `TemporalRecords` ADD `NameLength` int GENERATED ALWAYS AS (CHAR_LENGTH(`Name`)) STORED",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER TABLE `TemporalRecordsHistory` ADD `NameLength` int GENERATED ALWAYS AS (CHAR_LENGTH(`Name`)) STORED",
            sql,
            StringComparison.Ordinal);

        var rebuiltTriggers = sql[sql.IndexOf("CREATE TRIGGER", StringComparison.Ordinal)..];

        Assert.DoesNotContain("`NameLength`", rebuiltTriggers, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that renaming an emulated temporal table also renames its
    /// history table and rebinds every provider-owned trigger.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_renames_complete_temporal_contract_on_mysql()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        using var targetContext = new RenamedTemporalSchemaContext(
            CreateOptions<RenamedTemporalSchemaContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        // Assert
        Assert.Contains(
            "RENAME TABLE `TemporalRecords` TO `TemporalEntries`",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "RENAME TABLE `TemporalRecordsHistory` TO `TemporalEntriesHistory`",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(" ON `TemporalRecords` FOR EACH ROW", sql, StringComparison.Ordinal);
        Assert.Contains(" ON `TemporalEntries` FOR EACH ROW", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that dropping an emulated temporal table removes its external
    /// history table instead of leaving retained data without an owner.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_drops_complete_temporal_contract_on_mysql()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        using var targetContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));

        // Act
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        // Assert
        Assert.Contains("DROP TABLE `TemporalRecords`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `TemporalRecordsHistory`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MySQL temporal activation creates period columns before
    /// the history table and triggers.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_enables_temporal_contract_on_mysql()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var nonTemporalContext = new NonTemporalSchemaContext(
            CreateOptions<NonTemporalSchemaContext>(serverVersion));

        using var temporalContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        // Act
        var enableSql = GenerateMigrationSql(nonTemporalContext, temporalContext);
        var finalPeriodColumn = enableSql.IndexOf(
            "ADD `ValidTo` datetime(6) NOT NULL DEFAULT '9999-12-31 23:59:59.999999'",
            StringComparison.Ordinal);

        var historyActivation = enableSql.IndexOf(
            "CREATE TABLE `TemporalRecordsHistory`",
            StringComparison.Ordinal);

        // Assert
        Assert.Contains(
            "ADD `ValidFrom` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)",
            enableSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ADD `ValidTo` datetime(6) NOT NULL DEFAULT '9999-12-31 23:59:59.999999'",
            enableSql,
            StringComparison.Ordinal);
        Assert.True(historyActivation > finalPeriodColumn);
        Assert.Contains("CREATE TABLE `TemporalRecordsHistory`", enableSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TRIGGER", enableSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MySQL temporal deactivation removes triggers, history and
    /// period columns as one physical migration contract.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_disables_temporal_contract_on_mysql()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var nonTemporalContext = new NonTemporalSchemaContext(
            CreateOptions<NonTemporalSchemaContext>(serverVersion));

        using var temporalContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        // Act
        var disableSql = GenerateMigrationSql(temporalContext, nonTemporalContext);

        // Assert
        Assert.Contains("DROP TRIGGER", disableSql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `TemporalRecordsHistory`", disableSql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `ValidFrom`", disableSql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `ValidTo`", disableSql, StringComparison.Ordinal);
    }
}
