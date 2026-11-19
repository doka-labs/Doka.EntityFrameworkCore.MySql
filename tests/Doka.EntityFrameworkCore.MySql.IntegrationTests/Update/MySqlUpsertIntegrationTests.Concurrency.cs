namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>A real server-observed blocked write receives the caller token and leaves a usable connection after cancellation.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Blocked_upsert_cancellation_drains_the_native_command_and_preserves_connection_ownership(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(safety.Token);
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", safety.Token);
        await using var holder = store.CreateContext();
        holder.Add(NewRow(1, "original"));
        await holder.SaveChangesAsync(safety.Token);
        await using var transaction = await holder.Database.BeginTransactionAsync(safety.Token);
        _ = await holder
            .Rows
            .Where(row => row.Id == 1)
            .ForUpdate()
            .SingleAsync(safety.Token);

        var capture = new Writes();
        await using var waiting = store.CreateContext(capture);
        await waiting.Database.OpenConnectionAsync(safety.Token);
        var worker = (MySqlConnection)waiting.Database.GetDbConnection();
        Exception? primary = null;
        Exception? canceled = null;

        // Act
        var pending = waiting.ExecuteUpsertAsync(NewRow(1, "must-not-persist"),
            new[] { nameof(UpsertRow.Name) },
            cancellation.Token);

        try
        {
            await MySqlNativeLockWaitObserver.ObserveAsync(store.Version.IsMariaDb,
                store.Observer,
                worker,
                pending,
                safety.Token);

            await cancellation.CancelAsync();
            canceled = await Record.ExceptionAsync(async () => _ = await pending);
            primary = canceled;
        }
        catch (Exception error)
        {
            primary = error;

            throw;
        }
        finally
        {
            await DatabaseLockProbeCleanup.CompleteLockProbeAsync(cancellation, ReleaseAsync, pending, primary, null);
        }

        var openAfterCall = worker.State;
        var persistedName = await waiting
            .Rows
            .Select(row => row.Name)
            .SingleAsync(safety.Token);

        // Assert
        Assert.Equal(cancellation.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(canceled)
                .CancellationToken);

        Assert.Single(capture.Statements);
        Assert.Equal(ConnectionState.Open, openAfterCall);
        Assert.Equal("original", persistedName);

        // WHY: The test owns this transaction; its release cannot depend on the already cancelled write token.
        Task ReleaseAsync() => transaction.RollbackAsync(CancellationToken.None);
    }

    /// <summary>Two commands reach distinct physical connections before either native UPSERT is allowed to begin.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Concurrent_same_key_upserts_use_native_atomic_conflict_resolution_without_a_read_phase(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", safety.Token);
        var firstReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCapture = new Writes
        {
            BeforeWriteAsync = async (_, token) =>
            {
                firstReady.TrySetResult(true);
                await release.Task.WaitAsync(token);
            },
        };

        var secondCapture = new Writes
        {
            BeforeWriteAsync = async (_, token) =>
            {
                secondReady.TrySetResult(true);
                await release.Task.WaitAsync(token);
            },
        };

        await using var first = store.CreateContext(firstCapture);
        await using var second = store.CreateContext(secondCapture);
        long[]? affected = null;
        Exception? primary = null;

        // Act
        var firstWork = first.ExecuteUpsertAsync(NewRow(1, "first"), new[] { nameof(UpsertRow.Name) }, safety.Token);
        var secondWork = second.ExecuteUpsertAsync(NewRow(1, "second"), new[] { nameof(UpsertRow.Name) }, safety.Token);
        var combined = Task.WhenAll(firstWork, secondWork);

        try
        {
            await Task
                .WhenAll(firstReady.Task, secondReady.Task)
                .WaitAsync(safety.Token);

            release.TrySetResult(true);
            affected = await combined;
        }
        catch (Exception error)
        {
            primary = error;

            throw;
        }
        finally
        {
            release.TrySetResult(true);
            await DatabaseLockProbeCleanup.CompleteLockProbeAsync(safety,
                () => Task.CompletedTask,
                combined,
                primary,
                null);
        }

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(affected);
        Assert.Equal(new long[] { 1, 2 }, affected.Order());
        Assert.Contains(persisted.Name, s_concurrentNames);
        Assert.Equal(1, persisted.Id);
        Assert.Single(firstCapture.Statements);
        Assert.Single(secondCapture.Statements);
        Assert.Single(firstCapture.AllStatements);
        Assert.Single(secondCapture.AllStatements);
        Assert.NotEqual(firstCapture.ServerThreads.Single(), secondCapture.ServerThreads.Single());
    }
}
