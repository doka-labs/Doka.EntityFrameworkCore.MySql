namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMaterializedSourceTranslationTests
{
    /// <summary>
    /// TPT source columns are resolved before the flat source is bound.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tpt_projection_preserves_both_physical_tables(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<TptLeaf>()
            .Select(row => new { row.Id, row.Label, row.Score })
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Contains("BindingTptRoots", sql, StringComparison.Ordinal);
        Assert.Contains("BindingTptLeaves", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingTptRoots`"));
    }

    /// <summary>
    /// A TPC union belongs to one completed source and is not rebuilt for either reference.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tpc_projection_preserves_concrete_branch_selection(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<TpcRoot>().Select(row => new { row.Id, row.Label })
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingTpcFirst`"));
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingTpcSecond`"));
    }

    /// <summary>
    /// An entity-split source keeps the required join to its payload table.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entity_split_projection_preserves_payload_mapping(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<SplitRecord>().Select(row => new { row.Id, row.Name, row.Extra })
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Contains("BindingSplitRecords", sql, StringComparison.Ordinal);
        Assert.Contains("BindingSplitPayload", sql, StringComparison.Ordinal);
        Assert.Contains("`Extra`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A table-split dependent's payload remains a source column instead of a key-only rejoin.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Table_split_projection_preserves_dependent_columns(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<SharedRecord>()
            .Select(row => new { row.Id, row.Label, row.Details.Value })
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Contains("BindingSharedRecords", sql, StringComparison.Ordinal);
        Assert.Contains("`Label`", sql, StringComparison.Ordinal);
        Assert.Contains("`Value`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Owned reference values can be flattened into the source without materializing the owner.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Owned_reference_projection_preserves_native_values(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<OwnedRecord>()
            .Select(row => new { row.Id, row.Profile.Token, row.Profile.Note })
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Contains("Profile_Token", sql, StringComparison.Ordinal);
        Assert.Contains("Profile_Note", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Shadow and CLR parts of a composite key retain their mapped SQL equality.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shadow_composite_key_survives_multiple_references(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Set<ShadowRecord>()
            .Select(row => new { row.Id, Scope = EF.Property<Guid>(row, "Scope") });

        // Act
        var query = source.WithMaterializedSource(shared => shared.Join(
            shared,
            first => new { first.Id, first.Scope },
            second => new { second.Id, second.Scope },
            (first, _) => first));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingShadowRecords`"));
        Assert.Contains("`Scope`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converted values need no CLR equality operator to participate in a bound SQL join.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Operator_free_converted_value_keeps_its_sql_mapping(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Set<ConvertedRecord>().Select(row => new { row.Id, row.Reference });

        // Act
        var query = source.WithMaterializedSource(shared => shared.Join(
            shared,
            first => first.Reference,
            second => second.Reference,
            (first, second) => new { First = first.Id, Second = second.Id }));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingConvertedRecords`"));
        Assert.Contains("`Reference`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CHAR(36)", sql, StringComparison.OrdinalIgnoreCase);
    }
}
