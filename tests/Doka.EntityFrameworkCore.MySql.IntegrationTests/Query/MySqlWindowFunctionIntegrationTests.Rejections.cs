namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Supplies unsupported scalar and scope requests without synthesizing unsupported public APIs.</summary>
    public static TheoryData<string, string> RejectedWindowTargets
    {
        get
        {
            var cases = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, "date-range");
                cases.Add(target, "mapped-default");
                cases.Add(target, "having");
                cases.Add(target, "client-argument");
                cases.Add(target, "member-filter-after-page");
            }

            return cases;
        }
    }

    /// <summary>Unsupported expressions fail at the provider translation boundary without opening a native reader.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(RejectedWindowTargets))]
    public async Task Unsupported_window_requests_fail_before_the_native_reader(
        string target,
        string scenario
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var probe = new WindowParameterCommands();
        await using var context = new WindowContext(store.CreateOptions(probe, false));
        IQueryable query = scenario switch
        {
            "date-range" => context.Rows.Select(row => EF
                .Functions
                .Window()
                .OrderBy(row.Timestamp)
                .RangeBetween(MySqlWindowFrameBound.Preceding(1), MySqlWindowFrameBound.CurrentRow)
                .Sum(row.Amount)),
            "mapped-default" => context.Rows.Select(row => EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .Lag(row.CharGuid, 1, row.BinaryGuid)),
            "having" => context
                .Rows
                .GroupBy(row => row.Group)
                .Where(group => EF
                        .Functions
                        .Window()
                        .OrderBy(group.Key)
                        .RowNumber()
                    > 1)
                .Select(group => group.Count()),
            "client-argument" => context.Rows.Select(row => EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .Lag<string>(ClientWindowValue(row.Name))),
            "member-filter-after-page" => context
                .Rows
                .GroupBy(row => row.Group)
                .Select(group => group
                    .OrderBy(row => row.Id)
                    .Skip(1)
                    .Where(row => row.Amount > 0)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList()),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        // Act
        var exception = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, probe.Readers);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        var expected = scenario == "member-filter-after-page"
            ? "separate child ranking scope"
            : "window";

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Provides an intentionally nontranslatable application method inside a window argument.</summary>
    private static string? ClientWindowValue(
        string? value
    ) => value?.ToUpperInvariant();

    /// <summary>A scalar subquery from another context cannot be smuggled into a window argument.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Foreign_context_window_argument_is_rejected_before_either_reader(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var probe = new WindowParameterCommands();
        await using var context = new WindowContext(store.CreateOptions(probe, false));
        await using var other = new WindowContext(store.CreateOptions(probe, false));
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .OrderBy(row.Id)
            .Sum(
                other
                    .Rows
                    .Where(inner => inner.Id == row.Id)
                    .Select(inner => inner.Id)
                    .First()));

        // Act
        var error = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("context", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.Readers);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        Assert.Equal(ConnectionState.Closed,
            other.Database.GetDbConnection()
                .State);
    }
}
