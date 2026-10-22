using System.Text;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Protects complete JSON SQL generation and parameter-dependent limit traversal.</summary>
public sealed class MySqlEnumerationQueryTests
{
    /// <summary>
    /// Preserves static, indexed, escaped, and dynamic JSON scalar paths through complete SQL generation.
    /// </summary>
    /// <param name="mariaDb">Whether the context uses the MariaDB engine profile.</param>
    /// <param name="scenario">The complete path shape to render.</param>
    /// <param name="expectedPath">The independently specified SQL path expression.</param>
    [Theory]
    [MemberData(nameof(JsonScalarCases))]
    public void JsonScalarPathsPreserveCompleteSql(
        bool mariaDb,
        string scenario,
        string expectedPath
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var sql = context.GetService<ISqlExpressionFactory>();
        var scalar = new JsonScalarExpression(
            sql.Constant("{}", MySqlStringTypeMapping.Default),
            CreatePath(scenario),
            typeof(string),
            MySqlStringTypeMapping.Default,
            nullable: true);

        var select = CreateSelect(scalar);
        var extract = "JSON_EXTRACT('{}', " + expectedPath + ")";
        var expected = scenario == "empty"
            ? "SELECT '{}' AS `Value`"
            : "SELECT CASE WHEN JSON_TYPE(" + extract + ") = 'NULL' THEN NULL ELSE JSON_UNQUOTE("
                + extract + ") END AS `Value`";

        // Act
        var actual = context.GetService<IQuerySqlGeneratorFactory>().Create().GetCommand(select).CommandText;

        // Assert
        Assert.Equal(expected, actual, ignoreLineEndingDifferences: true);
    }

    /// <summary>Preserves row-source wildcards, explicit indices, dynamic extraction, and per-column paths.</summary>
    /// <param name="mariaDb">Whether the context uses the MariaDB engine profile.</param>
    /// <param name="scenario">The row-source path shape.</param>
    /// <param name="expectedSource">The independently specified JSON_TABLE source and row path.</param>
    [Theory]
    [MemberData(nameof(JsonTableCases))]
    public void JsonTablePathsPreserveCompleteSql(
        bool mariaDb,
        string scenario,
        string expectedSource
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var sql = context.GetService<ISqlExpressionFactory>();
        var path = scenario == "null" ? null : CreatePath(scenario);
        IReadOnlyList<PathSegment> columnPath = scenario is "empty" or "null" ? [] : [new("nested"), new("value")];
        var table = new MySqlJsonTableExpression(
            "j",
            sql.Constant("[]", MySqlStringTypeMapping.Default),
            path,
            [new MySqlJsonTableExpression.ColumnInfo("Value", IntTypeMapping.Default, columnPath)]);

        var select = CreateSelect(
            new ColumnExpression("Value", "j", typeof(int), IntTypeMapping.Default, nullable: false),
            [table]);

        var expectedColumnPath = scenario is "empty" or "null" ? "'$'" : "'$.nested.value'";
        var expected = "SELECT `j`.`Value`" + Environment.NewLine
            + "FROM JSON_TABLE(" + expectedSource + " COLUMNS (`Value` int PATH " + expectedColumnPath + ")) AS `j`";

        // Act
        var actual = context.GetService<IQuerySqlGeneratorFactory>().Create().GetCommand(select).CommandText;

        // Assert
        Assert.Equal(expected, actual, ignoreLineEndingDifferences: true);
    }

    /// <summary>Rejects an invalid UTF-16 property name instead of dropping a late path segment.</summary>
    /// <param name="mariaDb">Whether the context uses the MariaDB engine profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonPathWithInvalidLatePropertyIsRejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var sql = context.GetService<ISqlExpressionFactory>();
        var scalar = new JsonScalarExpression(
            sql.Constant("{}", MySqlStringTypeMapping.Default),
            [new PathSegment("valid"), new PathSegment("\uD800")],
            typeof(string),
            MySqlStringTypeMapping.Default,
            nullable: true);

        var select = CreateSelect(scalar);

        // Act
        var exception = Record.Exception(() => context.GetService<IQuerySqlGeneratorFactory>()
            .Create().GetCommand(select));

        // Assert
        Assert.IsType<EncoderFallbackException>(exception);
    }

    /// <summary>
    /// Evaluates the complete nested limit using current parameter values while retaining its CLR type.
    /// </summary>
    /// <param name="mariaDb">Whether the context uses the MariaDB engine profile.</param>
    /// <param name="scenario">The parameter, nesting, and numeric boundary.</param>
    /// <param name="expected">The final nonnegative limit.</param>
    [Theory]
    [MemberData(nameof(LeastCases))]
    public void NestedLeastUsesEveryArgumentAndCurrentParameters(
        bool mariaDb,
        string scenario,
        int expected
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var sql = context.GetService<ISqlExpressionFactory>();
        var useLong = scenario == "long";
        var clrType = useLong ? typeof(long) : typeof(int);
        RelationalTypeMapping mapping = useLong ? LongTypeMapping.Default : IntTypeMapping.Default;
        var arguments = new List<SqlExpression>
        {
            new SqlParameterExpression("first", clrType, mapping),
            new SqlParameterExpression("second", clrType, mapping),
        };

        SqlExpression least = Least(sql, arguments, clrType, mapping);

        if (scenario == "nested")
        {
            least = Least(sql, [sql.Constant(99), least, sql.Constant(7)], clrType, mapping);
        }

        if (scenario == "last-minimum")
        {
            least = Least(sql, [sql.Constant(99), least, sql.Constant(1)], clrType, mapping);
        }

        var select = CreateSelect(sql.Constant(1), limit: least);
        var values = new Dictionary<string, object?>
        {
            ["first"] = scenario == "negative" ? -3 : 30,
            ["second"] = 20,
        };

        var parameters = new ParametersCacheDecorator(values);
        var visitor = new MySqlLimitExpressionEvaluatingExpressionVisitor(sql, parameters);

        // Act
        var actual = (SelectExpression)visitor.Visit(select);

        // Assert
        var constant = Assert.IsType<SqlConstantExpression>(actual.Limit);

        Assert.Equal(clrType, constant.Type);
        Assert.Equal(useLong ? (object)(long)expected : expected, constant.Value);
        Assert.Same(least, select.Limit);
        Assert.False(CanCache(parameters));
    }

    /// <summary>
    /// Rejects missing, null, nonnumeric, and unresolved arguments instead of accepting a partial minimum.
    /// </summary>
    /// <param name="mariaDb">Whether the context uses the MariaDB engine profile.</param>
    /// <param name="scenario">The invalid argument or result-type boundary.</param>
    /// <param name="position">The position of the invalid argument when applicable.</param>
    [Theory]
    [MemberData(nameof(InvalidLeastCases))]
    public void InvalidLeastCannotUseAnEarlierPartialMinimum(
        bool mariaDb,
        string scenario,
        int position
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var sql = context.GetService<ISqlExpressionFactory>();
        var arguments = new List<SqlExpression> { sql.Constant(9), sql.Constant(8), sql.Constant(7) };
        var values = new Dictionary<string, object?>();

        if (scenario == "empty")
        {
            arguments.Clear();
        }
        else if (scenario == "unresolved")
        {
            arguments[position] = new ColumnExpression(
                "Value", "unresolved", typeof(int), IntTypeMapping.Default, nullable: false);
        }
        else if (scenario != "nonintegral")
        {
            arguments[position] = new SqlParameterExpression("invalid", typeof(int), IntTypeMapping.Default);

            if (scenario == "null")
            {
                values["invalid"] = null;
            }
            else if (scenario == "nonnumeric")
            {
                values["invalid"] = "not-a-number";
            }
        }

        var least = scenario == "nonintegral"
            ? Least(sql, arguments, typeof(decimal), DecimalTypeMapping.Default)
            : Least(sql, arguments, typeof(int), IntTypeMapping.Default);

        var select = CreateSelect(sql.Constant(1), limit: least);
        var parameters = new ParametersCacheDecorator(values);
        var visitor = new MySqlLimitExpressionEvaluatingExpressionVisitor(sql, parameters);

        // Act
        var exception = Record.Exception(() => visitor.Visit(select));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);

        Assert.Equal(
            "MySQL LIMIT requires an integral value or parameter, but the combined row-limit "
            + "expression could not be evaluated.",
            failure.Message);
        Assert.Same(least, select.Limit);
        Assert.False(CanCache(parameters));
    }

    /// <summary>Does not share one branch's minimum with a later independent limit in the same query.</summary>
    /// <param name="mariaDb">Whether the context uses the MariaDB engine profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentLimitsDoNotShareMinimumState(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var sql = context.GetService<ISqlExpressionFactory>();
        var first = CreateSelect(sql.Constant(1), limit: Least(sql,
            [new SqlParameterExpression("first", typeof(int), IntTypeMapping.Default), sql.Constant(4)],
            typeof(int), IntTypeMapping.Default));

        var second = CreateSelect(sql.Constant(2), limit: Least(sql,
            [new SqlParameterExpression("second", typeof(int), IntTypeMapping.Default), sql.Constant(20)],
            typeof(int), IntTypeMapping.Default));

        var union = new UnionExpression("u", first, second, distinct: true);
        var parameters = new ParametersCacheDecorator(
            new Dictionary<string, object?> { ["first"] = 3, ["second"] = 10 });

        var visitor = new MySqlLimitExpressionEvaluatingExpressionVisitor(sql, parameters);

        // Act
        var actual = (UnionExpression)visitor.Visit(union);

        // Assert
        Assert.Equal(3, Assert.IsType<SqlConstantExpression>(actual.Source1.Limit).Value);
        Assert.Equal(10, Assert.IsType<SqlConstantExpression>(actual.Source2.Limit).Value);
        Assert.IsType<SqlFunctionExpression>(first.Limit);
        Assert.IsType<SqlFunctionExpression>(second.Limit);
        Assert.False(CanCache(parameters));
    }

    /// <summary>Leaves an ordinary parameter limit and its cache permission untouched.</summary>
    /// <param name="mariaDb">Whether the context uses the MariaDB engine profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryParameterLimitIsPreserved(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var sql = context.GetService<ISqlExpressionFactory>();
        var limit = new SqlParameterExpression("take", typeof(int), IntTypeMapping.Default);
        var select = CreateSelect(sql.Constant(1), limit: limit);
        var parameters = new ParametersCacheDecorator(new Dictionary<string, object?>());
        var visitor = new MySqlLimitExpressionEvaluatingExpressionVisitor(sql, parameters);

        // Act
        var actual = visitor.Visit(select);

        // Assert
        Assert.Same(select, actual);
        Assert.Same(limit, ((SelectExpression)actual).Limit);
        Assert.True(CanCache(parameters));
    }

    /// <summary>Does not evaluate ordinary LEAST projections or disable their cache permission.</summary>
    /// <param name="mariaDb">Whether the context uses the MariaDB engine profile.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryLeastProjectionIsPreserved(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var sql = context.GetService<ISqlExpressionFactory>();
        var projection = Least(sql,
            [sql.Constant(7), new SqlParameterExpression("missing", typeof(int), IntTypeMapping.Default)],
            typeof(int), IntTypeMapping.Default);

        var select = CreateSelect(projection);
        var parameters = new ParametersCacheDecorator(new Dictionary<string, object?>());
        var visitor = new MySqlLimitExpressionEvaluatingExpressionVisitor(sql, parameters);

        // Act
        var actual = visitor.Visit(select);

        // Assert
        Assert.Same(select, actual);
        Assert.Same(projection, ((SelectExpression)actual).Projection[0].Expression);
        Assert.Null(((SelectExpression)actual).Limit);
        Assert.True(CanCache(parameters));
    }

    /// <summary>Supplies scalar-path shapes and exact path SQL for each engine family.</summary>
    public static IEnumerable<object[]> JsonScalarCases()
    {
        foreach (var mariaDb in new[] { false, true })
        {
            yield return [mariaDb, "empty", "'$'"];
            yield return [mariaDb, "property", "'$.name'"];
            yield return [mariaDb, "index", "'$[2]'"];
            yield return [mariaDb, "mixed", "'$.outer[2].name'"];
            yield return [mariaDb, "quoted", "'$.outer.\"quoted.name\".\"a''b\"'"];
            yield return [mariaDb, "escaped", "_utf8mb4 X'242E22615C22625C5C6322'"];
            yield return [mariaDb, "dynamic-first", "CONCAT('$[', CAST(@index AS CHAR), '].value')"];
            yield return [mariaDb, "dynamic-last", "CONCAT('$.items[', CAST(@index AS CHAR), ']')"];
            yield return [mariaDb, "dynamic-mixed",
                "CONCAT('$.outer[2].items[', CAST(@index AS CHAR), '].value[', CAST(@second AS CHAR), ']')"];
        }
    }

    /// <summary>Supplies row-source cases without conflating scalar indices and implicit array wildcards.</summary>
    public static IEnumerable<object[]> JsonTableCases()
    {
        foreach (var mariaDb in new[] { false, true })
        {
            yield return [mariaDb, "null", "'[]', '$[*]'"];
            yield return [mariaDb, "empty", "'[]', '$[*]'"];
            yield return [mariaDb, "row-property", "'[]', '$.items[*]'"];
            yield return [mariaDb, "row-index", "'[]', '$.items[2]'"];
            yield return [mariaDb, "row-quoted", "'[]', '$.outer.\"quoted.name\"[*]'"];
            yield return [mariaDb, "row-dynamic",
                "JSON_EXTRACT('[]', CONCAT('$.items[', CAST(@index AS CHAR), '].children')), '$[*]'"];
        }
    }

    /// <summary>Supplies meaningful parameter, nesting, clamping, and result-type cases.</summary>
    public static IEnumerable<object[]> LeastCases()
    {
        foreach (var mariaDb in new[] { false, true })
        {
            yield return [mariaDb, "two", 20];
            yield return [mariaDb, "nested", 7];
            yield return [mariaDb, "last-minimum", 1];
            yield return [mariaDb, "negative", 0];
            yield return [mariaDb, "long", 20];
        }
    }

    /// <summary>Supplies early and late rejection boundaries without a redundant numeric-type cross-product.</summary>
    public static IEnumerable<object[]> InvalidLeastCases()
    {
        foreach (var mariaDb in new[] { false, true })
        {
            yield return [mariaDb, "missing", 0];
            yield return [mariaDb, "missing", 2];
            yield return [mariaDb, "null", 1];
            yield return [mariaDb, "nonnumeric", 0];
            yield return [mariaDb, "nonnumeric", 2];
            yield return [mariaDb, "unresolved", 2];
            yield return [mariaDb, "empty", 0];
            yield return [mariaDb, "nonintegral", 0];
        }
    }

    private static IReadOnlyList<PathSegment> CreatePath(
        string scenario
    ) => scenario switch
    {
        "empty" => [],
        "property" => [new("name")],
        "index" => [new(new SqlConstantExpression(2, IntTypeMapping.Default))],
        "mixed" => [new("outer"), new(new SqlConstantExpression(2, IntTypeMapping.Default)), new("name")],
        "quoted" => [new("outer"), new("quoted.name"), new("a'b")],
        "escaped" => [new("a\"b\\c")],
        "dynamic-first" => [new(Parameter("index")), new("value")],
        "dynamic-last" => [new("items"), new(Parameter("index"))],
        "dynamic-mixed" => [new("outer"), new(new SqlConstantExpression(2, IntTypeMapping.Default)),
            new("items"), new(Parameter("index")), new("value"), new(Parameter("second"))],
        "row-property" => [new("items")],
        "row-index" => [new("items"), new(new SqlConstantExpression(2, IntTypeMapping.Default))],
        "row-quoted" => [new("outer"), new("quoted.name")],
        "row-dynamic" => [new("items"), new(Parameter("index")), new("children")],
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static SqlParameterExpression Parameter(
        string name
    ) => new(name, typeof(int), IntTypeMapping.Default);

    private static SqlFunctionExpression Least(
        ISqlExpressionFactory sql,
        IReadOnlyList<SqlExpression> arguments,
        Type clrType,
        RelationalTypeMapping mapping
    ) => (SqlFunctionExpression)sql.Function(
        "LEAST",
        arguments,
        nullable: false,
        argumentsPropagateNullability: arguments.Select(_ => false),
        clrType,
        mapping);

    /// <summary>Constructs the finalized EF SQL tree consumed by the complete provider generator and visitor.</summary>
    private static SelectExpression CreateSelect(
        SqlExpression projection,
        IReadOnlyList<TableExpressionBase>? tables = null,
        SqlExpression? limit = null
    )
    {
        // WHY: This is EF's real finalized query-tree boundary. A hand-written
        // SQL fragment would bypass precisely the path traversal being guarded.
#pragma warning disable EF1001
        return new SelectExpression(
            alias: null, tables ?? [], predicate: null, groupBy: [], having: null,
            [new ProjectionExpression(projection, "Value")], distinct: false, orderings: [],
            offset: null, limit, new SqlAliasManager());
#pragma warning restore EF1001
    }

    private static bool CanCache(
        ParametersCacheDecorator parameters
    )
    {
        // WHY: The internal flag is the actual EF cache-safety contract, not
        // an inferred absence of parameter text in the generated SQL.
#pragma warning disable EF1001
        return parameters.CanCache;
#pragma warning restore EF1001
    }

    private static QueryContext CreateContext(
        bool mariaDb
    ) => new(new DbContextOptionsBuilder<QueryContext>()
        .UseMySql(
            "Server=localhost;Database=enumeration_query_unit;User ID=test;Password=unused;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options);

    /// <summary>Supplies the real provider service graph without executing a database command.</summary>
    private sealed class QueryContext(DbContextOptions<QueryContext> options) : DbContext(options);
}
