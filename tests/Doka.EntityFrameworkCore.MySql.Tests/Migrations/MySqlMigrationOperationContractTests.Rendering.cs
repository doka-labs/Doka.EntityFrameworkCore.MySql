namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies recursive rendering, concurrent rendering, and context deactivation leases.</summary>
public sealed partial class MySqlMigrationOperationContractTests
{
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

                // Rendering is a synchronous provider contract. Hold this worker
                // deliberately while the async test probes the concurrent caller.
                return releaseRenderer.Wait(TimeSpan.FromSeconds(5))
                    ? [MySqlMigrationCommandSpec.Create("SELECT 1;")]
                    : throw new TimeoutException("The concurrent-render test did not release its first renderer.");
            });

        // Act
        var activeRender = Task.Run(
            () => context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 1;" }),
            CancellationToken.None);

        Exception? recordedException;

        try
        {
            await rendererEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            recordedException = Record.Exception(() =>
                context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 2;" }));
        }
        finally
        {
            // WHY: A failed coordination step must release and drain the first
            // synchronous render before its wait handle is disposed.
            releaseRenderer.Set();
            _ = await activeRender;
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
        var deactivationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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

                // Rendering is a synchronous provider contract. Hold this worker
                // deliberately until the async test observes the expiring lease.
                if (!releaseRenderer.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The deactivation test did not release its renderer.");
                }

                return [MySqlMigrationCommandSpec.Create("SELECT 1;")];
            });

        Task? deactivation = null;
        Task? prematureCompletion;
        MySqlMigrationOperationHandlerException? expiredException = null;
        bool observedExpiration;

        // Act
        // WHY: Observing the expiring lease and blocking deactivation needs one
        // active render; releasing it earlier would skip the contested state.
        var activeRender = Task.Run(
            () => context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 1;" }),
            CancellationToken.None);

        try
        {
            await rendererEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            deactivation = Task.Run(() =>
            {
                deactivationStarted.TrySetResult();
                context.Deactivate();
            }, CancellationToken.None);

            await deactivationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            prematureCompletion = await Task.WhenAny(
                deactivation,
                Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None));

            observedExpiration = SpinWait.SpinUntil(
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
                TimeSpan.FromSeconds(1));
        }
        finally
        {
            // WHY: Both workers must finish even if the contested-state probe
            // times out, so a failing test cannot leave a blocked render behind.
            releaseRenderer.Set();

            try
            {
                _ = await activeRender;
            }
            finally
            {
                if (deactivation is not null)
                {
                    await deactivation;
                }
            }
        }

        // Assert
        Assert.NotSame(deactivation, prematureCompletion);
        Assert.True(observedExpiration);
        Assert.NotNull(expiredException);
        Assert.Equal(MySqlMigrationHandlerFailureCode.ContextExpired, expiredException!.FailureCode);
    }
}
