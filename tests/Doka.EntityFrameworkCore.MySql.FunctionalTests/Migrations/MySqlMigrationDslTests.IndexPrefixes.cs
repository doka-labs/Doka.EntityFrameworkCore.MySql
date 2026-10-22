namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
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
        // Arrange
        using var sourceContext = CreateIndexPrefixContext(transition, source: true);
        using var targetContext = CreateIndexPrefixContext(transition, source: false);
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        // Act
        var hasDifferences = differ.HasDifferences(sourceModel, targetModel);
        var operations = differ
            .GetDifferences(sourceModel, targetModel)
            .ToList();

        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        Assert.True(hasDifferences);
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
        // Arrange
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        // Act
        var hasDifferences = differ.HasDifferences(sourceModel, targetModel);
        var operations = differ.GetDifferences(sourceModel, targetModel);

        // Assert
        Assert.False(hasDifferences);
        Assert.Empty(operations);
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
        // Arrange
        using DbContext sourceContext = fullText
            ? new FullTextIndexContext(CreateOptions<FullTextIndexContext>())
            : new SpatialIndexContext(CreateOptions<SpatialIndexContext>());

        using DbContext targetContext = fullText
            ? new FullTextIndexContext(CreateOptions<FullTextIndexContext>())
            : new SpatialIndexContext(CreateOptions<SpatialIndexContext>());

        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        // Act
        var hasDifferences = differ.HasDifferences(sourceModel, targetModel);
        var operations = differ.GetDifferences(sourceModel, targetModel);

        // Assert
        Assert.False(hasDifferences);
        Assert.Empty(operations);
    }

    /// <summary>
    /// A simultaneous index rename and prefix change is one physical rebuild,
    /// not a rename that silently retains the old prefix.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_rebuilds_a_renamed_index_when_prefix_lengths_change()
    {
        // Arrange
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedIndexPrefixContext(CreateOptions<RenamedIndexPrefixContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.Empty(operations.OfType<RenameIndexOperation>());
        Assert.Equal(IndexPrefixContract.IndexName, drop.Name);
        Assert.Equal(IndexPrefixContract.RenamedIndexName, create.Name);
        Assert.Equal(IndexPrefixContract.TargetPrefixLengths, create.GetMySqlMigrationMetadata().IndexPrefixLengths);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(create));

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
        // Arrange
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedTableIndexPrefixContext(
            CreateOptions<RenamedTableIndexPrefixContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var rename = Assert.Single(operations.OfType<RenameTableOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.Equal(IndexPrefixContract.TableName, drop.Table);
        Assert.Equal(IndexPrefixContract.RenamedTableName, rename.NewName);
        Assert.Equal(IndexPrefixContract.RenamedTableName, create.Table);
        Assert.Equal(IndexPrefixContract.TargetPrefixLengths, create.GetMySqlMigrationMetadata().IndexPrefixLengths);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(rename));
        Assert.True(operations.IndexOf(rename) < operations.IndexOf(create));

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
        // Arrange
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedTableAndIndexPrefixContext(
            CreateOptions<RenamedTableAndIndexPrefixContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        // Assert
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
        // Arrange
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedIndexSamePrefixContext(CreateOptions<RenamedIndexSamePrefixContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        // Assert
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
        // Arrange
        using var sourceContext = new SourceIndexPrefixContext(CreateOptions<SourceIndexPrefixContext>());
        using var targetContext = new RenamedTableSamePrefixContext(CreateOptions<RenamedTableSamePrefixContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);

        // Assert
        Assert.Single(operations.OfType<RenameTableOperation>());
        Assert.Empty(operations.OfType<DropIndexOperation>());
        Assert.Empty(operations.OfType<CreateIndexOperation>());
    }
}
