using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Protects ordering and rejection boundaries when linear SQL wrappers are traversed iteratively.</summary>
public sealed class MySqlInspectionTraversalTests
{
    /// <summary>Right-spine traversal retains each original clause in left-to-right order.</summary>
    /// <param name="depth">The number of nested conjunctions around the final clause.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public void Conjunction_flattening_preserves_clause_order(
        int depth
    )
    {
        // Arrange
        var clauses = Enumerable
            .Range(0, depth + 1)
            .Select(index => (SqlExpression)new ColumnExpression(
                "Clause" + index.ToString(CultureInfo.InvariantCulture),
                "source",
                typeof(bool),
                BoolTypeMapping.Default,
                nullable: false))
            .ToArray();

        var predicate = BuildConjunction(clauses);

        // Act
        var actual = Invoke<IReadOnlyList<SqlExpression>>(
            typeof(MySqlDynamicOffsetRewritingExpressionVisitor),
            "SplitConjunction",
            predicate);

        // Assert
        Assert.Equal(clauses.Length, actual.Count);

        // WHY: Positional reference identity detects reordering that set equality would miss.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < clauses.Length; index++)
        {
            Assert.Same(clauses[index], actual[index]);
        }
    }

    /// <summary>An absent predicate contributes no synthetic clause.</summary>
    [Fact]
    public void Absent_predicate_has_no_conjunction_clauses()
    {
        // Arrange
        object?[] arguments = [null];

        // Act
        var actual = Invoke<IReadOnlyList<SqlExpression>>(
            typeof(MySqlDynamicOffsetRewritingExpressionVisitor),
            "SplitConjunction",
            arguments);

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>A later repeated equality cannot resurrect an outer key whose inner candidates conflict.</summary>
    /// <param name="depth">The number of unrelated clauses between equality candidates.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public void Correlation_collection_keeps_conflicting_outer_keys_rejected(
        int depth
    )
    {
        // Arrange
        var outer = new ColumnExpression("Key", "outer", typeof(int), IntTypeMapping.Default, nullable: false);
        var first = new ColumnExpression("First", "inner", typeof(int), IntTypeMapping.Default, nullable: false);
        var conflicting = new ColumnExpression("Second", "inner", typeof(int), IntTypeMapping.Default, nullable: false);
        var firstEquality = new SqlBinaryExpression(
            ExpressionType.Equal,
            outer,
            first,
            typeof(bool),
            BoolTypeMapping.Default);

        var conflictingEquality = new SqlBinaryExpression(
            ExpressionType.Equal,
            outer,
            conflicting,
            typeof(bool),
            BoolTypeMapping.Default);

        SqlExpression[] clauses =
        [
            firstEquality,
            .. Enumerable.Repeat<SqlExpression>(new SqlConstantExpression(true, BoolTypeMapping.Default), depth),
            conflictingEquality,
            firstEquality,
        ];

        var predicate = BuildConjunction(clauses);
        var innerAliases = new HashSet<string>(StringComparer.Ordinal) { "inner" };

        // Act
        var actual = Invoke<Dictionary<(string TableAlias, string Name), ColumnExpression>>(
            typeof(MySqlLateralProjectionDecorrelationExpressionVisitor),
            "FindEquivalentInnerColumns",
            predicate,
            innerAliases);

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>Repeated proof of the same mapping retains the original inner-column identity.</summary>
    /// <param name="depth">The number of unrelated clauses between equivalent equality candidates.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Correlation_collection_retains_repeated_equivalent_key(
        int depth
    )
    {
        // Arrange
        var outer = new ColumnExpression("Key", "outer", typeof(int), IntTypeMapping.Default, nullable: false);
        var inner = new ColumnExpression("Key", "inner", typeof(int), IntTypeMapping.Default, nullable: false);
        var equality = new SqlBinaryExpression(
            ExpressionType.Equal,
            outer,
            inner,
            typeof(bool),
            BoolTypeMapping.Default);

        SqlExpression[] clauses =
        [
            equality,
            .. Enumerable.Repeat<SqlExpression>(new SqlConstantExpression(true, BoolTypeMapping.Default), depth),
            equality,
        ];

        var predicate = BuildConjunction(clauses);
        var innerAliases = new HashSet<string>(StringComparer.Ordinal) { "inner" };

        // Act
        var actual = Invoke<Dictionary<(string TableAlias, string Name), ColumnExpression>>(
            typeof(MySqlLateralProjectionDecorrelationExpressionVisitor),
            "FindEquivalentInnerColumns",
            predicate,
            innerAliases);

        // Assert
        Assert.Single(actual);
        Assert.Same(inner, actual[("outer", "Key")]);
    }

    /// <summary>Accepted collation and empty-coalesce wrappers retain the literal's scalar-character bound.</summary>
    /// <param name="depth">The number of alternating transparent wrappers.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public void Text_growth_keeps_bounds_through_transparent_wrappers(
        int depth
    )
    {
        // Arrange
        var expression = WrapText(new SqlConstantExpression("A\U0001F600B", StringTypeMapping.Default), depth);

        // Act
        var actual = Invoke<(bool Valid, int Frontiers, int Characters)>(
            typeof(MySqlRecursiveTextCapacity),
            "CountGrowth",
            expression,
            "frontier",
            "Value",
            "utf8mb4_unicode_ci");

        // Assert
        Assert.Equal((true, 0, 3), actual);
    }

    /// <summary>Transparent outer wrappers cannot hide an incompatible inner collation.</summary>
    /// <param name="depth">The number of accepted wrappers outside the rejected collation.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public void Text_growth_rejects_incompatible_collation_inside_wrappers(
        int depth
    )
    {
        // Arrange
        var mismatched = new CollateExpression(
            new SqlConstantExpression("text", StringTypeMapping.Default),
            "utf8mb4_bin");

        var expression = WrapText(mismatched, depth);

        // Act
        var actual = Invoke<(bool Valid, int Frontiers, int Characters)>(
            typeof(MySqlRecursiveTextCapacity),
            "CountGrowth",
            expression,
            "frontier",
            "Value",
            "utf8mb4_unicode_ci");

        // Assert
        Assert.Equal((false, 0, 0), actual);
    }

    /// <summary>Nested collation wrappers retain an actual literal bound without inventing storage width.</summary>
    /// <param name="depth">The number of collation wrappers around the literal.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Text_capacity_keeps_literal_bound_through_collation_wrappers(
        int depth
    )
    {
        // Arrange
        SqlExpression expression = new SqlConstantExpression("A\U0001F600B", StringTypeMapping.Default);

        for (var index = 0; index < depth; index++)
        {
            expression = new CollateExpression(expression, "utf8mb4_unicode_ci");
        }

        object?[] arguments = [expression, CreateScope(), 0];

        // Act
        var actual = Invoke<bool>(typeof(MySqlRecursiveTextCapacity), "TryMaximumCharacters", arguments);

        // Assert
        Assert.True(actual);
        Assert.Equal(3, Assert.IsType<int>(arguments[2]));
    }

    /// <summary>A nominal CLR string parameter has no proved finite source capacity.</summary>
    [Fact]
    public void Text_capacity_rejects_unbounded_parameter_inside_collation()
    {
        // Arrange
        var expression = new CollateExpression(
            new SqlParameterExpression("value", typeof(string), StringTypeMapping.Default),
            "utf8mb4_unicode_ci");

        object?[] arguments = [expression, CreateScope(), -1];

        // Act
        var actual = Invoke<bool>(typeof(MySqlRecursiveTextCapacity), "TryMaximumCharacters", arguments);

        // Assert
        Assert.False(actual);
        Assert.Equal(0, Assert.IsType<int>(arguments[2]));
    }

    /// <summary>A collation continuation retains the derived scope and requires the actual physical column's bound.</summary>
    /// <param name="storeType">The physical column's bounded or unbounded declaration.</param>
    /// <param name="hasPhysicalOrigin">Whether the inner SELECT contains the physical source.</param>
    /// <param name="expectedKnown">Whether a finite capacity can be proved.</param>
    /// <param name="expectedCapacity">The physical capacity, or zero when it cannot be proved.</param>
    [Theory]
    [InlineData("varchar(37)", true, true, 37)]
    [InlineData("varchar(37)", false, false, 0)]
    [InlineData("longtext", true, false, 0)]
    public void Text_capacity_follows_derived_collation_to_its_physical_origin(
        string storeType,
        bool hasPhysicalOrigin,
        bool expectedKnown,
        int expectedCapacity
    )
    {
        // Arrange
        var (expression, scope) = CreateDerivedCapacityScope(storeType, hasPhysicalOrigin);
        object?[] arguments = [expression, scope, -1];

        // Act
        var actual = Invoke<bool>(typeof(MySqlRecursiveTextCapacity), "TryMaximumCharacters", arguments);

        // Assert
        Assert.Equal(expectedKnown, actual);
        Assert.Equal(expectedCapacity, Assert.IsType<int>(arguments[2]));
    }

    /// <summary>Builds two completed projections whose nominal width differs from the actual physical declaration.</summary>
    private static (SqlExpression Expression, SelectExpression Scope) CreateDerivedCapacityScope(
        string storeType,
        bool hasPhysicalOrigin
    )
    {
        var physicalMapping = new StringTypeMapping(storeType, DbType.String);
        var projectedMapping = new StringTypeMapping("varchar(999)", DbType.String);

        // WHY: The actual ITable source distinguishes physical width from a nominal CLR projection mapping.
#pragma warning disable EF1001 // This fixture deliberately constructs EF10's concrete relational metadata and SQL nodes.
        var model = new RelationalModel(
            new RuntimeModel(false, Guid.Empty, 0));

        var table = new Table("Rows", null, model);
        var physical = new TableExpression("physical", table);
#pragma warning restore EF1001
        var inner = CreateScope("inner",
            hasPhysicalOrigin
                ? [physical]
                : [],
            [
                new ProjectionExpression(
                    new ColumnExpression("Value", "physical", typeof(string), physicalMapping, false),
                    "Text"),
            ]);

        var outer = CreateScope("outer",
            [inner],
            [
                new ProjectionExpression(new CollateExpression(
                        new ColumnExpression("Text", "inner", typeof(string), projectedMapping, false),
                        "utf8mb4_unicode_ci"),
                    "Text"),
            ]);

        return (new ColumnExpression("Text", "outer", typeof(string), projectedMapping, false),
            CreateScope("scope", [outer], []));
    }

    /// <summary>Creates the completed empty relational scope used by constant-only capacity proofs.</summary>
    private static SelectExpression CreateScope() => CreateScope("scope", [], []);

    /// <summary>Creates a completed relational fixture without a database or a synthetic provider.</summary>
    private static SelectExpression CreateScope(
        string alias,
        List<TableExpressionBase> tables,
        List<ProjectionExpression> projections
    )
    {
#pragma warning disable EF1001 // This fixture deliberately constructs the exact EF10 immutable relational scope.
        return SelectExpression.CreateImmutable(alias, tables, projections, new SqlAliasManager());
#pragma warning restore EF1001
    }

    /// <summary>Builds a right-associated predicate while retaining each supplied leaf identity.</summary>
    private static SqlExpression BuildConjunction(
        SqlExpression[] clauses
    )
    {
        var predicate = clauses[^1];

        // WHY: Each right-hand subtree is attached once; the saved order is the traversal contract.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = clauses.Length - 2; index >= 0; index--)
        {
            predicate = new SqlBinaryExpression(
                ExpressionType.AndAlso,
                clauses[index],
                predicate,
                typeof(bool),
                BoolTypeMapping.Default);
        }

        return predicate;
    }

    /// <summary>Alternates the exact transparent wrappers accepted by the recursive text proof.</summary>
    private static SqlExpression WrapText(
        SqlExpression expression,
        int depth
    )
    {
        for (var index = 0; index < depth; index++)
        {
            expression = index % 2 == 0
                ? new CollateExpression(expression, "utf8mb4_unicode_ci")
                : new SqlFunctionExpression(
                    "COALESCE",
                    [expression, new SqlConstantExpression(string.Empty, StringTypeMapping.Default)],
                    nullable: true,
                    argumentsPropagateNullability: [false, false],
                    typeof(string),
                    StringTypeMapping.Default);
        }

        return expression;
    }

    /// <summary>Exercises the existing private traversal seam without adding a production testing API.</summary>
    private static TResult Invoke<TResult>(
        Type owner,
        string name,
        params object?[] arguments
    )
    {
        var method = owner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The expected SQL traversal method was not found.");

        return (TResult)(method.Invoke(null, arguments)
            ?? throw new InvalidOperationException("The SQL traversal method returned no result."));
    }
}
