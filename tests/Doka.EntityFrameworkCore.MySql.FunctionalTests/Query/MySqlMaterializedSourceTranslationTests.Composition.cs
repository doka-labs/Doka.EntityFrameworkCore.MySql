namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMaterializedSourceTranslationTests
{
    /// <summary>
    /// Independent bindings retain separate source definitions even when their shapes match.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Independent_bindings_do_not_merge_volatile_sources(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var first = context.Nodes.Select(node => new { node.Id, Token = Guid.NewGuid() })
            .WithMaterializedSource(shared => shared);

        var second = context.Nodes.Select(node => new { node.Id, Token = Guid.NewGuid() })
            .WithMaterializedSource(shared => shared);

        // Act
        var query = first.Concat(second);

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(2, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.Equal(2, CountOccurrences(sql, "UUID()"));
    }

    /// <summary>
    /// Lexically nested bindings keep both identities available to the inner composition.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nested_bindings_resolve_the_correct_lexical_reference(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Nodes.Select(node => new { node.Id, node.Left });

        // Act
        var query = source.WithMaterializedSource(outer => context.Nodes
            .Where(node => node.Left > 3)
            .Select(node => new { node.Id, node.Right })
            .WithMaterializedSource(inner => outer.Join(
                inner,
                first => first.Id,
                second => second.Id,
                (first, second) => new { first.Left, second.Right })));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(2, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.Contains("`Left`", sql, StringComparison.Ordinal);
        Assert.Contains("`Right`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A nested binding may depend on the complete outer binding without capturing an outer row.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dependent_nested_binding_declares_sources_in_dependency_order(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Nodes.Select(node => new { node.Id, node.Left });

        // Act
        var query = source.WithMaterializedSource(outer => outer
            .Where(row => row.Left > 3)
            .WithMaterializedSource(inner => outer.Join(
                inner,
                first => first.Id,
                second => second.Id,
                (first, second) => new { First = first.Left, Second = second.Left })));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.Equal(2, CountOccurrences(sql, "WHERE 1 = 0"));
        // WHY: The second source reads the first CTE; declaration presence alone does not prove legal dependency order.
        var firstDefinition = sql.IndexOf("`__doka_source_0` (", StringComparison.Ordinal);
        var secondDefinition = sql.IndexOf("`__doka_source_1` (", StringComparison.Ordinal);
        Assert.True(firstDefinition >= 0 && secondDefinition > firstDefinition, sql);
    }

    /// <summary>
    /// The CTE's generated identity must not shadow a physical table with a plausible binder name.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generated_binding_name_does_not_shadow_a_physical_source_table(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Collisions.Select(row => row.Id)
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `__doka_source_0`"));
        Assert.DoesNotContain("`__doka_source_0` AS (", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`__doka_source_0` (`", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Raw SQL table references also reserve their physical names before CTE names are allocated.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Raw_sql_source_keeps_its_physical_table_visible(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var minimum = 3;

        // Act
        var query = context.Collisions
            .FromSqlInterpolated($"SELECT `Id` FROM `__doka_source_0` WHERE `Id` > {minimum}")
            .Select(row => row.Id)
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `__doka_source_0`"));
        Assert.DoesNotContain("`__doka_source_0` AS (", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`__doka_source_0` (`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@p0", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A narrow structural source can rehydrate ordinary entities with their collection identifiers.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flat_source_allows_normal_entity_and_collection_output(
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
        var query = source.WithMaterializedSource(shared => shared.Join(
                context.Nodes,
                row => row.Id,
                node => node.Id,
                (_, node) => node)
            .Select(node => new
            {
                Node = node,
                Children = node.Children.OrderBy(child => child.Id)
                    .Select(child => new { child.Id, child.Name })
                    .ToList(),
            }));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Contains("BindingChildren", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Owned collection identifiers remain available after rehydrating owners from a flat source.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flat_source_allows_owned_collection_output(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<OwnedRecord>().Select(row => row.Id)
            .WithMaterializedSource(shared => shared.Join(
                context.Set<OwnedRecord>(),
                id => id,
                row => row.Id,
                (_, row) => row)
                .Select(row => new
                {
                    row.Id,
                    Values = row.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToList(),
                }));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Contains("BindingOwnedTags", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An explicit single-query override restores the one-statement contract under a split default.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Single_query_override_allows_a_context_with_split_default(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb, splitQueryDefault: true);

        // Act
        var query = context.Nodes.Select(node => node.Id)
            .WithMaterializedSource(shared => shared.Join(
                context.Nodes,
                id => id,
                node => node.Id,
                (_, node) => node))
            .Include(node => node.Children)
            .AsSingleQuery();

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Contains("BindingChildren", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Explicitly ignoring automatic Includes yields a flat entity source.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ignored_automatic_includes_allow_a_flat_entity_source(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateAutoIncludeContext(mariaDb);

        // Act
        var query = context.Nodes.IgnoreAutoIncludes().WithMaterializedSource(shared => shared);

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.DoesNotContain("BindingChildren", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scalar projection discards an explicit Include before the completed source is bound.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ignored_include_before_scalar_projection_allows_binding(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Include(node => node.Children)
            .Select(node => node.Id)
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.DoesNotContain("BindingChildren", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scalar projection discards a model-level automatic Include without requiring an opt-out.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Automatic_include_does_not_poison_a_flat_scalar_source(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateAutoIncludeContext(mariaDb);

        // Act
        var query = context.Nodes.Select(node => node.Id)
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Equal(1, CountOccurrences(sql, "FROM `BindingNodes`"));
        Assert.DoesNotContain("BindingChildren", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rehydrated entities may retain ordinary Include shaping in a single statement.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flat_source_allows_single_query_include_on_rehydrated_output(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Select(node => node.Id)
            .WithMaterializedSource(shared => shared.Join(
                context.Nodes,
                id => id,
                node => node.Id,
                (_, node) => node))
            .Include(node => node.Children)
            .AsSingleQuery();

        var sql = query.ToQueryString();

        AssertBinding(sql);

        // Assert
        Assert.Contains("BindingChildren", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }
}
