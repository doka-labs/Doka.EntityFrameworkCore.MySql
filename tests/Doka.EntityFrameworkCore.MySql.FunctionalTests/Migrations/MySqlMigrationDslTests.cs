namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies provider migration metadata and SQL contracts.
/// </summary>
public sealed partial class MySqlMigrationDslTests
{
    private static readonly int[] s_indexPrefixLengths = [32, 0];

    private static readonly int[] s_singlePrefixLength = [16];

    private static readonly int[] s_negativePrefixLengths = [-1, 0];

    private static readonly bool[] s_mixedIndexDirections = [false, true];

    /// <summary>
    /// Verifies public MySQL specific fluent APIs stamp expected metadata annotations.
    /// </summary>
    [Fact]
    public void Public_mysql_specific_fluent_apis_stamp_expected_metadata_annotations()
    {
        // Arrange
        using var context = new MigrationDslContext(CreateOptions<MigrationDslContext>());

        // Act
        var entityType = context.Model.FindEntityType(typeof(MigrationDslEntity));
        var property = entityType?.FindProperty(nameof(MigrationDslEntity.ExternalId));
        var prefixIndex = entityType
            ?.GetIndexes()
            .Single(index => index.GetDatabaseName() == "IX_MigrationDsl_Name_Code");

        var fullTextIndex = entityType
            ?.GetIndexes()
            .Single(index => index.GetDatabaseName() == "IX_MigrationDsl_Body");

        // Assert
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
    /// Verifies that each indexed property requires its own prefix-length entry.
    /// </summary>
    [Fact]
    public void Public_index_fluent_api_rejects_incomplete_prefix_lengths()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var indexBuilder = modelBuilder
            .Entity<MigrationDslEntity>()
            .HasIndex(entity => new
            {
                entity.Name,
                entity.Code,
            });

        // Act
        var exception = Assert.Throws<ArgumentException>(
            () => indexBuilder.HasPrefixLength(s_singlePrefixLength));

        // Assert
        Assert.Equal("prefixLengths", exception.ParamName);
    }

    /// <summary>
    /// Verifies that prefix lengths cannot be negative.
    /// </summary>
    [Fact]
    public void Public_index_fluent_api_rejects_negative_prefix_lengths()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var indexBuilder = modelBuilder
            .Entity<MigrationDslEntity>()
            .HasIndex(entity => new
            {
                entity.Name,
                entity.Code,
            });

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => indexBuilder.HasPrefixLength(s_negativePrefixLengths));

        // Assert
        Assert.Equal("prefixLengths", exception.ParamName);
    }

    /// <summary>
    /// Verifies that the migrations model differ carries the narrow MySQL-specific annotations into operations.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_carries_mysql_specific_annotations_into_operations()
    {
        // Arrange
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();

        // Act
        var operations = differ.GetDifferences(
            sourceContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel(),
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());

        // Assert
        var alterDatabase = Assert.Single(operations.OfType<AlterDatabaseOperation>());
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var externalIdColumn = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(MigrationDslEntity.ExternalId));

        var idColumn = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(MigrationDslEntity.Id));

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
            MySqlGuidFormat.Char36,
            externalIdColumn.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            idColumn.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Equal(
            s_indexPrefixLengths,
            prefixIndex.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength)
                ?.Value as int[]);
        Assert.Equal(
            s_indexPrefixLengths,
            prefixIndex.GetMySqlMigrationMetadata().IndexPrefixLengths);
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
    /// Verifies that the migrations SQL generator emits the narrow charset and engine contract.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_emits_narrow_mysql_specific_table_and_database_options()
    {
        // Arrange
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var migrationsSqlGenerator = targetContext.GetService<IMigrationsSqlGenerator>();

        // Act
        var operations = differ.GetDifferences(
            sourceContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel(),
            targetContext
                .GetService<IDesignTimeModel>()
                .Model.GetRelationalModel());

        var commands = migrationsSqlGenerator.Generate(operations, targetContext.Model);
        var sql = string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

        // Assert
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
        // Arrange
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

        // Act
        var modelCalls = codeGenerator.GenerateFluentApiCalls(context.Model, modelAnnotations);
        var entityCalls = codeGenerator.GenerateFluentApiCalls(entityType, entityAnnotations);
        var propertyCalls = codeGenerator.GenerateFluentApiCalls(property, propertyAnnotations);
        var prefixIndexCalls = codeGenerator.GenerateFluentApiCalls(
            prefixIndex,
            prefixIndex.GetAnnotations().ToDictionary(annotation => annotation.Name));

        var fullTextIndexCalls = codeGenerator.GenerateFluentApiCalls(
            fullTextIndex,
            fullTextIndex.GetAnnotations().ToDictionary(annotation => annotation.Name));

        // Assert
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
}
