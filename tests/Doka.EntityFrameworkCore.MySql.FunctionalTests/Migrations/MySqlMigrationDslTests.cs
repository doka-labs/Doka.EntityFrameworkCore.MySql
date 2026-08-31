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
    /// Changing provider-owned index metadata rebuilds the physical index.
    /// </summary>
    [Theory]
    [InlineData(IndexPrefixTransition.Change)]
    [InlineData(IndexPrefixTransition.Add)]
    [InlineData(IndexPrefixTransition.Remove)]
    public void Migrations_model_differ_rebuilds_indexes_for_prefix_length_transitions(
        IndexPrefixTransition transition
    )
    {
        using var sourceContext = CreateIndexPrefixContext(transition, source: true);
        using var targetContext = CreateIndexPrefixContext(transition, source: false);
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        Assert.True(differ.HasDifferences(sourceModel, targetModel));

        var operations = differ
            .GetDifferences(sourceModel, targetModel)
            .ToList();
        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.Equal(IndexPrefixContract.IndexName, drop.Name);
        Assert.Equal(IndexPrefixContract.TableName, drop.Table);
        Assert.Equal(IndexPrefixContract.IndexName, create.Name);
        Assert.Equal(IndexPrefixContract.TableName, create.Table);
        Assert.Equal(
            transition is IndexPrefixTransition.Change or IndexPrefixTransition.Add
                ? IndexPrefixContract.TargetPrefixLengths
                : null,
            create.GetMySqlMigrationMetadata().IndexPrefixLengths);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(create));

        var sql = GenerateMigrationSql(targetContext, operations);

        Assert.Contains(
            $"ALTER TABLE `{IndexPrefixContract.TableName}` DROP INDEX `{IndexPrefixContract.IndexName}`",
            sql,
            StringComparison.Ordinal);

        var expectedCreate = $"CREATE INDEX `{IndexPrefixContract.IndexName}` "
            + $"ON `{IndexPrefixContract.TableName}` "
            + (transition is IndexPrefixTransition.Change or IndexPrefixTransition.Add
                ? "(`TenantId`, `Code`(48))"
                : "(`TenantId`, `Code`)");

        Assert.Contains(
            expectedCreate,
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Equal provider-owned index metadata does not create a redundant rebuild.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_does_not_rebuild_unchanged_prefix_lengths()
    {
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        Assert.False(differ.HasDifferences(sourceModel, targetModel));
        Assert.Empty(differ.GetDifferences(sourceModel, targetModel));
    }

    /// <summary>
    /// Equal provider-owned index kinds do not create a redundant rebuild.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Migrations_model_differ_does_not_rebuild_unchanged_index_kinds(
        bool fullText
    )
    {
        using DbContext sourceContext = fullText
            ? new FullTextIndexContext(CreateOptions<FullTextIndexContext>())
            : new SpatialIndexContext(CreateOptions<SpatialIndexContext>());
        using DbContext targetContext = fullText
            ? new FullTextIndexContext(CreateOptions<FullTextIndexContext>())
            : new SpatialIndexContext(CreateOptions<SpatialIndexContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        Assert.False(differ.HasDifferences(sourceModel, targetModel));
        Assert.Empty(differ.GetDifferences(sourceModel, targetModel));
    }

    /// <summary>
    /// A simultaneous index rename and prefix change is one physical rebuild,
    /// not a rename that silently retains the old prefix.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_rebuilds_a_renamed_index_when_prefix_lengths_change()
    {
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedIndexPrefixContext(CreateOptions<RenamedIndexPrefixContext>());
        var operations = GetDifferences(sourceContext, targetContext);

        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.Empty(operations.OfType<RenameIndexOperation>());
        Assert.Equal(IndexPrefixContract.IndexName, drop.Name);
        Assert.Equal(IndexPrefixContract.RenamedIndexName, create.Name);
        Assert.Equal(IndexPrefixContract.TargetPrefixLengths, create.GetMySqlMigrationMetadata().IndexPrefixLengths);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(create));

        var sql = GenerateMigrationSql(targetContext, operations);

        Assert.Contains(
            $"ALTER TABLE `{IndexPrefixContract.TableName}` DROP INDEX `{IndexPrefixContract.IndexName}`",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            $"CREATE INDEX `{IndexPrefixContract.RenamedIndexName}` "
            + $"ON `{IndexPrefixContract.TableName}` (`TenantId`, `Code`(48))",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A simultaneous table rename and prefix change rebuilds the index around
    /// the rename so neither operation addresses a stale table identity.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_rebuilds_an_index_when_its_table_and_prefix_lengths_change()
    {
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedTableIndexPrefixContext(
            CreateOptions<RenamedTableIndexPrefixContext>());
        var operations = GetDifferences(sourceContext, targetContext);

        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var rename = Assert.Single(operations.OfType<RenameTableOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.Equal(IndexPrefixContract.TableName, drop.Table);
        Assert.Equal(IndexPrefixContract.RenamedTableName, rename.NewName);
        Assert.Equal(IndexPrefixContract.RenamedTableName, create.Table);
        Assert.Equal(IndexPrefixContract.TargetPrefixLengths, create.GetMySqlMigrationMetadata().IndexPrefixLengths);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(rename));
        Assert.True(operations.IndexOf(rename) < operations.IndexOf(create));

        var sql = GenerateMigrationSql(targetContext, operations);

        Assert.Contains(
            $"ALTER TABLE `{IndexPrefixContract.TableName}` DROP INDEX `{IndexPrefixContract.IndexName}`",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            $"RENAME TABLE `{IndexPrefixContract.TableName}` TO `{IndexPrefixContract.RenamedTableName}`",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            $"CREATE INDEX `{IndexPrefixContract.IndexName}` "
            + $"ON `{IndexPrefixContract.RenamedTableName}` (`TenantId`, `Code`(48))",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Simultaneous table and index renames do not hide a physical prefix
    /// transition behind either rename operation.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_rebuilds_an_index_when_both_identity_parts_and_prefix_change()
    {
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedTableAndIndexPrefixContext(
            CreateOptions<RenamedTableAndIndexPrefixContext>());
        var operations = GetDifferences(sourceContext, targetContext);

        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var renameTable = Assert.Single(operations.OfType<RenameTableOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.Empty(operations.OfType<RenameIndexOperation>());
        Assert.Equal(IndexPrefixContract.IndexName, drop.Name);
        Assert.Equal(IndexPrefixContract.TableName, drop.Table);
        Assert.Equal(IndexPrefixContract.RenamedTableName, renameTable.NewName);
        Assert.Equal(IndexPrefixContract.RenamedIndexName, create.Name);
        Assert.Equal(IndexPrefixContract.RenamedTableName, create.Table);
        Assert.Equal(IndexPrefixContract.TargetPrefixLengths, create.GetMySqlMigrationMetadata().IndexPrefixLengths);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(renameTable));
        Assert.True(operations.IndexOf(renameTable) < operations.IndexOf(create));
    }

    /// <summary>
    /// A pure index rename retains the efficient native rename operation when
    /// provider metadata is unchanged.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_does_not_rebuild_a_renamed_index_with_unchanged_prefix_lengths()
    {
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedIndexSamePrefixContext(CreateOptions<RenamedIndexSamePrefixContext>());
        var operations = GetDifferences(sourceContext, targetContext);
        var rename = Assert.Single(operations.OfType<RenameIndexOperation>());

        Assert.Equal(IndexPrefixContract.IndexName, rename.Name);
        Assert.Equal(IndexPrefixContract.RenamedIndexName, rename.NewName);
        Assert.Empty(operations.OfType<DropIndexOperation>());
        Assert.Empty(operations.OfType<CreateIndexOperation>());
    }

    /// <summary>
    /// A pure table rename does not rebuild indexes whose provider metadata is
    /// unchanged.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_does_not_rebuild_indexes_for_a_table_rename_with_unchanged_prefix_lengths()
    {
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedTableSamePrefixContext(CreateOptions<RenamedTableSamePrefixContext>());
        var operations = GetDifferences(sourceContext, targetContext);

        Assert.Single(operations.OfType<RenameTableOperation>());
        Assert.Empty(operations.OfType<DropIndexOperation>());
        Assert.Empty(operations.OfType<CreateIndexOperation>());
    }

    /// <summary>
    /// TPT inheritance resolves each declared index against its owning table
    /// without reinterpreting an inherited base index as a derived-table index.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_resolves_declared_indexes_across_tpt_tables()
    {
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new InheritedIndexContext(CreateOptions<InheritedIndexContext>());
        var operations = GetDifferences(sourceContext, targetContext);
        var indexes = operations
            .OfType<CreateIndexOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        Assert.Equal(
            InheritedIndexContract.BaseTable,
            indexes[InheritedIndexContract.BaseIndex].Table);
        Assert.Equal(
            InheritedIndexContract.DerivedTable,
            indexes[InheritedIndexContract.DerivedIndex].Table);
        Assert.DoesNotContain(
            operations.OfType<CreateIndexOperation>(),
            operation => operation.Name == InheritedIndexContract.BaseIndex
                && operation.Table == InheritedIndexContract.DerivedTable);
    }

    /// <summary>
    /// TPC inheritance expands one base-declared index into each concrete
    /// table and preserves the provider metadata on every physical index.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_expands_base_indexes_across_tpc_tables()
    {
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new InheritedTpcIndexContext(CreateOptions<InheritedTpcIndexContext>());
        var indexes = GetDifferences(sourceContext, targetContext)
            .OfType<CreateIndexOperation>()
            .Where(operation => operation.Name == InheritedTpcIndexContract.Index)
            .OrderBy(operation => operation.Table, StringComparer.Ordinal)
            .ToArray();

        Assert.Collection(
            indexes,
            operation =>
            {
                Assert.Equal(InheritedTpcIndexContract.FirstTable, operation.Table);
                Assert.Equal([16], operation.GetMySqlMigrationMetadata().IndexPrefixLengths);
            },
            operation =>
            {
                Assert.Equal(InheritedTpcIndexContract.SecondTable, operation.Table);
                Assert.Equal([16], operation.GetMySqlMigrationMetadata().IndexPrefixLengths);
            });
    }

    /// <summary>
    /// Physical TPC copies retain distinct logical identities while their
    /// tables, index name, and provider metadata change together.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_rebuilds_renamed_tpc_index_copies_independently()
    {
        using var sourceContext = new InheritedTpcIndexContext(CreateOptions<InheritedTpcIndexContext>());
        using var targetContext = new RenamedInheritedTpcIndexContext(
            CreateOptions<RenamedInheritedTpcIndexContext>());
        var operations = GetDifferences(sourceContext, targetContext);
        var drops = operations
            .OfType<DropIndexOperation>()
            .OrderBy(operation => operation.Table, StringComparer.Ordinal)
            .ToArray();
        var creates = operations
            .OfType<CreateIndexOperation>()
            .OrderBy(operation => operation.Table, StringComparer.Ordinal)
            .ToArray();
        var tableRenames = operations
            .OfType<RenameTableOperation>()
            .OrderBy(operation => operation.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [InheritedTpcIndexContract.FirstTable, InheritedTpcIndexContract.SecondTable],
            drops.Select(operation => operation.Table));
        Assert.All(drops, operation => Assert.Equal(InheritedTpcIndexContract.Index, operation.Name));
        Assert.Equal(
            [InheritedTpcIndexContract.RenamedFirstTable, InheritedTpcIndexContract.RenamedSecondTable],
            creates.Select(operation => operation.Table));
        Assert.All(creates, operation =>
        {
            Assert.Equal(InheritedTpcIndexContract.RenamedIndex, operation.Name);
            Assert.Equal([24], operation.GetMySqlMigrationMetadata().IndexPrefixLengths);
        });
        Assert.Equal(2, tableRenames.Length);
        Assert.Empty(operations.OfType<RenameIndexOperation>());
        Assert.True(operations.IndexOf(drops[0]) < operations.IndexOf(tableRenames[0]));
        Assert.True(operations.IndexOf(tableRenames[^1]) < operations.IndexOf(creates[0]));
    }

    /// <summary>
    /// Other provider-owned index kinds use the same physical rebuild boundary.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Migrations_model_differ_rebuilds_indexes_for_full_text_transitions(
        bool enableFullText
    )
    {
        using var plainContext = new PlainTextIndexContext(CreateOptions<PlainTextIndexContext>());
        using var fullTextContext = new FullTextIndexContext(CreateOptions<FullTextIndexContext>());
        DbContext sourceContext = enableFullText ? plainContext : fullTextContext;
        DbContext targetContext = enableFullText ? fullTextContext : plainContext;
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var operations = differ
            .GetDifferences(sourceModel, targetModel)
            .ToList();
        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.True(differ.HasDifferences(sourceModel, targetModel));
        Assert.Equal(TextIndexContract.IndexName, drop.Name);
        Assert.Equal(
            enableFullText ? true : null,
            create.FindAnnotation(MySqlAnnotationNames.FullTextIndex)?.Value as bool?);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(create));

        var sql = GenerateMigrationSql(targetContext, operations);

        Assert.Contains(
            enableFullText
                ? $"CREATE FULLTEXT INDEX `{TextIndexContract.IndexName}`"
                : $"CREATE INDEX `{TextIndexContract.IndexName}`",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Spatial index-kind transitions rebuild the same physical index identity.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Migrations_model_differ_rebuilds_indexes_for_spatial_transitions(
        bool enableSpatial
    )
    {
        using var plainContext = new PlainSpatialIndexContext(CreateOptions<PlainSpatialIndexContext>());
        using var spatialContext = new SpatialIndexContext(CreateOptions<SpatialIndexContext>());
        DbContext sourceContext = enableSpatial ? plainContext : spatialContext;
        DbContext targetContext = enableSpatial ? spatialContext : plainContext;
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var operations = differ
            .GetDifferences(sourceModel, targetModel)
            .ToList();
        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.True(differ.HasDifferences(sourceModel, targetModel));
        Assert.Equal(SpatialIndexContract.IndexName, drop.Name);
        Assert.Equal(
            enableSpatial ? true : null,
            create.FindAnnotation(MySqlAnnotationNames.SpatialIndex)?.Value as bool?);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(create));

        var sql = GenerateMigrationSql(targetContext, operations);

        Assert.Contains(
            enableSpatial
                ? $"CREATE SPATIAL INDEX `{SpatialIndexContract.IndexName}`"
                : $"CREATE INDEX `{SpatialIndexContract.IndexName}`",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Entity splitting keeps AUTO_INCREMENT on the principal table while the
    /// secondary shared key remains only a primary and cascading foreign key.
    /// </summary>
    [Fact]
    public void Entity_splitting_emits_auto_increment_only_for_the_principal_table()
    {
        using var source = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var target = new GeneratedEntitySplitContext(CreateOptions<GeneratedEntitySplitContext>());
        var operations = GetDifferences(source, target);
        var tables = operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        var principal = tables["SplitInventory"];
        var secondary = tables["SplitInventoryDetails"];

        var principalId = Assert.Single(principal.Columns, column => column.Name == "Id");
        var secondaryId = Assert.Single(secondary.Columns, column => column.Name == "Id");
        var secondaryForeignKey = Assert.Single(secondary.ForeignKeys);

        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.Null(secondaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy));
        Assert.Equal(["Id"], principal.PrimaryKey!.Columns);
        Assert.Equal(["Id"], secondary.PrimaryKey!.Columns);
        Assert.Equal(["Id"], secondaryForeignKey.Columns);
        Assert.Equal("SplitInventory", secondaryForeignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.Cascade, secondaryForeignKey.OnDelete);

        var principalSql = GenerateMigrationSql(target, [principal]);
        var secondarySql = GenerateMigrationSql(target, [secondary]);

        Assert.Contains("`Id` int NOT NULL AUTO_INCREMENT", principalSql, StringComparison.Ordinal);
        Assert.DoesNotContain("AUTO_INCREMENT", secondarySql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Suppressing the secondary split-table generator must not remove generation
    /// from ordinary keys or invent generation for explicitly non-generated keys.
    /// </summary>
    [Fact]
    public void Entity_splitting_preserves_non_split_and_non_generated_key_contracts()
    {
        using var source = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var target = new NonGeneratedEntitySplitContext(CreateOptions<NonGeneratedEntitySplitContext>());
        var operations = GetDifferences(source, target);
        var tables = operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        var ordinaryId = Assert.Single(tables["OrdinaryGeneratedEntities"].Columns, column => column.Name == "Id");
        var principalId = Assert.Single(tables["ManualSplitInventory"].Columns, column => column.Name == "Id");
        var secondaryId = Assert.Single(tables["ManualSplitInventoryDetails"].Columns, column => column.Name == "Id");

        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            ordinaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.NotEqual(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.NotEqual(
            MySqlValueGenerationStrategy.AutoIncrement,
            secondaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.DoesNotContain(
            "AUTO_INCREMENT",
            GenerateMigrationSql(target, [tables["ManualSplitInventory"]]),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AUTO_INCREMENT",
            GenerateMigrationSql(target, [tables["ManualSplitInventoryDetails"]]),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Changing a split key from generated to caller-supplied keeps the old
    /// generator annotation on the principal alteration only. The secondary
    /// shared key must not regain it through removal metadata.
    /// </summary>
    [Fact]
    public void Entity_splitting_alter_history_keeps_generation_on_the_principal_only()
    {
        using var source = new GeneratedEntitySplitContext(CreateOptions<GeneratedEntitySplitContext>());
        using var target = new NonGeneratedSameTableEntitySplitContext(
            CreateOptions<NonGeneratedSameTableEntitySplitContext>());

        var operations = GetDifferences(source, target);
        var idAlterations = operations
            .OfType<AlterColumnOperation>()
            .Where(operation => operation.Name == "Id")
            .ToDictionary(operation => operation.Table, StringComparer.Ordinal);

        var principalId = idAlterations["SplitInventory"];
        var secondaryId = idAlterations["SplitInventoryDetails"];

        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            principalId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            principalId.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.OldColumn.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.OldColumn.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            secondaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
                ?.Value);
        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            secondaryId.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Null(secondaryId.OldColumn.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy));
        Assert.Null(secondaryId.OldColumn.GetMySqlMigrationMetadata().ValueGenerationStrategy);
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
    /// Verifies that a typed application-time model becomes MariaDB period DDL
    /// and binds <c>WITHOUT OVERLAPS</c> to the configured period.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_creates_application_time_period_on_mariadb()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var sourceContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));

        using var targetContext = new ApplicationTimeMigrationDslContext(
            CreateOptions<ApplicationTimeMigrationDslContext>(serverVersion));

        var sql = GenerateMigrationSql(sourceContext, targetContext);

        Assert.Contains(
            "PERIOD FOR `BusinessValidity` (`ValidFrom`, `ValidTo`)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "PRIMARY KEY (`Id`, `BusinessValidity` WITHOUT OVERLAPS)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            ",\n    CONSTRAINT `PK_MigrationDsl` PRIMARY KEY",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("WITH SYSTEM VERSIONING", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that bitemporal configuration composes both independent engine
    /// contracts rather than replacing one temporal dimension with the other.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_creates_bitemporal_table_on_mariadb()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        using var sourceContext = new EmptyMigrationDslContext(
            CreateOptions<EmptyMigrationDslContext>(serverVersion));

        using var targetContext = new BitemporalMigrationDslContext(
            CreateOptions<BitemporalMigrationDslContext>(serverVersion));

        var sql = GenerateMigrationSql(sourceContext, targetContext);

        Assert.Contains("PERIOD FOR SYSTEM_TIME (`SystemValidFrom`, `SystemValidTo`)", sql, StringComparison.Ordinal);
        Assert.Contains(
            "PERIOD FOR `BusinessValidity` (`BusinessValidFrom`, `BusinessValidTo`)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("WITH SYSTEM VERSIONING", sql, StringComparison.Ordinal);
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

    /// <summary>
    /// Verifies that an explicit SQL backfill makes different CLR projections
    /// equivalent when they alter the same physical shared column.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_deduplicates_equivalent_sql_backfills()
    {
        using var target = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var firstAlter = CreateRequiredJsonAlterColumn(typeof(string), "JSON_OBJECT()");
        var secondAlter = CreateRequiredJsonAlterColumn(typeof(JsonDocument), "JSON_OBJECT()");
        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(firstAlter, secondAlter));
        var operations = differ.GetDifferences(
            null,
            target
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel());

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
        using var target = new MigrationDslContext(CreateOptions<MigrationDslContext>());
        var firstAlter = CreateRequiredJsonAlterColumn(typeof(string), "JSON_OBJECT()");
        var secondAlter = CreateRequiredJsonAlterColumn(typeof(JsonDocument), "JSON_ARRAY()");
        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(firstAlter, secondAlter));
        var operations = differ.GetDifferences(
            null,
            target
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel());

        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        Assert.Equal(2, alterColumns.Length);
        Assert.Contains(alterColumns, operation => operation.DefaultValueSql == "JSON_OBJECT()");
        Assert.Contains(alterColumns, operation => operation.DefaultValueSql == "JSON_ARRAY()");
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
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();

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

    private static DbContext CreateIndexPrefixContext(
        IndexPrefixTransition transition,
        bool source
    ) => (transition, source) switch
    {
        (IndexPrefixTransition.Change, true) =>
            new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>()),
        (IndexPrefixTransition.Change, false) or (IndexPrefixTransition.Remove, true) =>
            new TargetIndexPrefixContext(CreateOptions<TargetIndexPrefixContext>()),
        (IndexPrefixTransition.Add, true) or (IndexPrefixTransition.Remove, false) =>
            new NoIndexPrefixContext(CreateOptions<NoIndexPrefixContext>()),
        (IndexPrefixTransition.Add, false) =>
            new TargetIndexPrefixContext(CreateOptions<TargetIndexPrefixContext>()),
        _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, null),
    };

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

    private sealed class SourceIndexPrefixContext : DbContext
    {
        public SourceIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(modelBuilder, IndexPrefixContract.SourcePrefixLengths);
    }

    private sealed class TargetIndexPrefixContext : DbContext
    {
        public TargetIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(modelBuilder, IndexPrefixContract.TargetPrefixLengths);
    }

    private sealed class NoIndexPrefixContext : DbContext
    {
        public NoIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(modelBuilder, prefixLengths: null);
    }

    private sealed class RenamedIndexPrefixContext : DbContext
    {
        public RenamedIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.TargetPrefixLengths,
            indexName: IndexPrefixContract.RenamedIndexName);
    }

    private sealed class RenamedTableIndexPrefixContext : DbContext
    {
        public RenamedTableIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.TargetPrefixLengths,
            tableName: IndexPrefixContract.RenamedTableName);
    }

    private sealed class RenamedTableAndIndexPrefixContext : DbContext
    {
        public RenamedTableAndIndexPrefixContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.TargetPrefixLengths,
            IndexPrefixContract.RenamedTableName,
            IndexPrefixContract.RenamedIndexName);
    }

    private sealed class RenamedIndexSamePrefixContext : DbContext
    {
        public RenamedIndexSamePrefixContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.SourcePrefixLengths,
            indexName: IndexPrefixContract.RenamedIndexName);
    }

    private sealed class RenamedTableSamePrefixContext : DbContext
    {
        public RenamedTableSamePrefixContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureIndexPrefix(
            modelBuilder,
            IndexPrefixContract.SourcePrefixLengths,
            tableName: IndexPrefixContract.RenamedTableName);
    }

    private sealed class PlainTextIndexContext : DbContext
    {
        public PlainTextIndexContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTextIndex(modelBuilder, fullText: false);
    }

    private sealed class FullTextIndexContext : DbContext
    {
        public FullTextIndexContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTextIndex(modelBuilder, fullText: true);
    }

    private sealed class PlainSpatialIndexContext : DbContext
    {
        public PlainSpatialIndexContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureSpatialIndex(modelBuilder, spatial: false);
    }

    private sealed class SpatialIndexContext : DbContext
    {
        public SpatialIndexContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureSpatialIndex(modelBuilder, spatial: true);
    }

    private sealed class InheritedIndexContext : DbContext
    {
        public InheritedIndexContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<InheritedIndexBase>()
                .ToTable(InheritedIndexContract.BaseTable)
                .HasIndex(entity => new
                {
                    entity.AlternateId,
                    entity.Id,
                })
                .HasDatabaseName(InheritedIndexContract.BaseIndex)
                .HasPrefixLength(16, 0);

            modelBuilder
                .Entity<InheritedIndexDerived>()
                .ToTable(InheritedIndexContract.DerivedTable)
                .HasIndex(entity => entity.DerivedCode)
                .HasDatabaseName(InheritedIndexContract.DerivedIndex)
                .HasPrefixLength(24);
        }
    }

    private sealed class InheritedTpcIndexContext : DbContext
    {
        public InheritedTpcIndexContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureInheritedTpcIndex(
            modelBuilder,
            InheritedTpcIndexContract.FirstTable,
            InheritedTpcIndexContract.SecondTable,
            InheritedTpcIndexContract.Index,
            prefixLength: 16);
    }

    private sealed class RenamedInheritedTpcIndexContext : DbContext
    {
        public RenamedInheritedTpcIndexContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureInheritedTpcIndex(
            modelBuilder,
            InheritedTpcIndexContract.RenamedFirstTable,
            InheritedTpcIndexContract.RenamedSecondTable,
            InheritedTpcIndexContract.RenamedIndex,
            prefixLength: 24);
    }

    private static void ConfigureIndexPrefix(
        ModelBuilder modelBuilder,
        int[]? prefixLengths,
        string tableName = IndexPrefixContract.TableName,
        string indexName = IndexPrefixContract.IndexName
    )
    {
        var index = modelBuilder
            .Entity<IndexPrefixEntity>()
            .ToTable(tableName)
            .HasIndex(entity => new
            {
                entity.TenantId,
                entity.Code,
            })
            .HasDatabaseName(indexName);

        if (prefixLengths is not null)
        {
            index.HasPrefixLength(prefixLengths);
        }
    }

    private static void ConfigureInheritedTpcIndex(
        ModelBuilder modelBuilder,
        string firstTable,
        string secondTable,
        string indexName,
        int prefixLength
    )
    {
        modelBuilder
            .Entity<InheritedTpcIndexBase>()
            .UseTpcMappingStrategy()
            .HasIndex(entity => entity.AlternateId)
            .HasDatabaseName(indexName)
            .HasPrefixLength(prefixLength);

        modelBuilder
            .Entity<FirstInheritedTpcIndex>()
            .ToTable(firstTable);
        modelBuilder
            .Entity<SecondInheritedTpcIndex>()
            .ToTable(secondTable);
    }

    private static void ConfigureTextIndex(
        ModelBuilder modelBuilder,
        bool fullText
    )
    {
        var index = modelBuilder
            .Entity<TextIndexEntity>()
            .ToTable(TextIndexContract.TableName)
            .HasIndex(entity => entity.Body)
            .HasDatabaseName(TextIndexContract.IndexName);

        modelBuilder
            .Entity<TextIndexEntity>()
            .Property(entity => entity.Body)
            .HasMaxLength(256);

        if (fullText)
        {
            index.IsFullText();
        }
    }

    private static void ConfigureSpatialIndex(
        ModelBuilder modelBuilder,
        bool spatial
    )
    {
        var index = modelBuilder
            .Entity<SpatialIndexEntity>()
            .ToTable(SpatialIndexContract.TableName)
            .HasIndex(entity => entity.Location)
            .HasDatabaseName(SpatialIndexContract.IndexName);

        modelBuilder
            .Entity<SpatialIndexEntity>()
            .Property(entity => entity.Location)
            .HasColumnType("point")
            .HasSrid(0);

        if (spatial)
        {
            index.IsSpatial();
        }
    }

    private sealed class GeneratedEntitySplitContext : DbContext
    {
        public GeneratedEntitySplitContext(
            DbContextOptions<GeneratedEntitySplitContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SplitInventory>(entity =>
            {
                entity.ToTable("SplitInventory");
                entity.HasKey(inventory => inventory.Id);
                entity.Property(inventory => inventory.Id).UseMySqlAutoIncrementColumn();
                entity.SplitToTable(
                    "SplitInventoryDetails",
                    split => split.Property(inventory => inventory.Description));
            });
        }
    }

    private sealed class NonGeneratedEntitySplitContext : DbContext
    {
        public NonGeneratedEntitySplitContext(
            DbContextOptions<NonGeneratedEntitySplitContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SplitInventory>(entity =>
            {
                entity.ToTable("ManualSplitInventory");
                entity.HasKey(inventory => inventory.Id);
                entity.Property(inventory => inventory.Id).ValueGeneratedNever();
                entity.SplitToTable(
                    "ManualSplitInventoryDetails",
                    split => split.Property(inventory => inventory.Description));
            });

            modelBuilder.Entity<OrdinaryGeneratedEntity>(entity =>
            {
                entity.ToTable("OrdinaryGeneratedEntities");
                entity.HasKey(item => item.Id);
            });
        }
    }

    private sealed class NonGeneratedSameTableEntitySplitContext : DbContext
    {
        public NonGeneratedSameTableEntitySplitContext(
            DbContextOptions<NonGeneratedSameTableEntitySplitContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SplitInventory>(entity =>
            {
                entity.ToTable("SplitInventory");
                entity.HasKey(inventory => inventory.Id);
                entity.Property(inventory => inventory.Id).ValueGeneratedNever();
                entity.SplitToTable(
                    "SplitInventoryDetails",
                    split => split.Property(inventory => inventory.Description));
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

    private sealed class ApplicationTimeMigrationDslContext : DbContext
    {
        public ApplicationTimeMigrationDslContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<MigrationDslEntity>(entity =>
        {
            entity.ToTable(
                "MigrationDsl",
                table => table.HasApplicationTimePeriod(applicationTime =>
                {
                    applicationTime.HasPeriodName("BusinessValidity");
                    applicationTime.HasPeriodStart("ValidFrom");
                    applicationTime.HasPeriodEnd("ValidTo");
                    applicationTime.UseWithoutOverlaps();
                }));
        });
    }

    private sealed class BitemporalMigrationDslContext : DbContext
    {
        public BitemporalMigrationDslContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<MigrationDslEntity>(entity =>
        {
            entity.ToTable(
                "MigrationDsl",
                table => table.IsBitemporal(
                    systemTime =>
                    {
                        systemTime.HasPeriodStart("SystemValidFrom");
                        systemTime.HasPeriodEnd("SystemValidTo");
                    },
                    applicationTime =>
                    {
                        applicationTime.HasPeriodName("BusinessValidity");
                        applicationTime.HasPeriodStart("BusinessValidFrom");
                        applicationTime.HasPeriodEnd("BusinessValidTo");
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

    private static AlterColumnOperation CreateRequiredJsonAlterColumn(
        Type clrType,
        string defaultValueSql
    )
    {
        var operation = CreateJsonAlterColumn(clrType, isUnicode: null, maxLength: null);
        operation.IsNullable = false;
        operation.DefaultValueSql = defaultValueSql;
        operation.OldColumn.IsNullable = true;

        return operation;
    }

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

    private sealed class SplitInventory
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
    }

    private sealed class OrdinaryGeneratedEntity
    {
        public int Id { get; set; }
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

    private sealed class IndexPrefixEntity
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    private sealed class TextIndexEntity
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;
    }

    private sealed class SpatialIndexEntity
    {
        public int Id { get; set; }

        public Point Location { get; set; } = new(0, 0);
    }

    private class InheritedIndexBase
    {
        public int Id { get; set; }

        public string AlternateId { get; set; } = string.Empty;
    }

    private sealed class InheritedIndexDerived : InheritedIndexBase
    {
        public string DerivedCode { get; set; } = string.Empty;
    }

    private abstract class InheritedTpcIndexBase
    {
        public int Id { get; set; }

        public string AlternateId { get; set; } = string.Empty;
    }

    private sealed class FirstInheritedTpcIndex : InheritedTpcIndexBase;

    private sealed class SecondInheritedTpcIndex : InheritedTpcIndexBase;

    private static class IndexPrefixContract
    {
        public const string TableName = "IndexPrefixRecords";
        public const string RenamedTableName = "RenamedIndexPrefixRecords";
        public const string IndexName = "IX_IndexPrefixRecords_TenantId_Code";
        public const string RenamedIndexName = "IX_IndexPrefixRecords_TenantId_Code_Renamed";
        public static readonly int[] SourcePrefixLengths = [0, 24];
        public static readonly int[] TargetPrefixLengths = [0, 48];
    }

    private static class TextIndexContract
    {
        public const string TableName = "TextIndexRecords";
        public const string IndexName = "IX_TextIndexRecords_Body";
    }

    private static class SpatialIndexContract
    {
        public const string TableName = "SpatialIndexRecords";
        public const string IndexName = "IX_SpatialIndexRecords_Location";
    }

    private static class InheritedIndexContract
    {
        public const string BaseTable = "InheritedIndexBase";
        public const string DerivedTable = "InheritedIndexDerived";
        public const string BaseIndex = "IX_InheritedIndexBase_AlternateId_Id";
        public const string DerivedIndex = "IX_InheritedIndexDerived_DerivedCode";
    }

    private static class InheritedTpcIndexContract
    {
        public const string FirstTable = "FirstInheritedTpcIndex";
        public const string SecondTable = "SecondInheritedTpcIndex";
        public const string RenamedFirstTable = "RenamedFirstInheritedTpcIndex";
        public const string RenamedSecondTable = "RenamedSecondInheritedTpcIndex";
        public const string Index = "IX_InheritedTpcIndex_AlternateId";
        public const string RenamedIndex = "IX_InheritedTpcIndex_AlternateId_Renamed";
    }

    public enum IndexPrefixTransition
    {
        Change,
        Add,
        Remove,
    }
}
