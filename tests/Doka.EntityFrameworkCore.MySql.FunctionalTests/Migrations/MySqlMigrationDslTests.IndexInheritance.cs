namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// TPT inheritance resolves each declared index against its owning table
    /// without reinterpreting an inherited base index as a derived-table index.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_resolves_declared_indexes_across_tpt_tables()
    {
        // Arrange
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new InheritedIndexContext(CreateOptions<InheritedIndexContext>());

        // Act
        var operations = GetDifferences(sourceContext, targetContext);
        var indexes = operations
            .OfType<CreateIndexOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        // Assert
        Assert.Equal(
            InheritedIndexContract.BaseTable,
            indexes[InheritedIndexContract.BaseIndex].Table);
        Assert.Equal(
            InheritedIndexContract.DerivedTable,
            indexes[InheritedIndexContract.DerivedIndex].Table);
        Assert.DoesNotContain(
            operations.OfType<CreateIndexOperation>(),
            operation => operation is { Name: InheritedIndexContract.BaseIndex, Table: InheritedIndexContract.DerivedTable });
    }

    /// <summary>
    /// TPC inheritance expands one base-declared index into each concrete
    /// table and preserves the provider metadata on every physical index.
    /// </summary>
    [Fact]
    public void Migrations_model_differ_expands_base_indexes_across_tpc_tables()
    {
        // Arrange
        using var sourceContext = new EmptyMigrationDslContext(CreateOptions<EmptyMigrationDslContext>());
        using var targetContext = new InheritedTpcIndexContext(CreateOptions<InheritedTpcIndexContext>());

        // Act
        var indexes = GetDifferences(sourceContext, targetContext)
            .OfType<CreateIndexOperation>()
            .Where(operation => operation.Name == InheritedTpcIndexContract.Index)
            .OrderBy(operation => operation.Table, StringComparer.Ordinal)
            .ToArray();

        // Assert
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
        // Arrange
        using var sourceContext = new InheritedTpcIndexContext(CreateOptions<InheritedTpcIndexContext>());
        using var targetContext = new RenamedInheritedTpcIndexContext(
            CreateOptions<RenamedInheritedTpcIndexContext>());

        // Act
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

        // Assert
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
}
