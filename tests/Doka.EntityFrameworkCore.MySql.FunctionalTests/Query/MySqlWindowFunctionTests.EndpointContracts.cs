namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlWindowFunctionTests
{
    /// <summary>The documented default endpoint denotes CURRENT ROW without requiring a marker property.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Default_frame_endpoints_retain_current_row_semantics(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(default, default)
            .Sum(row.Sequence));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("ROWS BETWEEN CURRENT ROW AND CURRENT ROW", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Explicit construction has the same documented semantics as the default endpoint value.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void New_frame_endpoints_retain_current_row_semantics(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(new MySqlWindowFrameBound(), new MySqlWindowFrameBound())
            .Count());

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("COUNT(*) OVER", sql, StringComparison.Ordinal);
        Assert.Contains("ROWS BETWEEN CURRENT ROW AND CURRENT ROW", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>An ending RANGE offset is validated even when the starting bound has no offset.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Range_with_only_an_ending_offset_keeps_its_numeric_distance(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Amount)
            .RangeBetween(MySqlWindowFrameBound.CurrentRow, MySqlWindowFrameBound.Following(0.75m))
            .Sum(row.Amount));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("RANGE BETWEEN CURRENT ROW AND 0.75 FOLLOWING", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Capturing a whole descriptor is not silently interpreted as a parameterized numeric offset.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Captured_whole_frame_endpoint_is_rejected_before_io(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var bound = MySqlWindowFrameBound.Preceding(1);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .RowsBetween(bound, MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Sequence));

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("typed markers inside the query expression",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
