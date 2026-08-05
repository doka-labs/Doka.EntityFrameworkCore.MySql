using Microsoft.EntityFrameworkCore.Design.Internal;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the narrow provider-specific migration DSL.
/// </summary>
public sealed class MySqlMigrationDslTests
{
    private static readonly int[] s_indexPrefixLengths = [32, 0];
    private static readonly int[] s_singlePrefixLength = [16];
    private static readonly int[] s_negativePrefixLengths = [-1, 0];
    private static readonly bool[] s_mixedIndexDirections = [false, true];

    [Fact]
    public void Public_mysql_specific_fluent_apis_stamp_expected_metadata_annotations()
    {
        using var context = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var entityType = context.Model.FindEntityType(typeof(MigrationDslEntity));
        var property = entityType?.FindProperty(nameof(MigrationDslEntity.ExternalId));
        var prefixIndex = entityType
            ?.GetIndexes()
            .Single(index => index.GetDatabaseName() == "IX_MigrationDsl_Name_Code");
        var fullTextIndex = entityType
            ?.GetIndexes()
            .Single(index => index.GetDatabaseName() == "IX_MigrationDsl_Body");

        Assert.Equal("utf8mb4", context.Model.GetMySqlCharSet());
        Assert.Equal("utf8mb4", entityType?.GetMySqlCharSet());
        Assert.Equal("InnoDB", entityType?.GetMySqlStorageEngine());
        Assert.Equal(MySqlGuidFormat.Char36, property?.GetMySqlGuidFormat());
        Assert.Equal(
            s_indexPrefixLengths,
            prefixIndex?.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength)
                ?.Value as int[]);
        Assert.True(
            fullTextIndex?.FindAnnotation(MySqlAnnotationNames.FullTextIndex)
                ?.Value as bool?);
    }

    /// <summary>
    /// Verifies that the public prefix-length API rejects incomplete and negative metadata.
    /// </summary>
    [Fact]
    public void Public_index_fluent_api_rejects_invalid_prefix_lengths()
    {
        var modelBuilder = new ModelBuilder();
        var indexBuilder = modelBuilder
            .Entity<MigrationDslEntity>()
            .HasIndex(entity => new
            {
                entity.Name,
                entity.Code,
            });

        Assert.Throws<ArgumentException>(
            () => indexBuilder.HasPrefixLength(s_singlePrefixLength));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => indexBuilder.HasPrefixLength(s_negativePrefixLengths));
    }

    /// <summary>
    /// Verifies that the migrations model differ carries the narrow MySQL-specific annotations into operations.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_carries_mysql_specific_annotations_into_operations()
    {
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var operations = differ.GetDifferences(
            sourceContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel(),
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());

        var alterDatabase = Assert.Single(operations.OfType<AlterDatabaseOperation>());
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var externalIdColumn = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(MigrationDslEntity.ExternalId));
        var prefixIndex = Assert.Single(
            operations.OfType<CreateIndexOperation>(),
            operation => operation.Name == "IX_MigrationDsl_Name_Code");
        var fullTextIndex = Assert.Single(
            operations.OfType<CreateIndexOperation>(),
            operation => operation.Name == "IX_MigrationDsl_Body");
        var spatialIndex = Assert.Single(
            operations.OfType<CreateIndexOperation>(),
            operation => operation.Name == "IX_MigrationDsl_Location");

        Assert.Equal(
            "utf8mb4",
            alterDatabase.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value);
        Assert.Equal(
            "utf8mb4",
            createTable.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value);
        Assert.Equal(
            "InnoDB",
            createTable.FindAnnotation(MySqlAnnotationNames.StorageEngine)
                ?.Value);
        Assert.Equal(
            MySqlGuidFormat.Char36,
            externalIdColumn.FindAnnotation(MySqlAnnotationNames.GuidFormat)
                ?.Value);
        Assert.Equal("char(36)", externalIdColumn.ColumnType);
        Assert.Equal(
            s_indexPrefixLengths,
            prefixIndex.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength)
                ?.Value as int[]);
        Assert.Equal(s_mixedIndexDirections, prefixIndex.IsDescending);
        Assert.Null(prefixIndex.FindAnnotation(MySqlAnnotationNames.SpatialIndex));
        Assert.Null(fullTextIndex.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength));
        Assert.Null(fullTextIndex.FindAnnotation(MySqlAnnotationNames.SpatialIndex));
        Assert.True(
            fullTextIndex.FindAnnotation(MySqlAnnotationNames.FullTextIndex)
                ?.Value as bool?);
        Assert.Null(spatialIndex.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength));
        Assert.Null(spatialIndex.FindAnnotation(MySqlAnnotationNames.FullTextIndex));
        Assert.True(
            spatialIndex.FindAnnotation(MySqlAnnotationNames.SpatialIndex)
                ?.Value as bool?);
    }

    /// <summary>
    /// Verifies that the relational model preserves the complete temporal table
    /// contract when EF Core materializes migration operations.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_carries_temporal_annotations_into_create_operations()
    {
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new TemporalMigrationDslContext(CreateOptions<TemporalMigrationDslContext>());
        var operations = GetDifferences(sourceContext, targetContext);
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var periodStart = Assert.Single(
            createTable.Columns,
            column => column.Name == "ValidFrom");
        var periodEnd = Assert.Single(
            createTable.Columns,
            column => column.Name == "ValidTo");

        Assert.True(createTable.FindAnnotation(MySqlAnnotationNames.IsTemporal)?.Value as bool?);
        Assert.Equal(
            "MigrationDslHistory",
            createTable.FindAnnotation(MySqlAnnotationNames.TemporalHistoryTable)?.Value);
        Assert.Equal(
            "ValidFrom",
            createTable.FindAnnotation(MySqlAnnotationNames.TemporalPeriodStartColumn)?.Value);
        Assert.Equal(
            "ValidTo",
            createTable.FindAnnotation(MySqlAnnotationNames.TemporalPeriodEndColumn)?.Value);
        Assert.True(periodStart.FindAnnotation(MySqlAnnotationNames.TemporalPeriodStartColumn)?.Value as bool?);
        Assert.True(periodEnd.FindAnnotation(MySqlAnnotationNames.TemporalPeriodEndColumn)?.Value as bool?);
    }

    /// <summary>
    /// Verifies that MariaDB receives its native system-versioned table contract.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_uses_native_system_versioning_on_mariadb()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var sourceContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));
        using var targetContext = new TemporalMigrationDslContext(
            CreateOptions<TemporalMigrationDslContext>(serverVersion));
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        Assert.Contains(
            "`ValidFrom` timestamp(6) GENERATED ALWAYS AS ROW START",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "`ValidTo` timestamp(6) GENERATED ALWAYS AS ROW END",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "PERIOD FOR SYSTEM_TIME (`ValidFrom`, `ValidTo`)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("WITH SYSTEM VERSIONING", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TRIGGER", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE `MigrationDslHistory`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MySQL receives the complete transactional temporal emulation.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_uses_history_table_and_triggers_on_mysql()
    {
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));
        using var targetContext = new TemporalMigrationDslContext(
            CreateOptions<TemporalMigrationDslContext>(serverVersion));
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        Assert.Contains("CREATE TABLE `MigrationDslHistory`", sql, StringComparison.Ordinal);
        Assert.Contains("BEFORE INSERT ON `MigrationDsl`", sql, StringComparison.Ordinal);
        Assert.Contains("BEFORE UPDATE ON `MigrationDsl`", sql, StringComparison.Ordinal);
        Assert.Contains("BEFORE DELETE ON `MigrationDsl`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `MigrationDslHistory`", sql, StringComparison.Ordinal);
        Assert.Contains("UTC_TIMESTAMP(6)", sql, StringComparison.Ordinal);
        Assert.Contains("doka-temporal-v1:", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH SYSTEM VERSIONING", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that an emulated MySQL temporal table keeps its current and
    /// history schemas synchronized while its triggers are being rebuilt.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_mirrors_temporal_column_additions_on_mysql()
    {
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));
        using var targetContext = new TemporalSchemaWithDescriptionContext(
            CreateOptions<TemporalSchemaWithDescriptionContext>(serverVersion));
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        var dropTrigger = sql.IndexOf("DROP TRIGGER", StringComparison.Ordinal);
        var alterCurrent = sql.IndexOf(
            "ALTER TABLE `TemporalRecords` ADD `Description`",
            StringComparison.Ordinal);
        var alterHistory = sql.IndexOf(
            "ALTER TABLE `TemporalRecordsHistory` ADD `Description`",
            StringComparison.Ordinal);
        var createTrigger = sql.IndexOf("CREATE TRIGGER", StringComparison.Ordinal);

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
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));
        using var targetContext = new TemporalSchemaWithRenamedColumnContext(
            CreateOptions<TemporalSchemaWithRenamedColumnContext>(serverVersion));
        var sql = GenerateMigrationSql(sourceContext, targetContext);

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
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));
        using var targetContext = new TemporalSchemaWithBoundedNameContext(
            CreateOptions<TemporalSchemaWithBoundedNameContext>(serverVersion));
        var sql = GenerateMigrationSql(sourceContext, targetContext);

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
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaWithDescriptionContext(
            CreateOptions<TemporalSchemaWithDescriptionContext>(serverVersion));
        using var targetContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));
        var operations = GetDifferences(sourceContext, targetContext);
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

        var commands = targetContext
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, targetContext.Model);
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
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));
        using var targetContext = new TemporalSchemaWithGeneratedColumnContext(
            CreateOptions<TemporalSchemaWithGeneratedColumnContext>(serverVersion));
        var sql = GenerateMigrationSql(sourceContext, targetContext);

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
    /// Verifies that native MariaDB temporal history is never made inaccurate
    /// through the engine's permissive system-versioning alteration mode.
    /// </summary>
    [Theory]
    [InlineData(NativeTemporalSchemaChange.AddColumn)]
    [InlineData(NativeTemporalSchemaChange.RenameColumn)]
    [InlineData(NativeTemporalSchemaChange.AlterColumn)]
    [InlineData(NativeTemporalSchemaChange.DropColumn)]
    [InlineData(NativeTemporalSchemaChange.RenameTable)]
    public void Migrations_sql_generator_rejects_unsafe_native_temporal_schema_changes_on_mariadb(
        NativeTemporalSchemaChange schemaChange
    )
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        var contexts = CreateNativeTemporalSchemaChangeContexts(schemaChange, serverVersion);

        using var sourceContext = contexts.Source;
        using var targetContext = contexts.Target;

        var exception = Assert.Throws<InvalidOperationException>(
            () => GenerateMigrationSql(sourceContext, targetContext));

        Assert.Contains("native MariaDB temporal table", exception.Message, StringComparison.Ordinal);
        Assert.Contains("history", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that MariaDB temporal activation and deactivation are complete
    /// native transitions and never introduce the MySQL emulation artifacts.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_materializes_native_temporal_transitions_on_mariadb()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var nonTemporalContext = new NonTemporalSchemaContext(
            CreateOptions<NonTemporalSchemaContext>(serverVersion));
        using var temporalContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        var enableOperations = GetDifferences(nonTemporalContext, temporalContext);
        var disableOperations = GetDifferences(temporalContext, nonTemporalContext);
        var enableSql = GenerateMigrationSql(temporalContext, enableOperations);
        var disableSql = GenerateMigrationSql(nonTemporalContext, disableOperations);
        var finalPeriodColumn = enableSql.IndexOf(
            "ADD `ValidTo` timestamp(6) GENERATED ALWAYS AS ROW END",
            StringComparison.Ordinal);
        var periodActivation = enableSql.IndexOf(
            "ADD PERIOD FOR SYSTEM_TIME (`ValidFrom`, `ValidTo`)",
            StringComparison.Ordinal);
        var systemVersioningDeactivation = disableSql.IndexOf(
            "DROP SYSTEM VERSIONING",
            StringComparison.Ordinal);
        var periodDeactivation = disableSql.IndexOf(
            "DROP PERIOD FOR SYSTEM_TIME",
            StringComparison.Ordinal);
        var firstPeriodColumnDrop = disableSql.IndexOf(
            "DROP COLUMN `ValidFrom`",
            StringComparison.Ordinal);

        Assert.Contains(
            "ALTER TABLE `TemporalRecords` "
            + "ADD `ValidFrom` timestamp(6) GENERATED ALWAYS AS ROW START, "
            + "ADD `ValidTo` timestamp(6) GENERATED ALWAYS AS ROW END, "
            + "ADD PERIOD FOR SYSTEM_TIME (`ValidFrom`, `ValidTo`), "
            + "ADD SYSTEM VERSIONING;",
            enableSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ADD `ValidTo` timestamp(6) GENERATED ALWAYS AS ROW END",
            enableSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ADD PERIOD FOR SYSTEM_TIME (`ValidFrom`, `ValidTo`)",
            enableSql,
            StringComparison.Ordinal);
        Assert.True(periodActivation > finalPeriodColumn);
        Assert.Contains("ADD SYSTEM VERSIONING", enableSql, StringComparison.Ordinal);
        Assert.Contains(
            "SET STATEMENT system_versioning_alter_history=KEEP FOR "
            + "ALTER TABLE `TemporalRecords` DROP SYSTEM VERSIONING, DROP PERIOD FOR SYSTEM_TIME, "
            + "DROP COLUMN `ValidFrom`, DROP COLUMN `ValidTo`;",
            disableSql,
            StringComparison.Ordinal);
        Assert.True(
            disableOperations
                .OfType<AlterTableOperation>()
                .Single()
                .IsDestructiveChange);
        Assert.True(periodDeactivation > systemVersioningDeactivation);
        Assert.True(firstPeriodColumnDrop > periodDeactivation);
        Assert.Contains("DROP COLUMN `ValidFrom`", disableSql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `ValidTo`", disableSql, StringComparison.Ordinal);
        Assert.Equal(
            firstPeriodColumnDrop,
            disableSql.LastIndexOf("DROP COLUMN `ValidFrom`", StringComparison.Ordinal));
        Assert.DoesNotContain("TemporalRecordsHistory", enableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("TemporalRecordsHistory", disableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("TRIGGER", enableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("TRIGGER", disableSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that renaming an emulated temporal table also renames its
    /// history table and rebinds every provider-owned trigger.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_renames_complete_temporal_contract_on_mysql()
    {
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));
        using var targetContext = new RenamedTemporalSchemaContext(
            CreateOptions<RenamedTemporalSchemaContext>(serverVersion));
        var sql = GenerateMigrationSql(sourceContext, targetContext);

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
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var sourceContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));
        using var targetContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));
        var sql = GenerateMigrationSql(sourceContext, targetContext);

        Assert.Contains("DROP TABLE `TemporalRecords`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `TemporalRecordsHistory`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that enabling and disabling temporal behavior is represented by
    /// complete physical contracts rather than annotation-only migrations.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_materializes_temporal_transitions_on_mysql()
    {
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        using var nonTemporalContext = new NonTemporalSchemaContext(
            CreateOptions<NonTemporalSchemaContext>(serverVersion));
        using var temporalContext = new TemporalSchemaContext(
            CreateOptions<TemporalSchemaContext>(serverVersion));

        var enableSql = GenerateMigrationSql(nonTemporalContext, temporalContext);
        var disableSql = GenerateMigrationSql(temporalContext, nonTemporalContext);
        var finalPeriodColumn = enableSql.IndexOf(
            "ADD `ValidTo` datetime(6) NOT NULL DEFAULT '9999-12-31 23:59:59.999999'",
            StringComparison.Ordinal);
        var historyActivation = enableSql.IndexOf(
            "CREATE TABLE `TemporalRecordsHistory`",
            StringComparison.Ordinal);

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
        Assert.Contains("DROP TRIGGER", disableSql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `TemporalRecordsHistory`", disableSql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `ValidFrom`", disableSql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `ValidTo`", disableSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the initial migration path still carries the configured database charset annotation.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_emits_alter_database_charset_for_initial_migration()
    {
        using var targetContext = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var operations = differ.GetDifferences(
            null,
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());

        var alterDatabase = Assert.Single(operations.OfType<AlterDatabaseOperation>());
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());

        Assert.Equal(
            "utf8mb4",
            alterDatabase.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value);
        Assert.Equal(
            "utf8mb4",
            createTable.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value);
    }

    /// <summary>
    /// Verifies that the migrations SQL generator emits the narrow charset and engine contract.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_emits_narrow_mysql_specific_table_and_database_options()
    {
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var migrationsSqlGenerator = targetContext.GetService<IMigrationsSqlGenerator>();
        var operations = differ.GetDifferences(
            sourceContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel(),
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());
        var commands = migrationsSqlGenerator.Generate(operations, targetContext.Model);
        var sql = string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

        Assert.Contains("ALTER DATABASE CHARACTER SET = utf8mb4;", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE `MigrationDslEntities`", sql, StringComparison.Ordinal);
        Assert.Contains("CHARACTER SET utf8mb4", sql, StringComparison.Ordinal);
        Assert.Contains("ENGINE = InnoDB", sql, StringComparison.Ordinal);
        Assert.Contains("`ExternalId` char(36) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX `IX_MigrationDsl_Name_Code` "
            + "ON `MigrationDslEntities` (`Name`(32), `Code` DESC)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE FULLTEXT INDEX `IX_MigrationDsl_Body` ON `MigrationDslEntities` (`Body`)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE SPATIAL INDEX `IX_MigrationDsl_Location` ON `MigrationDslEntities` (`Location`)",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that scaffold/code-generation services emit typed fluent APIs instead of raw annotation names.
    /// </summary>
    [Fact]
    public void Annotation_code_generator_emits_typed_mysql_specific_fluent_api_calls()
    {
        using var context = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        using var serviceProvider = CreateDesignTimeServiceProvider();
        var codeGenerator = serviceProvider.GetRequiredService<IAnnotationCodeGenerator>();
        var entityType = context.Model.FindEntityType(typeof(MigrationDslEntity))!;
        var property = entityType.FindProperty(nameof(MigrationDslEntity.ExternalId))!;
        var prefixIndex = entityType
            .GetIndexes()
            .Single(index => index.GetDatabaseName() == "IX_MigrationDsl_Name_Code");
        var fullTextIndex = entityType
            .GetIndexes()
            .Single(index => index.GetDatabaseName() == "IX_MigrationDsl_Body");

        var modelAnnotations = context
            .Model.GetAnnotations()
            .ToDictionary(annotation => annotation.Name);
        var entityAnnotations = entityType
            .GetAnnotations()
            .ToDictionary(annotation => annotation.Name);
        var propertyAnnotations = property
            .GetAnnotations()
            .ToDictionary(annotation => annotation.Name);
        var modelCalls = codeGenerator.GenerateFluentApiCalls(context.Model, modelAnnotations);
        var entityCalls = codeGenerator.GenerateFluentApiCalls(entityType, entityAnnotations);
        var propertyCalls = codeGenerator.GenerateFluentApiCalls(property, propertyAnnotations);
        var prefixIndexCalls = codeGenerator.GenerateFluentApiCalls(
            prefixIndex,
            prefixIndex.GetAnnotations().ToDictionary(annotation => annotation.Name));
        var fullTextIndexCalls = codeGenerator.GenerateFluentApiCalls(
            fullTextIndex,
            fullTextIndex.GetAnnotations().ToDictionary(annotation => annotation.Name));

        Assert.Contains(
            modelCalls,
            fragment => fragment.Method == nameof(MySqlModelBuilderExtensions.HasCharSet)
                && Equals(fragment.Arguments.Single(), "utf8mb4"));
        Assert.Contains(
            entityCalls,
            fragment => fragment.Method == nameof(MySqlEntityTypeBuilderExtensions.HasCharSet)
                && Equals(fragment.Arguments.Single(), "utf8mb4"));
        Assert.Contains(
            entityCalls,
            fragment => fragment.Method == nameof(MySqlEntityTypeBuilderExtensions.UseStorageEngine)
                && Equals(fragment.Arguments.Single(), "InnoDB"));
        Assert.Contains(
            propertyCalls,
            fragment => fragment.Method == nameof(MySqlPropertyBuilderExtensions.HasMySqlGuidFormat)
                && Equals(fragment.Arguments.Single(), MySqlGuidFormat.Char36));
        Assert.Contains(
            prefixIndexCalls,
            fragment => fragment.Method == nameof(MySqlIndexBuilderExtensions.HasPrefixLength)
                && fragment.Arguments.SequenceEqual(s_indexPrefixLengths.Cast<object>()));
        Assert.Contains(
            fullTextIndexCalls,
            fragment => fragment.Method == nameof(MySqlIndexBuilderExtensions.IsFullText)
                && fragment.Arguments.Count == 0);
    }

    /// <summary>
    /// Verifies that a new auto-increment primary key exists before the column gains
    /// AUTO_INCREMENT.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_adds_primary_key_before_enabling_auto_increment()
    {
        using var source = new KeylessPeopleContext(CreateOptions<KeylessPeopleContext>());
        using var target = new KeyedPeopleContext(CreateOptions<KeyedPeopleContext>());
        var operations = GetDifferences(source, target);
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
        using var source = new KeyedPeopleContext(CreateOptions<KeyedPeopleContext>());
        using var target = new KeylessPeopleContext(CreateOptions<KeylessPeopleContext>());
        var operations = GetDifferences(source, target);
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
        using var source = new KeyedPeopleContext(CreateOptions<KeyedPeopleContext>());
        using var target = new KeyedPersonsContext(CreateOptions<KeyedPersonsContext>());
        var operations = GetDifferences(source, target);

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
        using var source = new KeyedPeopleWithReplacementContext(CreateOptions<KeyedPeopleWithReplacementContext>());
        using var target = new ReplacementPeopleContext(CreateOptions<ReplacementPeopleContext>());
        var operations = GetDifferences(source, target);

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
        using var target = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var firstAlter = CreateJsonAlterColumn(typeof(string), isUnicode: true, maxLength: 255);
        var secondAlter = CreateJsonAlterColumn(typeof(JsonDocument), isUnicode: null, maxLength: null);
        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(firstAlter, secondAlter));
        var operations = differ.GetDifferences(
            null,
            target
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());
        var alterColumn = Assert.Single(operations.OfType<AlterColumnOperation>());

        Assert.Equal("Entity", alterColumn.Table);
        Assert.Equal("Name", alterColumn.Name);
        Assert.Equal("json", alterColumn.ColumnType);
        Assert.Equal("longtext", alterColumn.OldColumn.ColumnType);
    }

    private static List<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            target.GetService<IDesignTimeModel>().Model.GetRelationalModel())
        .ToList();

    private static string GenerateMigrationSql(
        DbContext source,
        DbContext target
    )
    {
        var operations = GetDifferences(source, target);

        return GenerateMigrationSql(target, operations);
    }

    private static string GenerateMigrationSql(
        DbContext target,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var commands = target
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, target.Model);

        return string.Join(
            Environment.NewLine,
            commands.Select(command => command.CommandText));
    }

    public enum NativeTemporalSchemaChange
    {
        AddColumn,
        RenameColumn,
        AlterColumn,
        DropColumn,
        RenameTable,
    }

    private static (DbContext Source, DbContext Target) CreateNativeTemporalSchemaChangeContexts(
        NativeTemporalSchemaChange schemaChange,
        MySqlServerVersion serverVersion
    ) => schemaChange switch
    {
        NativeTemporalSchemaChange.AddColumn => (
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion)),
            new TemporalSchemaWithDescriptionContext(
                CreateOptions<TemporalSchemaWithDescriptionContext>(serverVersion))),
        NativeTemporalSchemaChange.RenameColumn => (
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion)),
            new TemporalSchemaWithRenamedColumnContext(
                CreateOptions<TemporalSchemaWithRenamedColumnContext>(serverVersion))),
        NativeTemporalSchemaChange.AlterColumn => (
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion)),
            new TemporalSchemaWithBoundedNameContext(
                CreateOptions<TemporalSchemaWithBoundedNameContext>(serverVersion))),
        NativeTemporalSchemaChange.DropColumn => (
            new TemporalSchemaWithDescriptionContext(
                CreateOptions<TemporalSchemaWithDescriptionContext>(serverVersion)),
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion))),
        NativeTemporalSchemaChange.RenameTable => (
            new TemporalSchemaContext(CreateOptions<TemporalSchemaContext>(serverVersion)),
            new RenamedTemporalSchemaContext(
                CreateOptions<RenamedTemporalSchemaContext>(serverVersion))),
        _ => throw new ArgumentOutOfRangeException(nameof(schemaChange), schemaChange, null),
    };

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    => CreateOptions<TContext>(MySqlServerVersion.MySql(new Version(8, 4, 0)));

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();

        builder.UseMySql(
            "Server=localhost;Database=phase2;User ID=root;Password=password;",
            serverVersion,
            providerOptions => providerOptions.UseNetTopologySuite());

        return builder.Options;
    }

    private static ServiceProvider CreateDesignTimeServiceProvider()
    {
        var services = new ServiceCollection();
#pragma warning disable EF1001
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, _ => { }, _ => { }, _ => { }));
#pragma warning restore EF1001

        services.AddEntityFrameworkDesignTimeServices(reporter, () => new ServiceCollection().BuildServiceProvider());
        services.AddEntityFrameworkDokaMySqlDesignTime();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class EmptyMigrationDslContext : DbContext
    {
        public EmptyMigrationDslContext(
            DbContextOptions options
        ) : base(options) { }
    }

    private sealed class MigrationDslContext : DbContext
    {
        public MigrationDslContext(
            DbContextOptions options
        ) : base(options) { }

        public DbSet<MigrationDslEntity> MigrationDslEntities => Set<MigrationDslEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");

            modelBuilder.Entity<MigrationDslEntity>(entity =>
            {
                entity.HasCharSet("utf8mb4");
                entity.UseStorageEngine("InnoDB");
                entity
                    .Property(item => item.ExternalId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .HasIndex(item => new
                    {
                        item.Name,
                        item.Code,
                    })
                    .HasDatabaseName("IX_MigrationDsl_Name_Code")
                    .HasPrefixLength(s_indexPrefixLengths)
                    .IsDescending(s_mixedIndexDirections);
                entity
                    .HasIndex(item => item.Body)
                    .HasDatabaseName("IX_MigrationDsl_Body")
                    .IsFullText();
                entity
                    .Property(item => item.Location)
                    .HasColumnType("point");
                entity
                    .HasIndex(item => item.Location)
                    .HasDatabaseName("IX_MigrationDsl_Location")
                    .IsSpatial();
            });
        }
    }

    private sealed class TemporalMigrationDslContext : DbContext
    {
        public TemporalMigrationDslContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<MigrationDslEntity>(entity =>
        {
            entity.ToTable(
                "MigrationDsl",
                table => table.IsTemporal(temporal =>
                {
                    temporal.UseHistoryTable("MigrationDslHistory");
                    temporal.HasPeriodStart("ValidFrom");
                    temporal.HasPeriodEnd("ValidTo");
                }));
        });
    }

    private sealed class NonTemporalSchemaContext : DbContext
    {
        public NonTemporalSchemaContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(modelBuilder, "TemporalRecords", "TemporalRecordsHistory");
    }

    private sealed class TemporalSchemaContext : DbContext
    {
        public TemporalSchemaContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true);
    }

    private sealed class TemporalSchemaWithDescriptionContext : DbContext
    {
        public TemporalSchemaWithDescriptionContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true,
            includeDescription: true);
    }

    private sealed class TemporalSchemaWithRenamedColumnContext : DbContext
    {
        public TemporalSchemaWithRenamedColumnContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true,
            nameColumn: "DisplayName");
    }

    private sealed class TemporalSchemaWithBoundedNameContext : DbContext
    {
        public TemporalSchemaWithBoundedNameContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true,
            nameMaxLength: 128);
    }

    private sealed class TemporalSchemaWithGeneratedColumnContext : DbContext
    {
        public TemporalSchemaWithGeneratedColumnContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalRecords",
            "TemporalRecordsHistory",
            temporal: true,
            includeGeneratedNameLength: true);
    }

    private sealed class RenamedTemporalSchemaContext : DbContext
    {
        public RenamedTemporalSchemaContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalSchema(
            modelBuilder,
            "TemporalEntries",
            "TemporalEntriesHistory",
            temporal: true);
    }

    private sealed class KeylessPeopleContext : DbContext
    {
        public KeylessPeopleContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigurePeople(modelBuilder, "People", hasKey: false, propertyName: "SomeField");
    }

    private sealed class KeyedPeopleContext : DbContext
    {
        public KeyedPeopleContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigurePeople(modelBuilder, "People", hasKey: true, propertyName: "SomeField");
    }

    private sealed class KeyedPersonsContext : DbContext
    {
        public KeyedPersonsContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigurePeople(modelBuilder, "Persons", hasKey: true, propertyName: "SomeField");
    }

    private sealed class KeyedPeopleWithReplacementContext : DbContext
    {
        public KeyedPeopleWithReplacementContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigurePeople(modelBuilder, "People", hasKey: true, propertyName: "SomeField");
            modelBuilder
                .Entity("Person")
                .Property<int>("ReplacementField");
        }
    }

    private sealed class ReplacementPeopleContext : DbContext
    {
        public ReplacementPeopleContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigurePeople(modelBuilder, "People", hasKey: false, propertyName: "ReplacementField");
    }

    private static AlterColumnOperation CreateJsonAlterColumn(
        Type clrType,
        bool? isUnicode,
        int? maxLength
    ) => new()
    {
        Name = "Name",
        Table = "Entity",
        ClrType = clrType,
        ColumnType = "json",
        IsUnicode = isUnicode,
        MaxLength = maxLength,
        IsNullable = true,
        OldColumn = new AddColumnOperation
        {
            Name = "Name",
            Table = "Entity",
            ClrType = typeof(string),
            ColumnType = "longtext",
            IsUnicode = true,
            IsNullable = true,
        },
    };

    private sealed class FixedMigrationsModelDiffer : IMigrationsModelDiffer
    {
        private readonly MigrationOperation[] _operations;

        public FixedMigrationsModelDiffer(
            params MigrationOperation[] operations
        )
        {
            _operations = operations;
        }

        public bool HasDifferences(
            IRelationalModel? source,
            IRelationalModel? target
        ) => _operations.Length > 0;

        public IReadOnlyList<MigrationOperation> GetDifferences(
            IRelationalModel? source,
            IRelationalModel? target
        ) => _operations;
    }

    private static void ConfigurePeople(
        ModelBuilder modelBuilder,
        string tableName,
        bool hasKey,
        string propertyName
    )
    {
        modelBuilder.Entity(
            "Person",
            entity =>
            {
                entity.ToTable(tableName);
                entity.Property<int>(propertyName);

                if (hasKey)
                {
                    entity.HasKey(propertyName);
                }
                else
                {
                    entity.HasNoKey();
                }
            });
    }

    private static void ConfigureTemporalSchema(
        ModelBuilder modelBuilder,
        string tableName,
        string historyTableName,
        bool temporal = false,
        bool includeDescription = false,
        string nameColumn = "Name",
        int? nameMaxLength = null,
        bool includeGeneratedNameLength = false
    )
    {
        modelBuilder.Entity<TemporalSchemaEntity>(entity =>
        {
            if (temporal)
            {
                entity.ToTable(
                    tableName,
                    table => table.IsTemporal(temporalTable =>
                    {
                        temporalTable.UseHistoryTable(historyTableName);
                        temporalTable.HasPeriodStart("ValidFrom");
                        temporalTable.HasPeriodEnd("ValidTo");
                    }));
            }
            else
            {
                entity.ToTable(tableName);
            }

            var nameProperty = entity
                .Property(item => item.Name)
                .HasColumnName(nameColumn);

            if (nameMaxLength is not null)
            {
                nameProperty.HasMaxLength(nameMaxLength.Value);
            }

            if (!includeDescription)
            {
                entity.Ignore(item => item.Description);
            }

            if (includeGeneratedNameLength)
            {
                entity
                    .Property(item => item.NameLength)
                    .HasComputedColumnSql("CHAR_LENGTH(`Name`)", stored: true);
            }
            else
            {
                entity.Ignore(item => item.NameLength);
            }
        });
    }

    private sealed class TemporalSchemaEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public int NameLength { get; set; }
    }

    private sealed class MigrationDslEntity
    {
        public int Id { get; set; }

        public Guid ExternalId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public Point Location { get; set; } = new(0, 0);
    }
}
