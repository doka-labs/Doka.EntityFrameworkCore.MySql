namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Reports each independently constrained function on every selected engine.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> CachedWindowTargets => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, string>(target.Data, "offset"),
            new TheoryDataRow<string, string>(target.Data, "buckets"),
            new TheoryDataRow<string, string>(target.Data, "position"),
        });

    /// <summary>A compiled valid command cannot bypass current parameter validation on its next invocation.</summary>
    /// <param name="target">The selected pinned engine.</param>
    /// <param name="function">The independently constrained argument.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CachedWindowTargets))]
    public async Task Cached_window_parameter_rejects_invalid_values_before_the_native_reader(
        string target,
        string function
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var commands = new WindowParameterCommands();
        await using var context = CreateWindowParameterContext(store, commands);
        var compiled = CompileWindowParameterQuery(function);
        var warmed = await ReadWindowParametersAsync(context, compiled, 1, safety.Token);
        var before = commands.Readers;
        var invalid = function == "offset"
            ? -1L
            : 0L;

        // Act
        var exception =
            await Record.ExceptionAsync(() => ReadWindowParametersAsync(context, compiled, invalid, safety.Token));

        // Assert
        Assert.Equal([1, 2, 3, 4, 5, 6], warmed.Select(row => row.Id));
        Assert.Equal(1, before);
        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(before, commands.Readers);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A rejected invocation neither poisons the compiled command nor retains a prior parameter value.</summary>
    /// <param name="target">The selected pinned engine.</param>
    /// <param name="function">The independently constrained argument.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(CachedWindowTargets))]
    public async Task Cached_window_parameter_recovers_with_current_valid_values_after_rejection(
        string target,
        string function
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var commands = new WindowParameterCommands();
        await using var context = CreateWindowParameterContext(store, commands);
        var compiled = CompileWindowParameterQuery(function);

        // WHY: Recovery requires an already compiled command and a rejected invocation;
        // the separate guard test independently verifies that rejection executes no reader.
        _ = await ReadWindowParametersAsync(context, compiled, 1, safety.Token);
        var invalid = function == "offset"
            ? -1L
            : 0L;

        var rejected =
            await Record.ExceptionAsync(() => ReadWindowParametersAsync(context, compiled, invalid, safety.Token));

        decimal?[] expected = function switch
        {
            "offset" => [null, null, 10m, null, null, null],
            "buckets" => [1m, 1m, 2m, 1m, 2m, 1m],
            "position" => [20m, 20m, 20m, 15m, 15m, null],
            _ => throw new ArgumentOutOfRangeException(nameof(function)),
        };

        // Act
        var rows = await ReadWindowParametersAsync(context, compiled, 2, safety.Token);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(rejected);
        Assert.Equal([1, 2, 3, 4, 5, 6], rows.Select(row => row.Id));
        Assert.Equal(expected, rows.Select(row => row.Value));
        Assert.Equal(2, commands.Readers);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>MySQL consumes an actual integral ROWS parameter; MariaDB rejects its unsupported grammar before execution.</summary>
    /// <param name="target">The selected pinned engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Dynamic_rows_frame_obeys_the_qualified_engine_parameter_contract(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var commands = new WindowParameterCommands();
        await using var context = CreateWindowParameterContext(store, commands);
        var width = 1m;
        var mariaDb = IntegrationTestEnvironment.GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target))
            .IsMariaDb;

        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => EF
                .Functions
                .Window()
                .PartitionBy(row.Group)
                .OrderBy(row.Id)
                .RowsBetween(MySqlWindowFrameBound.Preceding(width), MySqlWindowFrameBound.CurrentRow)
                .Sum(row.Amount));

        decimal?[]? values = null;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            values = await query.ToArrayAsync(safety.Token);
        });

        // Assert
        if (mariaDb)
        {
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("literal window-frame offsets", exception.Message, StringComparison.Ordinal);
            Assert.Null(values);
            Assert.Equal(0, commands.Readers);
        }
        else
        {
            Assert.Null(exception);
            Assert.Equal([10m, 30m, 50m, 5m, 20m, 0m], values);
            Assert.Equal(1, commands.Readers);
        }

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Numeric RANGE retains the decimal distance and never silently becomes a physical-row frame.</summary>
    /// <param name="target">The selected pinned engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Dynamic_numeric_range_frame_obeys_the_qualified_engine_parameter_contract(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        var commands = new WindowParameterCommands();
        await using var context = CreateWindowParameterContext(store, commands);
        var width = 15.5m;
        var mariaDb = IntegrationTestEnvironment.GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target))
            .IsMariaDb;

        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => EF
                .Functions
                .Window()
                .PartitionBy(row.Group)
                .OrderBy(row.Amount)
                .RangeBetween(MySqlWindowFrameBound.Preceding(width), MySqlWindowFrameBound.CurrentRow)
                .Sum(row.Amount));

        decimal?[]? values = null;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            values = await query.ToArrayAsync(safety.Token);
        });

        // Assert
        if (mariaDb)
        {
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("literal window-frame offsets", exception.Message, StringComparison.Ordinal);
            Assert.Null(values);
            Assert.Equal(0, commands.Readers);
        }
        else
        {
            Assert.Null(exception);
            Assert.Equal([10m, 30m, 50m, 5m, 20m, 0m], values);
            Assert.Equal(1, commands.Readers);
        }

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Preserves the fixture's actual service graph while attaching an invocation-local command observer.</summary>
    private static WindowContext CreateWindowParameterContext(
        WindowStore store,
        WindowParameterCommands commands
    )
    {
        using var source = store.CreateContext();
        var options =
            new DbContextOptionsBuilder<WindowContext>(
                    (DbContextOptions<WindowContext>)source.GetService<IDbContextOptions>())
                .AddInterceptors(commands)
                .Options;

        return new WindowContext(options);
    }

    /// <summary>Creates one delegate per fixture model, avoiding EF's unsupported reuse of a compiled query across models.</summary>
    private static Func<WindowContext, long, IAsyncEnumerable<WindowParameterRow>> CompileWindowParameterQuery(
        string function
    ) => function switch
    {
        "offset" => EF.CompileAsyncQuery((WindowContext context, long value) => context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new WindowParameterRow(row.Id,
                EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Lag(row.Amount, value)))),
        "buckets" => EF.CompileAsyncQuery((WindowContext context, long value) => context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new WindowParameterRow(row.Id,
                EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .Ntile(value)))),
        "position" => EF.CompileAsyncQuery((WindowContext context, long value) => context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new WindowParameterRow(row.Id,
                EF
                    .Functions
                    .Window()
                    .PartitionBy(row.Group)
                    .OrderBy(row.Id)
                    .RowsBetween(MySqlWindowFrameBound.UnboundedPreceding, MySqlWindowFrameBound.UnboundedFollowing)
                    .NthValue(row.Amount, value)))),
        _ => throw new ArgumentOutOfRangeException(nameof(function)),
    };

    /// <summary>Enumerates the same compiled delegate with current values and the caller's actual cancellation token.</summary>
    private static async Task<WindowParameterRow[]> ReadWindowParametersAsync(
        WindowContext context,
        Func<WindowContext, long, IAsyncEnumerable<WindowParameterRow>> query,
        long value,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<WindowParameterRow>();

        await foreach (var row in query(context, value)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            rows.Add(row);
        }

        return rows.ToArray();
    }

    private sealed record WindowParameterRow(
        int Id,
        decimal? Value
    );

    /// <summary>Counts actual provider reader commands, not query construction or cached delegate invocation.</summary>
    private sealed class WindowParameterCommands : DbCommandInterceptor
    {
        private int _readers;

        public int Readers => Volatile.Read(ref _readers);

        /// <inheritdoc />
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Interlocked.Increment(ref _readers);

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _readers);

            return ValueTask.FromResult(result);
        }
    }
}
