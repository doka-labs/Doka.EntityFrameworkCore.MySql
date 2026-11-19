namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlWindowFunctionTests
{
    /// <summary>Supplies literal ROWS and numeric RANGE forms independently on both engines.</summary>
    public static TheoryData<bool, string, string> LiteralFrameCases
    {
        get
        {
            var cases = new TheoryData<bool, string, string>();

            foreach (var mariaDb in new[] { false, true })
            {
                cases.Add(mariaDb,
                    "running",
                    "ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW");

                cases.Add(mariaDb,
                    "sliding",
                    "ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING");

                cases.Add(mariaDb,
                    "peers",
                    "RANGE BETWEEN CURRENT ROW AND CURRENT ROW");

                cases.Add(mariaDb,
                    "numeric",
                    "RANGE BETWEEN 0.75 PRECEDING AND CURRENT ROW");

                cases.Add(mariaDb,
                    "empty-first",
                    "ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING");

                cases.Add(mariaDb,
                    "last",
                    "ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING");

                cases.Add(mariaDb,
                    "nth",
                    "ROWS BETWEEN 1 PRECEDING AND CURRENT ROW");
            }

            return cases;
        }
    }

    /// <summary>Typed literal endpoints produce exact native frames, including integer ROWS values.</summary>
    /// <param name="mariaDb">Whether to use MariaDB.</param>
    /// <param name="scenario">The independently qualified frame form.</param>
    /// <param name="expected">The native frame clause.</param>
    [Theory]
    [MemberData(nameof(LiteralFrameCases))]
    public void Literal_frames_preserve_the_selected_rows_or_range_semantics(
        bool mariaDb,
        string scenario,
        string expected
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(FrameSelector(scenario));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains(expected, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0 PRECEDING", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>MySQL ROWS offsets are bound as actual Int64 values after checked integral validation.</summary>
    [Fact]
    public void Mysql_parameterized_rows_binds_an_actual_integral_provider_value()
    {
        // Arrange
        using var context = CreateContext(mariaDb: false);
        var width = 1m;
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.Preceding(width), MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Sequence));

        // Act
        using var command = query.CreateDbCommand();

        // Assert
        Assert.Contains("ROWS BETWEEN @", command.CommandText, StringComparison.Ordinal);
        var parameter = Assert.Single(command.Parameters.Cast<DbParameter>());
        Assert.Equal(1L, Assert.IsType<long>(parameter.Value));
        Assert.Equal(DbType.Int64, parameter.DbType);
    }

    /// <summary>MySQL RANGE retains an actual decimal parameter rather than silently applying ROWS conversion.</summary>
    [Fact]
    public void Mysql_parameterized_range_retains_the_decimal_provider_value()
    {
        // Arrange
        using var context = CreateContext(mariaDb: false);
        var width = 0.75m;
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Amount)
            .RangeBetween(MySqlWindowFrameBound.Preceding(width), MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Amount));

        // Act
        using var command = query.CreateDbCommand();

        // Assert
        Assert.Contains("RANGE BETWEEN @", command.CommandText, StringComparison.Ordinal);
        var parameter = Assert.Single(command.Parameters.Cast<DbParameter>());
        Assert.Equal(0.75m, Assert.IsType<decimal>(parameter.Value));
        Assert.Equal(DbType.Decimal, parameter.DbType);
    }

    /// <summary>MariaDB frame grammar rejects parameters without specializing them into interpolated SQL literals.</summary>
    /// <param name="rows">Whether to request physical ROWS rather than numeric RANGE.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Maria_parameterized_frame_is_rejected_before_io(
        bool rows
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: true);
        var width = 1m;
        var query = rows
            ? context.Rows.Select(row => EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RowsBetween(MySqlWindowFrameBound.Preceding(width), MySqlWindowFrameBound.CurrentRow)
                .Sum(row.Sequence))
            : context.Rows.Select(row => EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RangeBetween(MySqlWindowFrameBound.Preceding(width), MySqlWindowFrameBound.CurrentRow)
                .Sum(row.Sequence));

        // Act
        var exception = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("literal window-frame offsets", exception.Message, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    private static Expression<Func<WindowRow, object?>> FrameSelector(
        string scenario
    ) => scenario switch
    {
        "running" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.UnboundedPreceding, MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Amount),
        "sliding" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.Preceding(1), MySqlWindowFrameBound.Following(1))
            .Average(row.Sequence),
        "peers" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Sequence)
            .RangeBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Amount),
        "numeric" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Amount)
            .RangeBetween(MySqlWindowFrameBound.Preceding(0.75m), MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Amount),
        "empty-first" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.Preceding(2), MySqlWindowFrameBound.Preceding(1))
            .FirstValue(row.NullableValue),
        "last" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.UnboundedFollowing)
            .LastValue(row.NullableValue),
        "nth" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .RowsBetween(MySqlWindowFrameBound.Preceding(1), MySqlWindowFrameBound.CurrentRow)
            .NthValue(row.NullableValue, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };
}
