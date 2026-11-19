namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-P26/N22 cancel an actually running native CTE and prove command drainage before healthy reuse.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BindingCases))]
    public async Task Cancellation_of_a_server_observed_cte_drains_work_and_keeps_subsequent_pool_access_healthy(
        string target,
        bool recursive
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands, pooled: false);
        await context.Database.OpenConnectionAsync(safety.Token);

        // WHY: This timeout only bounds failed cancellation. Native PROCESSLIST state,
        // never a delay or a pending Task, establishes that the command reached its wait.
        context.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var marker = "doka-cte-cancellation-"
            + Guid
                .NewGuid()
                .ToString("N", CultureInfo.InvariantCulture);

        var source = context
            .Vertices
            .Where(row => row.Id == 1)
            .Select(row => row.Id);

        var recursiveQuery = source
            .WithRecursiveCte(
                frontier => frontier.Select(id => id + GraphContext.NativeSleep(600)),
                rows => rows,
                1)
            .TagWith(marker);

        // WHY: The native control's single-PK MySQL read returned SLEEP=1 successfully
        // after KILL QUERY, with both raw Connector and EF cancellation. The measured
        // two-row range instead observes interruption while advancing the reader.
        var ordinaryQuery = context
            .Vertices
            .Where(row => row.Id <= 2)
            .Select(row => new
            {
                row.Id,
                Wait = GraphContext.NativeSleep(600),
            })
            .WithCte(rows => rows)
            .TagWith(marker);

        Exception? primary = null;
        Exception? observed = null;

        // Act
        Task pending = recursive
            ? recursiveQuery.ToArrayAsync(cancellation.Token)
            : ordinaryQuery.ToArrayAsync(cancellation.Token);

        try
        {
            await ObserveNativeSleepAsync(store.Observer, worker, marker, pending, safety.Token);
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
            // WHY: The shared probe cleanup also drains non-locking native work. There is
            // no held lock here, but cancellation/drain failures must preserve the primary error.
            await DatabaseLockProbeCleanup.CompleteLockProbeAsync(cancellation,
                static () => Task.CompletedTask,
                pending,
                primary,
                null);
        }

        var healthy = await context.Vertices.CountAsync(safety.Token);
        await context.Database.CloseConnectionAsync();
        await using var pooled = store.CreateContext(commands);
        var pooledValues = await pooled
            .Vertices
            .Where(row => row.Id == 1)
            .Select(row => row.Id)
            .WithCte(rows => rows)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(cancellation.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(observed)
                .CancellationToken);

        Assert.True(pending.IsCompleted);
        Assert.Equal(5, healthy);
        Assert.Equal([1], pooledValues);
        var reads = commands.Reads.ToArray();
        Assert.Equal(3, reads.Length);
        Assert.Equal(cancellation.Token, reads[0].Token);
        Assert.DoesNotContain("SET SESSION", reads[0].Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionState.Closed,
            pooled.Database.GetDbConnection()
                .State);
    }

    /// <summary>CTE-P26 verifies early stream disposal and reuse of the same owned physical pool session.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BindingCases))]
    public async Task Early_enumerator_disposal_returns_a_clean_reader_and_reusable_physical_session(
        string target,
        bool recursive
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var first = store.CreateContext(commands);
        await first.Database.OpenConnectionAsync(safety.Token);
        var originalThread = ((MySqlConnection)first.Database.GetDbConnection()).ServerThread;
        var source = first.Facets.Select(row => row.Id);
        var query = recursive
            ? source.WithRecursiveCte(frontier => frontier, rows => rows.OrderBy(id => id), 2)
            : source.WithCte(rows => rows
                .Concat(rows)
                .OrderBy(id => id));

        bool moved;
        int firstValue;

        // Act
        await using (var iterator = query
            .AsAsyncEnumerable()
            .GetAsyncEnumerator(safety.Token))
        {
            moved = await iterator.MoveNextAsync();
            firstValue = iterator.Current;
        }

        var retainedCallerOpening = first.Database.GetDbConnection()
            .State;

        await first.Database.CloseConnectionAsync();
        await using var second = store.CreateContext(commands);
        await second.Database.OpenConnectionAsync(safety.Token);
        var reusedThread = ((MySqlConnection)second.Database.GetDbConnection()).ServerThread;
        var count = await second.Vertices.CountAsync(safety.Token);
        await second.Database.CloseConnectionAsync();

        // Assert
        Assert.True(moved);
        Assert.Equal(1, firstValue);
        Assert.Equal(ConnectionState.Open, retainedCallerOpening);
        Assert.Equal(originalThread, reusedThread);
        Assert.Equal(5, count);
        var reads = commands.Reads.ToArray();
        Assert.Equal(2, reads.Length);
        Assert.DoesNotContain("WITH", reads[1].Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionState.Closed,
            second.Database.GetDbConnection()
                .State);
    }

    /// <summary>Observes the exact fresh worker's native user-sleep state instead of assuming elapsed time proves execution.</summary>
    private static async Task ObserveNativeSleepAsync(
        MySqlConnection observer,
        MySqlConnection worker,
        string marker,
        Task pending,
        CancellationToken cancellationToken
    )
    {
        // WHY: Preserve arbitrary native command bytes while matching only ASCII readiness markers.
        await using var command = new MySqlCommand(
            $"""
            SELECT COUNT(*) FROM {MySqlNativeProcessMetadata.GetProcessListSource(observer)}
            WHERE ID=@worker AND STATE='User sleep'
                AND LOCATE(@marker, CONVERT(CAST(INFO AS BINARY) USING latin1))>0
                AND LOCATE('600', CONVERT(CAST(INFO AS BINARY) USING latin1))>0;
            """,
            observer);

        command.Parameters.AddWithValue("@worker", worker.ServerThread);
        command.Parameters.AddWithValue("@marker", marker);

        while (!pending.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1)
            {
                return;
            }

            // WHY: Polling yields without measuring a performance threshold; only native state is the readiness oracle.
            await Task.Delay(25, cancellationToken);
        }

        await pending;

        throw new XunitException("The CTE command completed before its native sleep state was observed.");
    }
}
