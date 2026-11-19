namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Protects scalar membership rewrites without moving aggregate or window expressions into WHERE.</summary>
public sealed partial class MySqlIndependentCollectionMembershipSqlTests
{
    /// <summary>Supplies complete scalar projections on both engine profiles.</summary>
    public static TheoryData<bool, string, string, string> ScalarProjectionCases
    {
        get
        {
            var cases = new TheoryData<bool, string, string, string>();

            foreach (var mariaDb in new[] { false, true })
            {
                cases.Add(mariaDb, "absolute", "(ABS(`j`.`value`))", "(`e`.`Index`)");
                cases.Add(mariaDb, "binary", "(`j`.`value` + 1)", "(`e`.`Index`)");
                cases.Add(mariaDb, "case", "(CASE WHEN `j`.`value` < 0 THEN 1 ELSE `j`.`value` END)", "(`e`.`Index`)");
                cases.Add(mariaDb, "outer-function", "(ABS(`e`.`Index`))", "(`e`.`Index`)");
                cases.Add(mariaDb, "mapped-sum", "(`app`.`SUM`(`j`.`value`))", "(`e`.`Index`)");
                cases.Add(mariaDb, "logical-left", "(`j`.`value` > 0 OR `j`.`value` < -10)", "(`e`.`Index`)");
                cases.Add(mariaDb, "logical-right", "(ABS(`j`.`value`))", "(`e`.`Index` > 0 OR `e`.`Index` < -10)");
                cases.Add(mariaDb, "not-left", "(NOT (`j`.`value` > 0))", "(`e`.`Index`)");
                cases.Add(mariaDb, "not-right", "(ABS(`j`.`value`))", "(NOT (`e`.`Index` > 0))");
            }

            return cases;
        }
    }

    /// <summary>Supplies NULL predicates on either comparison side with expression, false and null candidates.</summary>
    public static TheoryData<bool, bool, bool, string, bool> NullPredicateCases
    {
        get
        {
            var cases = new TheoryData<bool, bool, bool, string, bool>();

            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var predicateOnLeft in new[] { false, true })
                {
                    foreach (var notNull in new[] { false, true })
                    {
                        foreach (var candidate in new[] { "comparison", "false", "null" })
                        {
                            foreach (var negated in new[] { false, true })
                            {
                                cases.Add(mariaDb, predicateOnLeft, notNull, candidate, negated);
                            }
                        }
                    }
                }
            }

            return cases;
        }
    }

    /// <summary>Supplies direct and nested non-scalar expressions on both engine profiles.</summary>
    public static TheoryData<bool, string, string> NonScalarProjectionCases
    {
        get
        {
            var cases = new TheoryData<bool, string, string>();

            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var nested in new[] { false, true })
                {
                    var prefix = nested
                        ? "nested-"
                        : string.Empty;

                    cases.Add(mariaDb, prefix + "window", "ROW_NUMBER()");
                    cases.Add(mariaDb, prefix + "row-number", "ROW_NUMBER()");
                    cases.Add(mariaDb, prefix + "sum", "sUm(`j`.`value`)");
                    cases.Add(mariaDb, prefix + "count", "COUNT(`j`.`value`)");
                }
            }

            return cases;
        }
    }

    /// <summary>NULL tests remain complete operands rather than binding to the null-safe comparison result.</summary>
    /// <param name="mariaDb">Whether the MariaDB SQL profile is used.</param>
    /// <param name="predicateOnLeft">Whether the NULL test is the projected expression rather than the candidate.</param>
    /// <param name="notNull">Whether the operand tests IS NOT NULL rather than IS NULL.</param>
    /// <param name="candidate">The opposite operand: another predicate, false or SQL NULL.</param>
    /// <param name="negated">Whether the original membership is negated.</param>
    [Theory]
    [MemberData(nameof(NullPredicateCases))]
    public void Null_predicate_operands_remain_grouped_for_each_membership_direction(
        bool mariaDb,
        bool predicateOnLeft,
        bool notNull,
        string candidate,
        bool negated
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var sql = context.GetService<ISqlExpressionFactory>();
        var booleanMapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(bool))!;

        var value = new ColumnExpression("value", "j", typeof(int), IntTypeMapping.Default, nullable: true);
        var outer = new ColumnExpression("Index", "e", typeof(int), IntTypeMapping.Default, nullable: false);
        var predicateColumn = predicateOnLeft
            ? value
            : outer;

        var otherColumn = predicateOnLeft
            ? outer
            : value;

        var nullTest = notNull
            ? sql.IsNotNull(predicateColumn)
            : sql.IsNull(predicateColumn);

        // WHY: The typed constant preserves SQL NULL as a boolean operand; EF10's
        // factory overload requires a non-null value and cannot represent this control.
        var other = candidate == "comparison"
            ? (SqlExpression)sql.GreaterThan(otherColumn, sql.Constant(0, IntTypeMapping.Default))
            : new SqlConstantExpression(candidate == "false"
                    ? false
                    : null,
                typeof(bool),
                booleanMapping);

        var projection = predicateOnLeft
            ? nullTest
            : other;

        var item = predicateOnLeft
            ? other
            : nullTest;

        var jsonTable = new MySqlJsonTableExpression("j",
            new ColumnExpression("Document", "e", typeof(string), MySqlStringTypeMapping.Default, nullable: false),
            null,
            [
                new MySqlJsonTableExpression.ColumnInfo("value", IntTypeMapping.Default, []),
                new MySqlJsonTableExpression.ColumnInfo("ordinal", IntTypeMapping.Default, ForOrdinality: true),
            ]);

        var membership = sql.In(item, CreateSelect(projection, [jsonTable]));
        var table = context
            .Model
            .GetRelationalModel()
            .Tables
            .Single(table => table.Name == "MembershipRows");

        var query = CreateSelect(negated
                ? sql.Not(membership)
                : membership,
            [new TableExpression("e", table)]);

        var predicateColumnSql = predicateOnLeft
            ? "`j`.`value`"
            : "`e`.`Index`";

        var otherColumnSql = predicateOnLeft
            ? "`e`.`Index`"
            : "`j`.`value`";

        var expectedPredicate = $"({predicateColumnSql} IS {(notNull ? "NOT " : string.Empty)}NULL)";
        var expectedOther = candidate == "comparison"
            ? $"({otherColumnSql} > 0)"
            : $"({booleanMapping.GenerateSqlLiteral(candidate == "false" ? false : null)})";

        var expectedComparison = predicateOnLeft
            ? $"{expectedPredicate} <=> {expectedOther}"
            : $"{expectedOther} <=> {expectedPredicate}";

        // Act
        var actual = Generate(context, query);

        // Assert
        Assert.Contains("SELECT COUNT(*)", actual, StringComparison.Ordinal);

        // WHY: IS and <=> share left-associative precedence. Grouping only selected
        // operator kinds silently changes the result when a NULL test appears on either side.
        Assert.Contains($"WHERE {expectedComparison}", Regex.Replace(actual, @"\s+", " "), StringComparison.Ordinal);
        Assert.Contains(negated
                ? ") = 0"
                : ") > 0",
            actual,
            StringComparison.Ordinal);

        Assert.DoesNotContain(" IN (", actual, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Correlated membership retains every scalar operator and its outer-row references.</summary>
    /// <param name="mariaDb">Whether the MariaDB SQL profile is used.</param>
    /// <param name="scenario">The scalar expression shape.</param>
    /// <param name="expectedProjection">The complete SQL expression required inside the membership comparison.</param>
    /// <param name="expectedItem">The complete comparison operand, including required grouping.</param>
    [Theory]
    [MemberData(nameof(ScalarProjectionCases))]
    public void Correlated_scalar_projection_is_preserved_inside_count(
        bool mariaDb,
        string scenario,
        string expectedProjection,
        string expectedItem
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = CreateScalarProjectionMembership(context, scenario);

        // Act
        var actual = Generate(context, query);

        // Assert
        var normalized = Regex.Replace(actual, @"\s+", " ");

        Assert.Contains("SELECT COUNT(*)", actual, StringComparison.Ordinal);
        Assert.Contains($"WHERE {expectedProjection} <=> {expectedItem}", normalized, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(`e`.`Document`", actual, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", actual, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Aggregate and window expressions retain their original IN projection even below a scalar wrapper.</summary>
    /// <param name="mariaDb">Whether the MariaDB SQL profile is used.</param>
    /// <param name="scenario">The non-scalar expression shape and optional nesting.</param>
    /// <param name="expectedProjection">The original calculation which must remain in SELECT.</param>
    [Theory]
    [MemberData(nameof(NonScalarProjectionCases))]
    public void Non_scalar_projection_is_not_moved_into_count_predicate(
        bool mariaDb,
        string scenario,
        string expectedProjection
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = CreateScalarProjectionMembership(context, scenario);
        var inner = Assert.IsType<InExpression>(query.Projection[0].Expression)
            .Subquery!;

        var projection = inner.Projection[0].Expression;

        // Act
        var actual = Generate(context, query);

        // Assert
        Assert.Contains(" IN (", actual, StringComparison.Ordinal);
        Assert.Contains(expectedProjection, actual, StringComparison.Ordinal);
        Assert.Same(projection, inner.Projection[0].Expression);

        // WHY: COUNT(value) is a legitimate original projection; only the newly introduced
        // scalar COUNT(*) and WHERE comparison would move the calculation to an invalid SQL scope.
        Assert.DoesNotContain("SELECT COUNT(*)", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE ", actual, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Builds one correlated JSON rowset whose selected calculation is the eligibility discriminator.</summary>
    private static SelectExpression CreateScalarProjectionMembership(
        MembershipContext context,
        string scenario
    )
    {
        var sql = context.GetService<ISqlExpressionFactory>();
        var value = new ColumnExpression("value", "j", typeof(int), IntTypeMapping.Default, nullable: true);
        var ordinal = new ColumnExpression("ordinal", "j", typeof(int), IntTypeMapping.Default, nullable: false);
        var outer = new ColumnExpression("Index", "e", typeof(int), IntTypeMapping.Default, nullable: false);
        var document = new ColumnExpression("Document",
            "e",
            typeof(string),
            MySqlStringTypeMapping.Default,
            nullable: false);

        var nested = scenario.StartsWith("nested-", StringComparison.Ordinal);
        var kind = nested
            ? scenario["nested-".Length..]
            : scenario;

        var projection = kind switch
        {
            "absolute" => sql.Function("ABS", [value], true, [true], typeof(int), IntTypeMapping.Default),
            "binary" => sql.Add(value, sql.Constant(1, IntTypeMapping.Default)),
            "case" => sql.Case(
                [
                    new CaseWhenClause(sql.LessThan(value, sql.Constant(0, IntTypeMapping.Default)),
                        sql.Constant(1, IntTypeMapping.Default)),
                ],
                value),
            "outer-function" => sql.Function("ABS", [outer], true, [true], typeof(int), IntTypeMapping.Default),
            "logical-left" => sql.OrElse(sql.GreaterThan(value, sql.Constant(0)),
                sql.LessThan(value, sql.Constant(-10))),
            "not-left" => new SqlUnaryExpression(ExpressionType.Not,
                sql.GreaterThan(value, sql.Constant(0)),
                typeof(bool),
                context
                    .GetService<IRelationalTypeMappingSource>()
                    .FindMapping(typeof(bool))),
            "logical-right" or "not-right" => sql.Function("ABS",
                [value],
                true,
                [true],
                typeof(int),
                IntTypeMapping.Default),

            // WHY: EF marks schema-qualified mapped functions as non-built-in. A scalar
            // function named SUM must not inherit the unrelated native aggregate contract.
            "mapped-sum" => sql.Function("app",
                "SUM",
                [value],
                true,
                [true],
                typeof(int),
                IntTypeMapping.Default),
            "window" => new MySqlWindowExpression(MySqlWindowFunction.RowNumber,
                [],
                [],
                [new OrderingExpression(ordinal, ascending: true)],
                null,
                typeof(long),
                LongTypeMapping.Default,
                nullable: false),
            "row-number" => new RowNumberExpression([],
                [new OrderingExpression(ordinal, ascending: true)],
                LongTypeMapping.Default),
            "sum" or "count" => sql.Function(kind == "sum"
                    ? "sUm"
                    : "COUNT",
                [value],
                true,
                [true],
                typeof(int),
                IntTypeMapping.Default),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        if (nested)
        {
            // WHY: Eligibility must inspect children; a scalar ABS cannot legalize
            // an aggregate or window calculation after moving it into a WHERE clause.
            projection = sql.Function("ABS", [projection], true, [true], projection.Type, projection.TypeMapping);
        }

        var jsonTable = new MySqlJsonTableExpression("j",
            document,
            null,
            [
                new MySqlJsonTableExpression.ColumnInfo("value", IntTypeMapping.Default, []),
                new MySqlJsonTableExpression.ColumnInfo("ordinal", IntTypeMapping.Default, ForOrdinality: true),
            ]);

        var inner = CreateSelect(projection, [jsonTable]);
        var table = context
            .Model
            .GetRelationalModel()
            .Tables
            .Single(table => table.Name == "MembershipRows");

        var item = kind switch
        {
            "logical-right" => sql.OrElse(sql.GreaterThan(outer, sql.Constant(0)),
                sql.LessThan(outer, sql.Constant(-10))),
            "not-right" => new SqlUnaryExpression(ExpressionType.Not,
                sql.GreaterThan(outer, sql.Constant(0)),
                typeof(bool),
                context
                    .GetService<IRelationalTypeMappingSource>()
                    .FindMapping(typeof(bool))),
            _ => outer,
        };

        return CreateSelect(sql.In(item, inner), [new TableExpression("e", table)]);
    }
}
