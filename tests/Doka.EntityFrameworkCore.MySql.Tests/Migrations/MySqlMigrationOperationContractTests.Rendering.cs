namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies recursive rendering, concurrent rendering, and context deactivation leases.</summary>
public sealed partial class MySqlMigrationOperationContractTests
{
    // WHY: Blocking lease probes must leave the runner's shared thread pool
    // available for the test coordinator, including on small CI runners.
    private static readonly TaskFactory s_renderWorkers = new(
        CancellationToken.None,
        TaskCreationOptions.LongRunning,
        TaskContinuationOptions.None,
        TaskScheduler.Default);

    /// <summary>
    /// Verifies recursive standard rendering is rejected.
    /// </summary>
    [Fact]
    public void Recursive_standard_rendering_is_rejected()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 11));
        MySqlMigrationOperationContext context = null!;
        context = new MySqlMigrationOperationContext(
            new CustomOperation(),
            model: null,
            MigrationsSqlGenerationOptions.Default,
            serverVersion,
            new MySqlMigrationFeatureSet(serverVersion.Profile),
            operationOrdinal: 0,
            "tests.recursive",
            operation => context.RenderStandardOperation(operation));

        Action act = () =>
            context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 1;" });

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.RecursiveProviderRendering, exception.FailureCode);
    }

    /// <summary>
    /// Verifies concurrent standard rendering is rejected.
    /// </summary>
    [Fact]
    public async Task Concurrent_standard_rendering_is_rejected()
    {
        // Arrange
        var rendererEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRenderer = new ManualResetEventSlim();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 11));
        var context = new MySqlMigrationOperationContext(
            new CustomOperation(),
            model: null,
            MigrationsSqlGenerationOptions.Default,
            serverVersion,
            new MySqlMigrationFeatureSet(serverVersion.Profile),
            operationOrdinal: 0,
            "tests.concurrent",
            _ =>
            {
                rendererEntered.TrySetResult();

                // WHY: The owner releases this synchronous callback in finally;
                // an independent deadline would race a delayed test continuation.
                releaseRenderer.Wait();

                return [MySqlMigrationCommandSpec.Create("SELECT 1;")];
            });

        Task<Exception?>? concurrentProbe = null;
        Exception? recordedException;

        // Act
        var activeRender = s_renderWorkers.StartNew(
            () => context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 1;" }));

        try
        {
            await rendererEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            // WHY: A broken concurrency guard can enter the blocking callback.
            // Keep the coordinator free to time out and release both renderers.
            concurrentProbe = s_renderWorkers.StartNew<Exception?>(() => Record.Exception(() =>
                context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 2;" })));

            recordedException = await concurrentProbe.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        finally
        {
            // WHY: A failed coordination step must release and drain both
            // synchronous renderers before their wait handle is disposed.
            releaseRenderer.Set();
            await Task.WhenAll(activeRender, concurrentProbe ?? Task.CompletedTask)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.RecursiveProviderRendering, exception.FailureCode);
    }

    /// <summary>
    /// Verifies deactivation waits for the active render lease and blocks new rendering.
    /// </summary>
    [Fact]
    public async Task Deactivation_waits_for_the_active_render_lease_and_blocks_new_rendering()
    {
        // Arrange
        var rendererEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRenderer = new ManualResetEventSlim();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 11));
        var context = new MySqlMigrationOperationContext(
            new CustomOperation(),
            model: null,
            MigrationsSqlGenerationOptions.Default,
            serverVersion,
            new MySqlMigrationFeatureSet(serverVersion.Profile),
            operationOrdinal: 0,
            "tests.deactivation",
            _ =>
            {
                rendererEntered.TrySetResult();

                // WHY: The lease remains contested until its owner releases it;
                // only the coordinator's waits and cleanup have deadlines.
                releaseRenderer.Wait();

                return [MySqlMigrationCommandSpec.Create("SELECT 1;")];
            });

        Task? deactivation = null;
        Task<bool>? expirationProbe = null;
        Task? prematureCompletion;
        MySqlMigrationOperationHandlerException? expiredException = null;
        bool observedExpiration;

        // Act
        // WHY: Observing the expiring lease and blocking deactivation needs one
        // active render; releasing it earlier would skip the contested state.
        var activeRender = s_renderWorkers.StartNew(
            () => context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 1;" }));

        try
        {
            await rendererEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            // WHY: Synchronous lease closing has no cancellation contract;
            // its owner must release and drain the worker even after a failure.
            deactivation = s_renderWorkers.StartNew(context.Deactivate, CancellationToken.None);
            // WHY: Probe the actual closed lease rather than a signal emitted
            // before Deactivate enters it. A broken guard must not block cleanup.
            expirationProbe = s_renderWorkers.StartNew(() => SpinWait.SpinUntil(
                () =>
                {
                    try
                    {
                        _ = context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 2;" });

                        return false;
                    }
                    catch (MySqlMigrationOperationHandlerException exception) when (exception.FailureCode
                     == MySqlMigrationHandlerFailureCode.RecursiveProviderRendering)
                    {
                        return false;
                    }
                    catch (MySqlMigrationOperationHandlerException exception) when (exception.FailureCode
                     == MySqlMigrationHandlerFailureCode.ContextExpired)
                    {
                        expiredException = exception;

                        return true;
                    }
                },
                TimeSpan.FromSeconds(1)));

            observedExpiration = await expirationProbe.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            prematureCompletion = await Task.WhenAny(
                deactivation,
                Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None));
        }
        finally
        {
            // WHY: Release every callback before draining the owned workers.
            // A missing monitor pulse must fail within a bound instead of hanging.
            releaseRenderer.Set();
            await Task.WhenAll(
                    activeRender,
                    deactivation ?? Task.CompletedTask,
                    expirationProbe ?? Task.CompletedTask)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }

        // Assert
        Assert.NotSame(deactivation, prematureCompletion);
        Assert.True(observedExpiration);
        Assert.NotNull(expiredException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.ContextExpired, expiredException!.FailureCode);
    }
}
