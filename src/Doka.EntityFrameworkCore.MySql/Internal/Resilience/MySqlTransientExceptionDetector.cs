namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlTransientExceptionDetector : IMySqlTransientExceptionDetector
{
    private const int MaxInnerExceptionDepth = 20;

    public bool IsCancellation(
        Exception exception
    ) => (ClassifyTerminalConditions(exception) & TerminalCondition.Cancellation) != 0;

    public bool IsCommandTimeout(
        Exception exception
    ) => (ClassifyTerminalConditions(exception) & TerminalCondition.CommandTimeout) != 0;

    public bool ShouldRetryOn(
        Exception exception
    )
    {
        // WHY: A NOWAIT conflict keeps its original native exception, but must remain a single
        // attempt after command/connection cleanup. Ordinary lock-wait timeouts retain their policy.
        if (ContainsNonRetryableFailure(exception))
        {
            return false;
        }

        // Cancellation and command timeout are terminal classifications for
        // the whole bounded chain. Resolve them before a transient outer
        // wrapper can short-circuit traversal and schedule another attempt.
        if (ClassifyTerminalConditions(exception) != TerminalCondition.None)
        {
            return false;
        }

        var current = exception;
        var depth = 0;
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);

        while (current is not null
            && depth < MaxInnerExceptionDepth
            && visited.Add(current))
        {
            if (current is MySqlException mySqlException)
            {
                if (IsKnownRetryableErrorCode(mySqlException.ErrorCode)
                    || mySqlException.IsTransient)
                {
                    return true;
                }

                // Connector failures without a server error number are
                // transport wrappers. Continue into their bounded cause chain;
                // a real server error remains an authoritative terminal verdict.
                if (mySqlException.Number != 0)
                {
                    return false;
                }
            }

            if (current is SocketException or IOException)
            {
                return true;
            }

            current = current.InnerException;
            depth++;
        }

        return false;
    }

    private static bool ContainsNonRetryableFailure(
        Exception exception
    )
    {
        var current = exception;
        var depth = 0;
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);

        while (current is not null
            && depth < MaxInnerExceptionDepth
            && visited.Add(current))
        {
            if (current is MySqlMigrationSessionCleanupException
                || (current is MySqlException && MySqlLockingReadMetadata.IsNoWaitConflict(current)))
            {
                return true;
            }

            current = current.InnerException;
            depth++;
        }

        return false;
    }

    private static TerminalCondition ClassifyTerminalConditions(
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        var classification = TerminalCondition.None;
        var current = exception;
        var depth = 0;
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);

        while (current is not null
            && depth < MaxInnerExceptionDepth
            && visited.Add(current))
        {
            if (current is OperationCanceledException)
            {
                classification |= TerminalCondition.Cancellation;
            }

            if (current is TimeoutException or MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired })
            {
                classification |= TerminalCondition.CommandTimeout;
            }

            current = current.InnerException;
            depth++;
        }

        return classification;
    }

    private static bool IsKnownRetryableErrorCode(
        MySqlErrorCode errorCode
    ) => errorCode is MySqlErrorCode.ConnectionCountError
        or MySqlErrorCode.TooManyUserConnections
        or MySqlErrorCode.UnableToConnectToHost
        or MySqlErrorCode.ServerShutdown
        or MySqlErrorCode.LockWaitTimeout
        or MySqlErrorCode.LockDeadlock
        or MySqlErrorCode.XARBDeadlock
        or MySqlErrorCode.UserLockDeadlock;

    [Flags]
    private enum TerminalCondition
    {
        None = 0,
        Cancellation = 1,
        CommandTimeout = 2,
    }
}
