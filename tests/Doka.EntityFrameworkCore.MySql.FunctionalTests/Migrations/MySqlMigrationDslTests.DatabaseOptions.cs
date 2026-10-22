namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// Verifies that the initial migration path still carries the configured database charset annotation.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_emits_alter_database_charset_for_initial_migration()
    {
        // Arrange
        using var targetContext = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();

        // Act
        var operations = differ.GetDifferences(
            null,
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());

        // Assert
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
    /// The initial migration carries the canonical database collation without
    /// turning it into a table-level override.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_preserves_initial_database_collation()
    {
        // Arrange
        using var targetContext = new Utf8DatabaseOptionsContext(
            CreateOptions<Utf8DatabaseOptionsContext>());

        // Act
        var operations = GetDifferences(targetContext);
        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var alterDatabase = Assert.Single(operations.OfType<AlterDatabaseOperation>());
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());

        Assert.Equal("utf8mb4_unicode_ci", alterDatabase.Collation);
        Assert.Null(alterDatabase.OldDatabase.Collation);
        Assert.Equal(
            "utf8mb4",
            alterDatabase.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value);
        Assert.Null(alterDatabase.OldDatabase.FindAnnotation(MySqlAnnotationNames.CharSet));
        Assert.Null(createTable.FindAnnotation(RelationalAnnotationNames.Collation));
        Assert.Null(createTable.FindAnnotation(MySqlAnnotationNames.Collation));
        Assert.True(operations.IndexOf(alterDatabase) < operations.IndexOf(createTable));

        Assert.Contains(
            "ALTER DATABASE CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci;",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CREATE TABLE `DatabaseOptionEntities` (\n) COLLATE",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Both sides of a database-default transition survive operation and C#
    /// migration generation so the inverse is exact.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_preserves_database_options_for_up_and_down()
    {
        // Arrange
        using var sourceContext = new Latin1DatabaseOptionsContext(
            CreateOptions<Latin1DatabaseOptionsContext>());

        using var targetContext = new Utf8DatabaseOptionsContext(
            CreateOptions<Utf8DatabaseOptionsContext>());

        // Act
        var upOperations = GetDifferences(sourceContext, targetContext);
        var downOperations = GetDifferences(targetContext, sourceContext);
        var migrationCode = GenerateMigrationCode(upOperations, downOperations);

        // Assert
        var up = Assert.Single(upOperations.OfType<AlterDatabaseOperation>());
        var down = Assert.Single(downOperations.OfType<AlterDatabaseOperation>());

        AssertDatabaseOptions(
            up,
            "utf8mb4",
            "utf8mb4_unicode_ci",
            "latin1",
            "latin1_swedish_ci");
        AssertDatabaseOptions(
            down,
            "latin1",
            "latin1_swedish_ci",
            "utf8mb4",
            "utf8mb4_unicode_ci");

        Assert.Contains("collation: \"utf8mb4_unicode_ci\"", migrationCode, StringComparison.Ordinal);
        Assert.Contains("oldCollation: \"latin1_swedish_ci\"", migrationCode, StringComparison.Ordinal);
        Assert.Contains(
            $".Annotation(\"{MySqlAnnotationNames.CharSet}\", \"utf8mb4\")",
            migrationCode,
            StringComparison.Ordinal);
        Assert.Contains(
            $".OldAnnotation(\"{MySqlAnnotationNames.CharSet}\", \"latin1\")",
            migrationCode,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A collation-only model transition creates executable SQL even when no
    /// provider character-set annotation is configured.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_emits_collation_only_transition()
    {
        // Arrange
        using var sourceContext = new UnicodeCollationOnlyContext(
            CreateOptions<UnicodeCollationOnlyContext>());

        using var targetContext = new BinaryCollationOnlyContext(
            CreateOptions<BinaryCollationOnlyContext>());

        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        // Act
        var hasDifferences = differ.HasDifferences(sourceModel, targetModel);

        var operations = differ.GetDifferences(sourceModel, targetModel);
        var commands = targetContext
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, targetContext.Model);

        // Assert
        Assert.True(hasDifferences);
        var operation = Assert.Single(operations.OfType<AlterDatabaseOperation>());

        AssertDatabaseOptions(
            operation,
            targetCharSet: null,
            "utf8mb4_bin",
            sourceCharSet: null,
            "utf8mb4_unicode_ci");
        Assert.Equal(
            "ALTER DATABASE COLLATE = utf8mb4_bin;\n",
            Assert.Single(commands).CommandText);
    }

    /// <summary>
    /// Removing the last explicit database default remains visible in the
    /// operation but fails before a mutable server default can be selected.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_rejects_implicit_database_default_reset()
    {
        // Arrange
        using var sourceContext = new UnicodeCollationOnlyContext(
            CreateOptions<UnicodeCollationOnlyContext>());

        using var targetContext = new DatabaseOptionsUnsetContext(
            CreateOptions<DatabaseOptionsUnsetContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        var exception = Assert.Throws<InvalidOperationException>(
            () => GenerateMigrationSql(targetContext, operations));

        // Assert
        var operation = Assert.Single(operations.OfType<AlterDatabaseOperation>());

        Assert.Null(operation.Collation);
        Assert.Equal("utf8mb4_unicode_ci", operation.OldDatabase.Collation);

        Assert.Contains("explicit target value", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Explicit table collations use the canonical relational annotation and
    /// remain independent of the database default.
    /// </summary>
    [Theory]
    [InlineData(false, "utf8mb4_unicode_ci")]
    [InlineData(true, "utf8mb4_bin")]
    public void Migrations_model_differ_preserves_explicit_table_collation(
        bool useDifferentCollation,
        string expectedCollation
    )
    {
        // Arrange
        using DbContext targetContext = useDifferentCollation
            ? new DifferentTableCollationContext(CreateOptions<DifferentTableCollationContext>())
            : new MatchingTableCollationContext(CreateOptions<MatchingTableCollationContext>());

        // Act
        var operations = GetDifferences(targetContext);
        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());

        Assert.Equal(
            expectedCollation,
            createTable.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);
        Assert.Null(createTable.FindAnnotation(MySqlAnnotationNames.Collation));

        Assert.Contains($"COLLATE {expectedCollation}", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// An explicit table-default transition changes only the table default and
    /// does not request conversion of existing textual columns.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_preserves_explicit_table_collation_transition()
    {
        // Arrange
        using var sourceContext = new MatchingTableCollationContext(
            CreateOptions<MatchingTableCollationContext>());

        using var targetContext = new DifferentTableCollationContext(
            CreateOptions<DifferentTableCollationContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);
        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var operation = Assert.Single(operations.OfType<AlterTableOperation>());

        Assert.Equal(
            "utf8mb4_bin",
            operation.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);
        Assert.Equal(
            "utf8mb4_unicode_ci",
            operation.OldTable.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);

        Assert.Contains(
            "ALTER TABLE `DatabaseOptionEntities` DEFAULT COLLATE = utf8mb4_bin;",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CONVERT TO CHARACTER SET", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("MODIFY COLUMN", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Removing a table override writes the target model's database default
    /// explicitly instead of relying on server-specific DEFAULT semantics.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_resets_table_collation_to_database_default()
    {
        // Arrange
        using var sourceContext = new DifferentTableCollationContext(
            CreateOptions<DifferentTableCollationContext>());

        using var targetContext = new Utf8DatabaseOptionsContext(
            CreateOptions<Utf8DatabaseOptionsContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);
        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var operation = Assert.Single(operations.OfType<AlterTableOperation>());

        Assert.Equal(
            "utf8mb4_unicode_ci",
            operation.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);
        Assert.Equal(
            "utf8mb4_bin",
            operation.OldTable.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);

        Assert.Contains(
            "ALTER TABLE `DatabaseOptionEntities` DEFAULT COLLATE = utf8mb4_unicode_ci;",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A change to the public storage-engine model option reaches executable
    /// table DDL instead of being reduced to inert migration metadata.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_preserves_storage_engine_transition()
    {
        // Arrange
        using var sourceContext = new MyIsamTableOptionsContext(
            CreateOptions<MyIsamTableOptionsContext>());

        using var targetContext = new InnoDbTableOptionsContext(
            CreateOptions<InnoDbTableOptionsContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);
        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var operation = Assert.Single(operations.OfType<AlterTableOperation>());

        Assert.Equal(
            "InnoDB",
            operation.FindAnnotation(MySqlAnnotationNames.StorageEngine)?.Value);
        Assert.Equal(
            "MyISAM",
            operation.OldTable.FindAnnotation(MySqlAnnotationNames.StorageEngine)?.Value);

        Assert.Contains(
            "ALTER TABLE `DatabaseOptionEntities` ENGINE = InnoDB;",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Removing explicit table character-set metadata resolves both table
    /// defaults from the target database model and emits one compatible pair.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_resets_table_charset_and_collation_to_database_defaults()
    {
        // Arrange
        using var sourceContext = new Latin1TableOptionsContext(
            CreateOptions<Latin1TableOptionsContext>());

        using var targetContext = new MatchingTableCollationContext(
            CreateOptions<MatchingTableCollationContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);
        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var operation = Assert.Single(operations.OfType<AlterTableOperation>());

        Assert.Equal(
            "utf8mb4",
            operation.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value);
        Assert.Equal(
            "latin1",
            operation.OldTable.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value);
        Assert.Equal(
            "utf8mb4_unicode_ci",
            operation.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);
        Assert.Equal(
            "latin1_swedish_ci",
            operation.OldTable.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);

        Assert.Contains(
            "ALTER TABLE `DatabaseOptionEntities` DEFAULT CHARACTER SET = utf8mb4 "
            + "DEFAULT COLLATE = utf8mb4_unicode_ci;",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CONVERT TO CHARACTER SET", sql, StringComparison.Ordinal);
    }
}
