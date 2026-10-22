namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies statement-local source composition without opening a database connection.
/// </summary>
public sealed partial class MySqlMaterializedSourceTranslationTests
{
    /// <summary>
    /// A mapped entity remains a server-shaped source rather than a client buffer.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entity_source_preserves_mapped_columns(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.WithMaterializedSource(shared => shared.Where(node => node.Left > 2));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.Contains("`Name`", sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Scalar projections retain bag multiplicity even when all projected values repeat.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_scalar_source_does_not_add_distinct(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Nodes.Select(node => node.Name);

        // Act
        var query = source.WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.DoesNotContain("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
        // WHY: One UNION ALL forms the empty recursive arm; the other preserves the consumer's bag.
        Assert.Equal(2, CountOccurrences(sql, "UNION ALL"));
    }

    /// <summary>
    /// Ordering and paging belong to the complete source, before either reference filters it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordered_page_and_reference_filters_share_one_source(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var minimum = 3;
        var skip = 5;
        var take = 7;
        var source = context.Nodes
            .Where(node => node.Left > minimum)
            .OrderBy(node => node.Name)
            .ThenBy(node => node.Id)
            .Skip(skip)
            .Take(take)
            .Select(node => new Coordinates { Id = node.Id, Left = node.Left, Right = node.Right });

        // Act
        var query = source.WithMaterializedSource(shared => shared
            .Where(node => node.Left < 10)
            .Concat(shared.Where(node => node.Right > 20)));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.Equal(1, CountOccurrences(sql, "LIMIT"));
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("@minimum", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COUNT(", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A volatile projected value is evaluated in the source, not in each consumer reference.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Volatile_projection_is_not_duplicated_by_a_self_join(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Nodes.Select(node => new { node.Id, Token = Guid.NewGuid() });

        // Act
        var query = source.WithMaterializedSource(shared => shared.Join(
            shared,
            first => first.Id,
            second => second.Id,
            (first, second) => new { first.Id, First = first.Token, Second = second.Token }));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "UUID()"));
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
    }

    /// <summary>
    /// Envelope aggregates and the exact result use the same completed source definition.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggregate_references_do_not_repeat_source_selection(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Nodes.Select(node => new Coordinates
        {
            Id = node.Id,
            Left = node.Left,
            Right = node.Right,
        });

        // Act
        var query = source.WithMaterializedSource(shared => shared.Select(node => new
        {
            node.Id,
            Maximum = shared.Max(other => other.Left),
            Minimum = shared.Min(other => other.Right),
        }));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
        MySqlSqlAssert.ContainsFunction(sql, "MAX");
        MySqlSqlAssert.ContainsFunction(sql, "MIN");
    }

    /// <summary>
    /// An empty source remains an empty relational input without a preflight command.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_source_stays_inside_the_statement(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Where(_ => false)
            .Select(node => node.Id)
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.DoesNotContain("COUNT(", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Binary GUID and nullable payload projections retain SQL-native comparisons.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_guid_nullability_and_collation_survive_binding(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes
            .Select(node => new { node.Token, node.OptionalName })
            .WithMaterializedSource(shared => shared.Join(
                shared,
                first => first.Token,
                second => second.Token,
                (first, second) => new { First = first.OptionalName, Second = second.OptionalName }))
            .Where(row => EF.Functions.Collate(row.First!, "utf8mb4_bin") == row.Second);

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Contains("COLLATE utf8mb4_bin", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CHAR(36)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reusing an expression shape cannot depend on a random or per-call SQL alias.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equivalent_query_shapes_generate_stable_sql(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var first = BuildParameterizedQuery(context, 3);
        var second = BuildParameterizedQuery(context, 3);

        // Act
        var sql = new[] { first.ToQueryString(), second.ToQueryString() };

        // Assert
        Assert.Equal(sql[0], sql[1]);
        Assert.Contains("@minimum", sql[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a fresh, equivalent expression tree while leaving scalar inputs parameterized.
    /// </summary>
    private static IQueryable<int> BuildParameterizedQuery(
        BindingContext context,
        int minimum
    )
        => context.Nodes.Where(node => node.Left > minimum)
            .Select(node => node.Id)
            .WithMaterializedSource(shared => shared.Concat(shared));

    /// <summary>
    /// Checks the provider-owned recursive fence without coupling tests to generated aliases.
    /// </summary>
    private static void AssertBinding(
        string sql
    )
    {
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Counts SQL tokens whose multiplicity distinguishes a definition from its references.
    /// </summary>
    private static int CountOccurrences(
        string value,
        string fragment
    )
    {
        var count = 0;
        var offset = 0;

        while ((offset = value.IndexOf(fragment, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }
}
