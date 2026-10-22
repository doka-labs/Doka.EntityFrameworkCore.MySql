namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMaterializedSourceTranslationTests
{
    /// <summary>A related entity cannot be silently removed from the completed source.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Non_owned_reference_include_in_source_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<BindingChild>().Include(child => child.Node)
            .WithMaterializedSource(shared => shared);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("non-owned reference Include", exception.Message, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Ordinary reference loading on composed output remains a single-query EF shape.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reference_include_on_composed_entity_remains_supported(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<BindingChild>()
            .WithMaterializedSource(shared => shared.Include(child => child.Node));

        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `BindingNodes`", sql, StringComparison.Ordinal);
    }

    /// <summary>An Include eliminated by scalar projection imposes no source loading contract.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Irrelevant_reference_include_in_scalar_source_remains_supported(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<BindingChild>().Include(child => child.Node).Select(child => child.Id)
            .WithMaterializedSource(shared => shared.Concat(shared));

        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN `BindingNodes`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A client-only source value cannot be reevaluated independently after the source fence.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Client_only_source_projection_fails_before_execution(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Select(node => new { node.Id, Name = ClientOnly(node.Name) })
            .WithMaterializedSource(shared => shared);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Query roots from another context cannot silently share the binder's execution lifetime.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Foreign_context_in_composition_fails_before_execution(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        using var other = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Select(node => node.Id)
            .WithMaterializedSource(shared => shared.Join(
                other.Nodes,
                id => id,
                node => node.Id,
                (_, node) => node.Id));

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("context", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(other.ChangeTracker.Entries());
    }

    /// <summary>
    /// Deferred source collections do not satisfy the flat SQL-row source contract.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Collection_in_source_projection_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Select(node => new
            {
                node.Id,
                Children = node.Children.ToList(),
            })
            .WithMaterializedSource(shared => shared);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An unexpanded source Include cannot become an implicit collection-shaped source buffer.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_include_in_source_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Include(node => node.Children)
            .WithMaterializedSource(shared => shared);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// String-based source Includes obey the same flat-source boundary as typed Includes.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void String_collection_include_in_source_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Include(nameof(BindingNode.Children))
            .WithMaterializedSource(shared => shared);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A collection reached through ThenInclude cannot bypass the flat-source boundary.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Chained_collection_include_in_source_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<BindingChild>()
            .Include(child => child.Node)
            .ThenInclude(node => node.Children)
            .WithMaterializedSource(shared => shared);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A per-row correlated input cannot be hoisted into one statement-level source definition.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Correlated_source_cannot_be_hoisted_outside_its_row_scope(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.SelectMany(outer => context.Nodes
            .Where(inner => inner.Id == outer.Id)
            .WithMaterializedSource(shared => shared));

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A new binding over an existing source still cannot capture one of that source's consumer rows.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Correlated_bound_source_cannot_reuse_an_enclosing_row_alias(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Select(node => node.Id)
            .WithMaterializedSource(shared => shared.SelectMany(outer => shared
                .Where(inner => inner == outer)
                .WithMaterializedSource(inner => inner)));

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Automatic collection Includes cannot introduce deferred work into a bound entity source.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Automatic_collection_include_in_source_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateAutoIncludeContext(mariaDb);

        // Act
        var query = context.Nodes.WithMaterializedSource(shared => shared);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Moving an Include outside the binder does not turn its directly bound entity into a flat source.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Collection_include_on_directly_bound_output_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.WithMaterializedSource(shared => shared)
            .Include(node => node.Children);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Split collection loading would enumerate the statement-local binding in another command.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Split_query_collection_output_is_rejected(
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
            .AsSplitQuery();

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A context-wide split-query default cannot bypass the statement-lifetime guard.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Global_split_collection_default_is_rejected(
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
            .Include(node => node.Children);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A source reference cannot escape its statement inside a deferred IQueryable result value.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_queryable_result_cannot_escape_the_binding(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Select(node => node.Id)
            .WithMaterializedSource(shared => shared.Select(id => new
            {
                Id = id,
                Rows = shared.Where(other => other == id),
            }));

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("query", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An ordinary untranslatable predicate must still fail rather than switch to client evaluation.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Untranslatable_consumer_predicate_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Nodes.Select(node => node.Name)
            .WithMaterializedSource(shared => shared.Where(name => ClientOnly(name) == "alpha"));

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Supplies a deliberately untranslated operation without side effects.
    /// </summary>
    private static string ClientOnly(
        string value
    ) => $"client:{value}";
}
