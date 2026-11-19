namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Cancels a server-observed window command and proves cleanup before the next pooled lease.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Active_window_reader_cancellation_remains_visible_and_allows_pool_recovery(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var observer = store.CreateObserverConnection();
        await observer.OpenAsync(safety.Token);
        var commands = new WindowCancellationCommands();
        var factory = new PooledDbContextFactory<WindowContext>(store.CreateOptions(commands, true), poolSize: 1);
        var marker = "doka-window-cancel-"
            + Guid
                .NewGuid()
                .ToString("N", CultureInfo.InvariantCulture);

        WindowContext first;
        Exception? observed = null;
        string observedSql;
        bool completed;
        await using (first = await factory.CreateDbContextAsync(safety.Token))
        {
            await first.Database.OpenConnectionAsync(safety.Token);

            // WHY: This only bounds failed cancellation. Native state and the exact marker
            // establish readiness; elapsed time or a pending Task is never the success oracle.
            first.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
            var worker = (MySqlConnection)first.Database.GetDbConnection();
            var query = first
                .Rows
                .Where(row => row.Id == 1)
                .TagWith(marker)
                .Select(row => new
                {
                    Position = EF
                        .Functions
                        .Window()
                        .OrderBy(row.Id)
                        .RowNumber(),
                    Wait = WindowContext.NativeSleep(600),
                });

            Exception? primary = null;

            // Act
            var pending = query.ToArrayAsync(cancellation.Token);

            try
            {
                observedSql =
                    await ObserveWindowNativeSleepAsync(observer, worker.ServerThread, marker, pending, safety.Token);

                await cancellation.CancelAsync();
                observed = await Record.ExceptionAsync(() => pending);
                primary = observed;
            }
            catch (Exception failure)
            {
                primary = failure;
                throw;
            }
            finally
            {
                // WHY: Drain the actual connector operation before releasing the context,
                // retaining its original failure if cancellation or cleanup also fails.
                await DatabaseLockProbeCleanup.CompleteLockProbeAsync(cancellation,
                    static () => Task.CompletedTask,
                    pending,
                    primary,
                    null);
            }

            completed = pending.IsCompleted;
            await first.Database.CloseConnectionAsync();
        }

        await using var context = await factory.CreateDbContextAsync(safety.Token);
        var rows = await context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Contains(marker, observedSql, StringComparison.Ordinal);
        Assert.Contains("SLEEP(600)", observedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(cancellation.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(observed)
                .CancellationToken);

        Assert.Equal(cancellation.Token, commands.FirstToken);
        Assert.True(completed);
        Assert.Same(first, context);
        Assert.Equal([1, 2, 3, 4, 5, 6], rows);
        Assert.Equal(2, commands.Readers);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        _output.WriteLine($"Target: {target}; observed native window command: {observedSql}");
    }

    /// <summary>Matches the worker, unique statement marker and active native sleep before requesting cancellation.</summary>
    private static async Task<string> ObserveWindowNativeSleepAsync(
        MySqlConnection observer,
        int worker,
        string marker,
        Task pending,
        CancellationToken cancellationToken
    )
    {
        // WHY: Preserve arbitrary native command bytes while inspecting the ASCII statement marker and sleep.
        await using var command = new MySqlCommand(
            $"""
            SELECT CONVERT(CAST(INFO AS BINARY) USING latin1)
            FROM {MySqlNativeProcessMetadata.GetProcessListSource(observer)}
            WHERE ID=@worker AND STATE='User sleep' AND COMMAND='Query';
            """,
            observer);

        command.Parameters.AddWithValue("@worker", worker);

        while (!pending.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sql = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);

            if (sql is not null
                && sql.Contains(marker, StringComparison.Ordinal)
                && sql.Contains("SLEEP(600)", StringComparison.OrdinalIgnoreCase))
            {
                return sql;
            }

            // WHY: Polling yields to other work; no polling count or elapsed threshold qualifies the query.
            await Task.Delay(25, cancellationToken);
        }

        await pending;

        throw new XunitException("The window command completed before its exact native sleep was observed.");
    }

    /// <summary>Records the actual first reader token and detects unexpected retries across cancellation and recovery.</summary>
    private sealed class WindowCancellationCommands : DbCommandInterceptor
    {
        internal int Readers { get; private set; }
        internal CancellationToken FirstToken { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (++Readers == 1)
            {
                FirstToken = cancellationToken;
            }

            return ValueTask.FromResult(result);
        }
    }
}
