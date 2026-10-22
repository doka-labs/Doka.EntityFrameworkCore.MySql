namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the indexes responsibility for MySqlMigrationDdlCoverageTests.</summary>
public sealed partial class MySqlMigrationDdlCoverageTests
{
    // -- INDEX METADATA --

    /// <summary>
    /// Verifies that prefix lengths and descending key parts retain their column order.
    /// </summary>
    [Fact]
    public void CreateIndex_with_prefix_and_direction_generates_exact_key_parts()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Name_Code",
            Table = "Entries",
            Columns = ["Name", "Code"],
            IsDescending = [false, true],
        };

        operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_mixedPrefixLengths);

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains(
            "CREATE INDEX `IX_Entries_Name_Code` ON `Entries` (`Name`(32), `Code` DESC)",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies EF Core's empty direction array convention: every index key part
    /// is descending when <c>IndexBuilder.IsDescending()</c> has no arguments.
    /// </summary>
    [Fact]
    public void CreateIndex_with_empty_directions_marks_every_key_part_descending()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_First_Second",
            Table = "Entries",
            Columns = ["First", "Second"],
            IsDescending = [],
        };

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains(
            "CREATE INDEX `IX_Entries_First_Second` ON `Entries` (`First` DESC, `Second` DESC)",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the full-text annotation selects the native FULLTEXT index grammar.
    /// </summary>
    [Fact]
    public void CreateIndex_with_full_text_annotation_generates_full_text_index()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Body",
            Table = "Entries",
            Columns = ["Body"],
        };

        operation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);

        // Act
        var sql = JoinSql(generator.Generate([operation], context.Model));

        // Assert
        Assert.Contains(
            "CREATE FULLTEXT INDEX `IX_Entries_Body` ON `Entries` (`Body`)",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that invalid provider index metadata fails before incomplete SQL can be emitted.
    /// </summary>
    [Theory]
    [InlineData("prefix-count")]
    [InlineData("negative-prefix")]
    [InlineData("unique-full-text")]
    [InlineData("prefixed-full-text")]
    [InlineData("spatial-full-text")]
    [InlineData("prefixed-spatial")]
    public void CreateIndex_with_invalid_provider_metadata_is_rejected(
        string scenario
    )
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Invalid",
            Table = "Entries",
            Columns = ["Name", "Code"],
        };

        ConfigureInvalidIndex(operation, scenario);

        // Act
        var failure = Record.Exception(() => generator.Generate([operation], context.Model));

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
    }

    // -- SPATIAL INDEX --

    [Fact]
    public void CreateIndex_with_spatial_annotation_generates_spatial_index()
    {
        // Arrange
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Location",
            Table = "Places",
            Columns = ["Location"],
        };

        operation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        // Assert
        Assert.Contains("SPATIAL INDEX", sql, StringComparison.OrdinalIgnoreCase);
    }
}
