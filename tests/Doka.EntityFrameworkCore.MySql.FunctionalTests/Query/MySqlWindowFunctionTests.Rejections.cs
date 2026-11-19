namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlWindowFunctionTests
{
    /// <summary>Supplies distinct unsupported semantics rather than exercising several acts in one test.</summary>
    public static TheoryData<bool, string> RejectedCases
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var scenario in new[]
                    {
                        "unordered-number",
                        "unordered-lag",
                        "unordered-first",
                        "thenby-first",
                        "negative-lag",
                        "zero-ntile",
                        "zero-nth",
                        "fractional-rows",
                        "reversed-frame",
                        "impossible-start",
                        "impossible-end",
                        "text-range",
                        "frame-twice",
                        "nested-window",
                        "escaped-definition",
                        "frame-number",
                        "frame-rank",
                        "frame-dense",
                        "frame-ntile",
                        "frame-percent",
                        "frame-cume",
                        "frame-lag",
                        "frame-lead",
                    })
                {
                    cases.Add(mariaDb, scenario);
                }
            }

            return cases;
        }
    }

    /// <summary>Invalid ordering, endpoint, nesting and ignored-frame requests fail before database I/O.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    /// <param name="scenario">The independently rejected semantic request.</param>
    [Theory]
    [MemberData(nameof(RejectedCases))]
    public void Invalid_window_semantics_are_rejected_before_io(
        bool mariaDb,
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(RejectedSelector(scenario));

        // Act
        var exception = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.NotNull(exception);
        Assert.True(exception is InvalidOperationException or ArgumentOutOfRangeException);
        Assert.Contains("window", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Offline binding also rejects a fractional ROWS parameter rather than truncating it for diagnostics.</summary>
    [Fact]
    public void Fractional_rows_parameter_is_rejected_by_the_actual_parameter_converter()
    {
        // Arrange
        using var context = CreateContext(mariaDb: false);
        var width = 1.5m;
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.Preceding(width), MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Sequence));

        // Act
        var exception = Record.Exception(() => query.CreateDbCommand());

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>String- or GUID-valued defaults retain the first argument's mapped representation.</summary>
    /// <param name="mariaDb">Whether to use MariaDB.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Guid_default_parameter_uses_the_char36_argument_mapping(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var fallback = Guid.Parse("ba2f92a4-f60d-402b-875b-889008704743");
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .Lag<Guid>(row.GuidValue, 1, fallback));

        // Act
        using var command = query.CreateDbCommand();

        // Assert
        var parameter = Assert.Single(command.Parameters.Cast<DbParameter>());
        Assert.Equal(fallback.ToString("D"), parameter.Value);
        Assert.NotEqual(DbType.Binary, parameter.DbType);
    }

    /// <summary>A captured NULL marker is rejected during translation instead of being treated as EF.Functions.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Captured_null_function_receiver_is_rejected_before_io(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        DbFunctions? functions = null;
        var query = context.Rows.Select(row => functions!
            .Window()
            .OrderBy(row.Id)
            .RowNumber());

        // Act
        var exception = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("must begin at EF.Functions.Window", exception.Message, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    private static Expression<Func<WindowRow, object?>> RejectedSelector(
        string scenario
    ) => scenario switch
    {
        "unordered-number" => row => EF
            .Functions
            .Window()
            .RowNumber(),
        "unordered-lag" => row => EF
            .Functions
            .Window()
            .Lag(row.NullableValue),
        "unordered-first" => row => EF
            .Functions
            .Window()
            .FirstValue(row.NullableValue),
        "thenby-first" => row => EF
            .Functions
            .Window()
            .ThenBy(row.Id)
            .RowNumber(),
        "negative-lag" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .Lag(row.NullableValue, -1),
        "zero-ntile" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .Ntile(0),
        "zero-nth" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .NthValue(row.NullableValue, 0),
        "fractional-rows" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.Preceding(0.5m), MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Sequence),
        "reversed-frame" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.Preceding(1), MySqlWindowFrameBound.Preceding(2))
            .Sum(row.Sequence),
        "impossible-start" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.UnboundedFollowing, MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Sequence),
        "impossible-end" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.UnboundedPreceding)
            .Sum(row.Sequence),
        "text-range" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Name)
            .RangeBetween(MySqlWindowFrameBound.Preceding(1), MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Sequence),
        "frame-twice" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .RangeBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Sequence),
        "nested-window" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .Sum(EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RowNumber()),
        "escaped-definition" => row => ((MySqlWindowDefinition)null!)
            .OrderBy(row.Id)
            .RowNumber(),
        "frame-number" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .RowNumber(),
        "frame-rank" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .Rank(),
        "frame-dense" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .DenseRank(),
        "frame-ntile" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .Ntile(2),
        "frame-percent" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .PercentRank(),
        "frame-cume" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .CumeDist(),
        "frame-lag" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .Lag(row.NullableValue),
        "frame-lead" => row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .Lead(row.NullableValue),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };
}
