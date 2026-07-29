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

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();

        builder.UseMySql(
            "Server=localhost;Database=phase2;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
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
