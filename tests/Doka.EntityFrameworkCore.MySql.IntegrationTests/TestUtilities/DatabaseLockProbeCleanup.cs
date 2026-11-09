using System.Runtime.ExceptionServices;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Completes test-owned database lock work before resource reuse without masking its primary failure.</summary>
internal static class DatabaseLockProbeCleanup
{
    /// <summary>Releases a probe's lock and observes all owned work without masking its primary failure.</summary>
    /// <param name="cancellation">The cancellation source, or null when unlocking completes the work.</param>
    /// <param name="releaseLock">The protected asynchronous release of the probe-owned database lock.</param>
    /// <param name="pending">The operation that must finish before its resources can be disposed.</param>
    /// <param name="primaryFailure">The existing failure that remains authoritative over teardown failures.</param>
    /// <param name="afterDrain">Optional resource cleanup that may run only after owned work is observed.</param>
    internal static async Task CompleteLockProbeAsync(
        CancellationTokenSource? cancellation,
        Func<Task> releaseLock,
        Task pending,
        Exception? primaryFailure,
        Func<Task>? afterDrain
    )
    {
        // WHY: Cancellation, lock release, and task drainage must all finish even if one fails.
        // This ownership boundary deliberately has no cancellation token or abandonable timed wait.
        List<Exception>? failures = null;

        try
        {
            if (cancellation is not null)
            {
                await cancellation
                    .CancelAsync()
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await releaseLock()
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            // WHY: IsCompleted also includes faulted and canceled tasks, which still require observation.
            await pending.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(exception, primaryFailure))
            {
                (failures ??= []).Add(exception);
            }
        }

        if (afterDrain is not null)
        {
            try
            {
                await afterDrain()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is null)
        {
            return;
        }

        if (primaryFailure is not null)
        {
            // WHY: Keep secondary failures available without changing the original exception identity or stack.
            // Nested lock probes can report teardown against the same primary failure, so retain earlier evidence.
            if (primaryFailure.Data[nameof(CompleteLockProbeAsync)] is AggregateException previousFailures)
            {
                failures.InsertRange(0, previousFailures.InnerExceptions);
            }

            primaryFailure.Data[nameof(CompleteLockProbeAsync)] = new AggregateException(failures);

            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo
                .Capture(failures[0])
                .Throw();
        }

        throw new AggregateException("Lock probe teardown failed.", failures);
    }
}
