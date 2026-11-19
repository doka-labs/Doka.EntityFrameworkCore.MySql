namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Limits raw temporal grammar tokens to the native TIMESTAMPDIFF unit position.</summary>
public sealed partial class MySqlIndependentCollectionMembershipSqlTests
{
    /// <summary>Supplies every native unit while retaining a lowercase case-insensitivity control.</summary>
    public static TheoryData<bool, string> TimestampDiffUnitCases
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var unit in new[]
                    {
                        "microsecond",
                        "SECOND",
                        "MINUTE",
                        "HOUR",
                        "DAY",
                        "WEEK",
                        "MONTH",
                        "QUARTER",
                        "YEAR",
                    })
                {
                    cases.Add(mariaDb, unit);
                }
            }

            return cases;
        }
    }

    /// <summary>Supplies malformed grammar and non-scalar children which cannot enter a COUNT predicate.</summary>
    public static TheoryData<bool, string> UnsafeTimestampDiffCases
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var scenario in new[]
                    {
                        "invalid-unit",
                        "constant-unit",
                        "wrong-position",
                        "wrong-function",
                        "schema-function",
                        "non-built-in",
                        "short-arity",
                        "long-arity",
                        "aggregate-argument",
                        "window-argument",
                    })
                {
                    cases.Add(mariaDb, scenario);
                }
            }

            return cases;
        }
    }

    /// <summary>Native temporal unit tokens preserve their complete calculation in correlated membership.</summary>
    /// <param name="mariaDb">Whether the MariaDB SQL profile is used.</param>
    /// <param name="unit">The native unit token, whose spelling must be preserved.</param>
    [Theory]
    [MemberData(nameof(TimestampDiffUnitCases))]
    public void Native_timestamp_difference_unit_is_eligible_for_scalar_count(
        bool mariaDb,
        string unit
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = CreateTimestampDiffMembership(context, unit, "valid");

        // Act
        var actual = Generate(context, query);

        // Assert
        Assert.Contains("SELECT COUNT(*)", actual, StringComparison.Ordinal);
        Assert.Contains($"WHERE (tImEsTaMpDiFf({unit}, ", actual, StringComparison.Ordinal);
        Assert.Contains("FROM_UNIXTIME(`j`.`value`))) <=> (`e`.`Index`)", actual, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN (", actual, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Unrecognized fragments and unsafe temporal arguments remain in the original SELECT scope.</summary>
    /// <param name="mariaDb">Whether the MariaDB SQL profile is used.</param>
    /// <param name="scenario">The exact grammar or scalar-safety violation.</param>
    [Theory]
    [MemberData(nameof(UnsafeTimestampDiffCases))]
    public void Unsafe_timestamp_difference_shape_does_not_enter_count_predicate(
        bool mariaDb,
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = CreateTimestampDiffMembership(context, "SECOND", scenario);
        var inner = Assert.IsType<InExpression>(query.Projection[0].Expression)
            .Subquery!;

        var projection = inner.Projection[0].Expression;

        // Act
        var actual = Generate(context, query);

        // Assert
        Assert.Contains(" IN (", actual, StringComparison.Ordinal);
        Assert.Same(projection, inner.Projection[0].Expression);
        Assert.DoesNotContain("SELECT COUNT(*)", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE ", actual, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Builds the temporal grammar directly so invalid metadata never reaches a database.</summary>
    private static SelectExpression CreateTimestampDiffMembership(
        MembershipContext context,
        string unit,
        string scenario
    )
    {
        var sql = context.GetService<ISqlExpressionFactory>();
        var dateMapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(DateTime))!;

        var value = new ColumnExpression("value", "j", typeof(long), LongTypeMapping.Default, nullable: true);
        var document = new ColumnExpression("Document",
            "e",
            typeof(string),
            MySqlStringTypeMapping.Default,
            nullable: false);

        var outer = new ColumnExpression("Index", "e", typeof(int), IntTypeMapping.Default, nullable: false);
        var date = sql.Constant(new DateTime(1970, 1, 1), dateMapping);

        var end = sql.Function("FROM_UNIXTIME", [value], true, [true], typeof(DateTime), dateMapping);

        if (scenario == "aggregate-argument")
        {
            end = sql.Function("MAX", [end], true, [true], typeof(DateTime), dateMapping);
        }
        else if (scenario == "window-argument")
        {
            end = new MySqlWindowExpression(MySqlWindowFunction.FirstValue,
                [end],
                [],
                [new OrderingExpression(value, ascending: true)],
                null,
                typeof(DateTime),
                dateMapping,
                nullable: true);
        }

        SqlExpression[] arguments = [new SqlFragmentExpression(unit), date, end];

        if (scenario == "invalid-unit")
        {
            arguments[0] = new SqlFragmentExpression("MILLISECOND");
        }
        else if (scenario == "constant-unit")
        {
            arguments[0] = sql.Constant(unit);
        }
        else if (scenario == "wrong-position")
        {
            arguments[1] = new SqlFragmentExpression(unit);
        }
        else if (scenario == "short-arity")
        {
            arguments = [arguments[0], date];
        }
        else if (scenario == "long-arity")
        {
            arguments = [arguments[0], date, end, date];
        }

        var propagation = arguments
            .Select(static argument => argument is not SqlFragmentExpression)
            .ToArray();

        // WHY: The schema overload deliberately constructs non-built-in metadata even
        // when its schema is null, isolating IsBuiltIn from function spelling and arity.
        var projection = scenario switch
        {
            "schema-function" => sql.Function("app",
                "TIMESTAMPDIFF",
                arguments,
                true,
                propagation,
                typeof(long),
                LongTypeMapping.Default),
            "non-built-in" => sql.Function(null!,
                "TIMESTAMPDIFF",
                arguments,
                true,
                propagation,
                typeof(long),
                LongTypeMapping.Default),
            _ => sql.Function(scenario == "wrong-function"
                    ? "OTHER_FUNCTION"
                    : "tImEsTaMpDiFf",
                arguments,
                true,
                propagation,
                typeof(long),
                LongTypeMapping.Default),
        };

        var jsonTable = new MySqlJsonTableExpression("j",
            document,
            null,
            [
                new MySqlJsonTableExpression.ColumnInfo("value", LongTypeMapping.Default, []),
                new MySqlJsonTableExpression.ColumnInfo("ordinal", IntTypeMapping.Default, ForOrdinality: true),
            ]);

        var inner = CreateSelect(projection, [jsonTable]);
        var table = context
            .Model
            .GetRelationalModel()
            .Tables
            .Single(table => table.Name == "MembershipRows");

        return CreateSelect(sql.In(outer, inner), [new TableExpression("e", table)]);
    }
}
