namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies one explicitly directed join while retaining ordinary EF query semantics.</summary>
public sealed partial class MySqlStraightJoinTests
{
    /// <summary>Enumerates both supported database families and both EF null-comparison modes.</summary>
    public static TheoryData<bool, bool, string> Shapes => new()
    {
        { false, false, "scalar" }, { true, false, "scalar" },
        { false, true, "scalar" }, { true, true, "scalar" },
        { false, false, "nullable" }, { true, false, "nullable" },
        { false, true, "nullable" }, { true, true, "nullable" },
        { false, false, "composite" }, { true, false, "composite" },
        { false, true, "composite" }, { true, true, "composite" },
        { false, false, "converted" }, { true, false, "converted" },
        { false, true, "converted" }, { true, true, "converted" },
        { false, false, "entity" }, { true, false, "entity" },
        { false, false, "reference" }, { true, false, "reference" },
        { false, false, "reference-navigation" }, { true, false, "reference-navigation" },
        { false, true, "reference-navigation" }, { true, true, "reference-navigation" },
        { false, false, "owned-collection" }, { true, false, "owned-collection" },
        { false, false, "collection" }, { true, false, "collection" },
        { false, false, "constant-true" }, { true, false, "constant-true" },
        { false, false, "empty" }, { true, false, "empty" },
        { false, false, "paging" }, { true, false, "paging" },
        { false, false, "nested" }, { true, false, "nested" },
        { false, false, "repeated" }, { true, false, "repeated" },
    };

    /// <summary>The directive changes only selected edge grammar, including null and navigation processing.</summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void Translation_matches_the_corresponding_ordinary_join(bool mariaDb, bool relationalNulls, string shape)
    {
        using var context = CreateContext(mariaDb, relationalNulls);
        var ordinary = Query(context, shape, directed: false).ToQueryString();
        var expectedEdges = shape == "repeated" ? 2 : 1;

        var actual = Query(context, shape, directed: true).ToQueryString();

        if (shape == "constant-true")
        {
            // WHY: EF simplifies an ordinary TRUE inner join to CROSS JOIN; directed grammar must
            // retain its edge. Removing only this tautology compares their otherwise identical SQL.
            Assert.Equal(Normalize(ordinary).Replace("CROSS JOIN", "INNER JOIN", StringComparison.Ordinal),
                Regex.Replace(Normalize(actual), @" ON (?:TRUE|1)(?=\r?\n|$)", string.Empty));
        }
        else
        {
            Assert.Equal(Normalize(ordinary), Normalize(actual));
        }

        Assert.Equal(expectedEdges, Regex.Count(actual, @"\bSTRAIGHT_JOIN\b"));
        Assert.Equal(0, context.Blocker.Openings);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The operator keeps its exact public identity and never invokes a selector during construction.</summary>
    [Fact]
    public void Construction_preserves_provider_and_public_expression_identity()
    {
        using var context = CreateContext();
        var source = context.Nodes.Where(value => value.Visible);

        var query = source.StraightJoin(source, value => value.Id, value => value.Id, (left, right) => left.Id);

        Assert.Same(source.Provider, query.Provider);
        var call = Assert.IsAssignableFrom<MethodCallExpression>(query.Expression);
        Assert.Equal(MySqlQueryableExtensions.StraightJoinMethod, call.Method.GetGenericMethodDefinition());
        Assert.Equal(0, context.Blocker.Openings);
    }

    /// <summary>Every missing argument is rejected before a provider or query selector is used.</summary>
    [Theory]
    [InlineData("outer")]
    [InlineData("inner")]
    [InlineData("outerKeySelector")]
    [InlineData("innerKeySelector")]
    [InlineData("resultSelector")]
    public void Null_arguments_fail_at_the_public_boundary(string argument)
    {
        using var context = CreateContext();
        IQueryable<JoinNode>? outer = argument == "outer" ? null : context.Nodes;
        IQueryable<JoinNode>? inner = argument == "inner" ? null : context.Nodes;
        Expression<Func<JoinNode, int>>? outerKey = argument == "outerKeySelector" ? null : value => value.Id;
        Expression<Func<JoinNode, int>>? innerKey = argument == "innerKeySelector" ? null : value => value.Id;
        Expression<Func<JoinNode, JoinNode, int>>? result = argument == "resultSelector" ? null : (left, right) => left.Id;

        var error = Assert.Throws<ArgumentNullException>(() => outer!.StraightJoin(inner!, outerKey!, innerKey!, result!));

        Assert.Equal(argument, error.ParamName);
        Assert.Equal(0, context.Blocker.Openings);
    }

    /// <summary>LINQ-to-Objects does not execute provider-only join semantics accidentally.</summary>
    [Fact]
    public void Enumerable_inputs_are_rejected()
    {
        var input = Array.Empty<int>().AsQueryable();

        var error = Assert.Throws<InvalidOperationException>(() => input.StraightJoin(input, value => value, value => value,
            (left, right) => left));

        Assert.Contains("provider", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sources from different contexts are rejected before SQL translation or connection access.</summary>
    [Fact]
    public void Different_provider_instances_are_rejected()
    {
        using var first = CreateContext();
        using var second = CreateContext();

        var error = Assert.Throws<InvalidOperationException>(() => first.Nodes.StraightJoin(second.Nodes,
            value => value.Id, value => value.Id, (left, right) => left));

        Assert.Contains("same", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, first.Blocker.Openings + second.Blocker.Openings);
    }

    /// <summary>Async translation reaches only the deliberately blocked opening after retaining the directed edge.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Async_query_translates_before_the_blocked_connection(bool mariaDb, bool relationalNulls)
    {
        await using var context = CreateContext(mariaDb, relationalNulls);
        var query = context.Nodes.StraightJoin(context.Nodes.Where(value => value.ParentId != null),
            value => value.Id, value => value.ParentId!.Value, (left, right) => new { left.Id, Child = right.Id });
        var sql = query.ToQueryString();

        var error = await Assert.ThrowsAsync<ConnectionBlockedException>(() => query.ToArrayAsync(CancellationToken.None));

        Assert.NotNull(error);
        Assert.Contains("STRAIGHT_JOIN", sql, StringComparison.Ordinal);
        Assert.Equal(1, context.Blocker.Openings);
    }

    /// <summary>Fresh captured values participate in EF's ordinary parameter extraction and query cache.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_construction_keeps_fresh_parameter_values(bool mariaDb)
    {
        using var context = CreateContext(mariaDb);
        var minimum = 11;
        var first = context.Nodes.Where(value => value.Id >= minimum).StraightJoin(context.Nodes,
            value => value.Id, value => value.Id, (left, right) => left.Id).ToQueryString();
        minimum = 29;

        var second = context.Nodes.Where(value => value.Id >= minimum).StraightJoin(context.Nodes,
            value => value.Id, value => value.Id, (left, right) => left.Id).ToQueryString();

        Assert.Contains("'11'", first, StringComparison.Ordinal);
        Assert.Contains("'29'", second, StringComparison.Ordinal);
        Assert.Equal(Normalize(first), Normalize(second));
        Assert.Equal(0, context.Blocker.Openings);
    }

    /// <summary>Builds equivalent expression trees without hiding any provider branch inside application callbacks.</summary>
    private static IQueryable Query(JoinContext context, string shape, bool directed)
    {
        var outer = context.Nodes.Where(value => value.Id > 0);
        var inner = context.Nodes.Where(value => value.Id < 20);

        return shape switch
        {
            "nullable" => Join(outer, inner, left => left.OptionalKey, right => right.OptionalKey,
                (left, right) => new { left.Id, Child = right.Id }, directed),
            "composite" => Join(outer, inner, left => new { left.TenantId, left.OptionalKey },
                right => new { right.TenantId, right.OptionalKey }, (left, right) => new { left.Id, Child = right.Id }, directed),
            "converted" => Join(outer, inner, left => left.Code, right => right.Code,
                (left, right) => new { left.Id, Child = right.Id }, directed),
            "entity" => Join(outer, inner, left => left.Id, right => right.Id, (left, right) => right, directed),
            "reference" => Join(outer, inner, left => left.Id, right => right.Id,
                (left, right) => new { left.Id, right.Details.Text }, directed),
            "reference-navigation" => Join(outer, inner, left => left.Id, right => right.Id,
                (left, right) => new
                {
                    left.Id,
                    OuterParent = left.Parent == null ? null : left.Parent.Details.Text,
                    InnerParent = right.Parent == null ? null : right.Parent.Details.Text,
                }, directed),
            "owned-collection" => Join(outer, inner, left => left.Id, right => right.Id,
                (left, right) => new { left.Id, Tags = right.Tags.Select(tag => tag.Text).ToArray() }, directed),
            "collection" => Join(outer, inner, left => left.Id, right => right.Id,
                (left, right) => new { left.Id, Items = right.Items.Select(item => item.Text).ToArray() }, directed),
            "constant-true" => Join(outer, inner, left => 1, right => 1, (left, right) => new { left.Id, Child = right.Id }, directed),
            "empty" => Join(outer.Where(value => false), inner, left => left.Id, right => right.Id,
                (left, right) => new { left.Id, Child = right.Id }, directed),
            "paging" => Join(outer.OrderBy(value => value.Id).Skip(2).Take(7), inner.OrderBy(value => value.Id).Take(9),
                left => left.Id, right => right.Id, (left, right) => new { left.Id, Child = right.Id }, directed)
                .OrderBy(value => value.Child).Skip(1).Take(3),
            "nested" => Join(Join(outer, inner, left => left.Id, right => right.Id, (left, right) => new { left.Id }, false),
                context.Nodes, left => left.Id, right => right.Id, (left, right) => new { left.Id, Child = right.Id }, directed),
            "repeated" => Join(Join(outer, inner, left => left.Id, right => right.Id, (left, right) => new { left.Id }, directed),
                context.Nodes, left => left.Id, right => right.Id, (left, right) => new { left.Id, Child = right.Id }, directed),
            _ => Join(outer, inner, left => left.Id, right => right.Id, (left, right) => new { left.Id, Child = right.Id }, directed),
        };
    }

    /// <summary>Selects the public operator only while constructing the test's complete EF expression.</summary>
    private static IQueryable<TResult> Join<TOuter, TInner, TKey, TResult>(IQueryable<TOuter> outer, IQueryable<TInner> inner,
        Expression<Func<TOuter, TKey>> outerKey, Expression<Func<TInner, TKey>> innerKey,
        Expression<Func<TOuter, TInner, TResult>> result, bool directed)
        => directed ? outer.StraightJoin(inner, outerKey, innerKey, result) : outer.Join(inner, outerKey, innerKey, result);

    /// <summary>Compares parameterized SQL structure independently of values and the explicitly selected edge grammar.</summary>
    private static string Normalize(string sql) => Regex.Replace(sql, @"^(?:SET|--)[^\n]*\n", string.Empty, RegexOptions.Multiline)
        .Replace("STRAIGHT_JOIN", "INNER JOIN", StringComparison.Ordinal);
}
