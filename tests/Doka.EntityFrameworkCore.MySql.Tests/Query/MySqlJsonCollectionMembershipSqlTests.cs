namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies scalar JSON membership emission independently of LINQ optimizations.</summary>
public sealed class MySqlJsonCollectionMembershipSqlTests
{
    private static readonly bool[] s_booleanCases = [false, true];
    private static readonly string[] s_predicateShapes = ["mixed-or", "and", "nullable-or", "in"];

    /// <summary>Reports each predicate shape, negation and engine independently.</summary>
    public static IEnumerable<TheoryDataRow<bool, bool, string>> CountPredicateCases => s_booleanCases
        .SelectMany(mariaDb => s_booleanCases
            .SelectMany(negated => s_predicateShapes
                .Select(shape => new TheoryDataRow<bool, bool, string>(mariaDb, negated, shape))));

    /// <summary>Parenthesizes the complete predicate before attaching membership with AND.</summary>
    /// <param name="mariaDb">Whether to generate for MariaDB.</param>
    /// <param name="negated">Whether the membership expression is negated.</param>
    /// <param name="shape">The existing predicate's operator shape.</param>
    [Theory]
    [MemberData(nameof(CountPredicateCases))]
    public void Count_membership_groups_complete_predicate(
        bool mariaDb,
        bool negated,
        string shape
    )
    {
        // Arrange
        using var context = new DbContext(new DbContextOptionsBuilder()
            .EnableServiceProviderCaching(false)
            .UseMySql(
                "Server=localhost;Database=unused;User ID=root;Password=unused;",
                mariaDb
                    ? MySqlServerVersion.MariaDb(new Version(12, 3, 0))
                    : MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options);

        var factory = context.GetService<ISqlExpressionFactory>();
        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(int))!;

        var rowset = new MySqlJsonTableExpression(
            "values_rows",
            new SqlConstantExpression("[1,2]", MySqlStringTypeMapping.Default),
            path: null,
            columnInfos:
            [
                new MySqlJsonTableExpression.ColumnInfo("value", mapping),
                new MySqlJsonTableExpression.ColumnInfo("ordinal", mapping, ForOrdinality: true),
            ]);

        var value = new ColumnExpression("value", "values_rows", typeof(int), mapping, nullable: true);
        var one = factory.Constant(1, mapping);
        var predicate = shape switch
        {
            "mixed-or" => factory.OrElse(factory.Equal(value, one), factory.GreaterThan(value, one)),
            "and" => factory.AndAlso(factory.GreaterThan(value, factory.Constant(0, mapping)),
                factory.LessThan(value, factory.Constant(3, mapping))),
            "nullable-or" => factory.OrElse(factory.IsNull(value),
                factory.GreaterThan(value, factory.Constant(0, mapping))),
            "in" => factory.In(value, [one, factory.Constant(2, mapping)]),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var subquery = CreateSelect([rowset], predicate, value);
        var membership = factory.In(factory.Constant(3, mapping), subquery);
        var expression = CreateSelect([],
            predicate: null,
            negated
                ? factory.Not(membership)
                : membership);

        // Act
        var sql = context
            .GetService<IQuerySqlGeneratorFactory>()
            .Create()
            .GetCommand(expression)
            .CommandText;

        // Assert
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE (", sql, StringComparison.Ordinal);
        Assert.Contains(") AND (`values_rows`.`value`) <=> (3)", sql, StringComparison.Ordinal);
        Assert.Contains(
            negated
                ? ") = 0"
                : ") > 0",
            sql,
            StringComparison.Ordinal);

        if (shape == "in")
        {
            // WHY: This finalized IN node is the folded-OR control without
            // depending on whether EF folds a particular LINQ operand shape.
            Assert.Contains(" IN (1, 2)", sql, StringComparison.Ordinal);
        }
    }

    /// <summary>Builds finalized SQL nodes so no optimizer can fold the disjunction away.</summary>
    private static SelectExpression CreateSelect(
        IReadOnlyList<TableExpressionBase> tables,
        SqlExpression? predicate,
        SqlExpression projection
    )
    {
        // WHY: The actual SQL tree exercises the generator boundary directly;
        // a SQL fragment would bypass the predicate visitor under regression.
#pragma warning disable EF1001
        return new SelectExpression(
            alias: null,
            tables,
            predicate,
            groupBy: [],
            having: null,
            [new ProjectionExpression(projection, "value")],
            distinct: false,
            orderings: [],
            offset: null,
            limit: null,
            new SqlAliasManager());
#pragma warning restore EF1001
    }
}
