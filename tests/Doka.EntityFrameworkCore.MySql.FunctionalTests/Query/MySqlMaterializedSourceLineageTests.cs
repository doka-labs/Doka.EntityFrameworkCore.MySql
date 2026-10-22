using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies exact mapping and temporal lineage across statement-local source leaves.</summary>
public sealed partial class MySqlMaterializedSourceLineageTests
{
    private static readonly DateTime s_firstPoint = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime s_secondPoint = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    /// <summary>Named shared mappings remain usable as flat source rows.</summary>
    [Theory]
    [InlineData(false, "First", "LineageFirst")]
    [InlineData(false, "Second", "LineageSecond")]
    [InlineData(true, "First", "LineageFirst")]
    [InlineData(true, "Second", "LineageSecond")]
    public void Named_shared_source_preserves_exact_mapping(
        bool mariaDb,
        string name,
        string table
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<SharedNode>(name).Where(row => row.Id > 1)
            .WithMaterializedSource(shared => shared.Select(row => new { row.Id, row.Name }));

        var sql = query.ToQueryString();

        // Assert
        Assert.Contains($"FROM `{table}`", sql, StringComparison.Ordinal);
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
    }

    /// <summary>Navigation expansion uses the selected named mapping rather than its shared CLR type.</summary>
    [Theory]
    [InlineData(false, "First", "LineageFirst")]
    [InlineData(false, "Second", "LineageSecond")]
    [InlineData(true, "First", "LineageFirst")]
    [InlineData(true, "Second", "LineageSecond")]
    public void Named_shared_source_preserves_navigation_identity(
        bool mariaDb,
        string name,
        string table
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<SharedNode>(name).Where(row => row.Id > 1)
            .WithMaterializedSource(shared => shared.Select(row => row.Target.Name));

        var sql = query.ToQueryString();

        // Assert
        Assert.Contains($"FROM `{table}`", sql, StringComparison.Ordinal);
        Assert.Contains("LineageTargets", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN", sql, StringComparison.Ordinal);
    }

    /// <summary>An identity selector does not erase the exact named entity source.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entity_identity_projection_retains_named_lineage(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);

        // Act
        var query = context.Set<SharedNode>("Second").Select(row => row)
            .WithMaterializedSource(shared => shared.Select(row => row.Target.Name));

        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("FROM `LineageSecond`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `LineageFirst`", sql, StringComparison.Ordinal);
        Assert.Contains("LineageTargets", sql, StringComparison.Ordinal);
    }

    /// <summary>A join's result selector determines the retained named mapping.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entity_join_result_retains_the_selected_named_mapping(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Set<SharedNode>("First").Join(
            context.Set<SharedNode>("Second"),
            first => first.Id,
            second => second.Id,
            (_, second) => second);

        // Act
        var query = source.WithMaterializedSource(shared => shared.Select(row => row.Target.Name));

        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("FROM `LineageFirst`", sql, StringComparison.Ordinal);
        Assert.Contains("SecondTargetId", sql, StringComparison.Ordinal);
        Assert.Contains("LineageTargets", sql, StringComparison.Ordinal);
    }

    /// <summary>AsOf semantics continue onto separately stored navigation targets after binding.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Temporal_navigation_after_binding_keeps_the_original_instant(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Parents.TemporalAsOf(s_firstPoint).Where(parent => parent.Id > 1)
            .WithMaterializedSource(shared => shared.Select(parent => parent.Child.Name));

        // Act
        var sql = query.ToQueryString();

        // Assert
        AssertTemporalReads(sql, mariaDb, 2);
        AssertHistoricalTable(sql, mariaDb, "LineageParents");
        AssertHistoricalTable(sql, mariaDb, "LineageChildren");
    }

    /// <summary>Projecting a mapped navigation retains its historical origin for later navigation expansion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Projected_temporal_entity_preserves_navigation_lineage(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = context.Parents.TemporalAsOf(s_firstPoint).Select(parent => parent.Child);
        var query = source.WithMaterializedSource(shared => shared.Select(child => child.Next!.Name));

        // Act
        var sql = query.ToQueryString();

        // Assert
        AssertTemporalReads(sql, mariaDb, 3);
        AssertHistoricalTable(sql, mariaDb, "LineageParents");
        AssertHistoricalTable(sql, mariaDb, "LineageChildren");
    }

    /// <summary>Historical and current bound entity roots cannot be mixed as though both were current.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Temporal_and_current_bound_roots_reject_set_operations(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var historical = context.Parents.TemporalAsOf(s_firstPoint).WithMaterializedSource(shared => shared);
        var current = context.Parents.WithMaterializedSource(shared => shared);

        // Act
        var query = historical.Concat(current);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("matching temporal operators", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Different instants remain incompatible even when both roots are hidden behind source leaves.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Different_temporal_boundaries_reject_bound_set_operations(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var first = context.Parents.TemporalAsOf(s_firstPoint).WithMaterializedSource(shared => shared);
        var second = context.Parents.TemporalAsOf(s_secondPoint).WithMaterializedSource(shared => shared);

        // Act
        var query = first.Concat(second);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("identical UTC boundaries", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Matching instants remain valid across independent bindings.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Matching_temporal_boundaries_allow_bound_set_operations(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var first = context.Parents.TemporalAsOf(s_firstPoint).WithMaterializedSource(shared => shared);
        var second = context.Parents.TemporalAsOf(s_firstPoint).WithMaterializedSource(shared => shared);
        var query = first.Concat(second);

        // Act
        var sql = query.ToQueryString();

        // Assert
        AssertTemporalReads(sql, mariaDb, 2);
    }

    /// <summary>Application-time navigation guards observe the original mutation-only root.</summary>
    [Fact]
    public void Bound_application_time_root_preserves_navigation_rejection()
    {
        // Arrange
        using var context = CreateApplicationContext();

        // Act
        var query = context.Parents.ForPortionOf(s_firstPoint, s_secondPoint)
            .WithMaterializedSource(shared => shared.Select(parent => parent.Child.Name));

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        // Assert
        Assert.Contains("Navigation expansion", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("mutation-only", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Two bound application-time roots still require identical range semantics.</summary>
    [Fact]
    public void Bound_application_time_roots_preserve_range_compatibility()
    {
        // Arrange
        using var context = CreateApplicationContext();
        var first = ExtractRoot(context, context.Parents.ForPortionOf(s_firstPoint, s_secondPoint));
        var second = ExtractRoot(context, context.Parents.ForPortionOf(s_firstPoint, s_secondPoint.AddDays(1)));
        var helper = context.GetService<INavigationExpansionExtensibilityHelper>();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => helper.AreQueryRootsCompatible(first, second));

        // Assert
        Assert.Contains("identical FOR PORTION OF boundaries", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Extracts a lexical entity leaf without SQL preprocessing or database access.</summary>
    private static EntityQueryRootExpression ExtractRoot<TEntity>(
        DbContext context,
        IQueryable<TEntity> source
    )
    {
        var definitions = new Dictionary<int, Expression>();
        var query = source.WithMaterializedSource(shared => shared);
        var root = new MySqlMaterializedSourceBinding(context.Model, definitions).Visit(query.Expression);

        return (EntityQueryRootExpression)root;
    }

    /// <summary>Checks native versus emulated historical table reads without depending on aliases.</summary>
    private static void AssertHistoricalTable(
        string sql,
        bool mariaDb,
        string table
    )
        => Assert.Contains(mariaDb ? $"`{table}` FOR SYSTEM_TIME" : $"FROM `{table}History`", sql,
            StringComparison.Ordinal);

    /// <summary>Counts historical roots for both provider-owned temporal lowerings.</summary>
    private static void AssertTemporalReads(
        string sql,
        bool mariaDb,
        int count
    )
        => Assert.Equal(count, CountOccurrences(sql, mariaDb ? "FOR SYSTEM_TIME AS OF" : "History`"));

    /// <summary>Counts statement fragments whose multiplicity establishes root propagation.</summary>
    private static int CountOccurrences(
        string value,
        string fragment
    )
    {
        var count = 0;
        var offset = 0;

        while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }
}
