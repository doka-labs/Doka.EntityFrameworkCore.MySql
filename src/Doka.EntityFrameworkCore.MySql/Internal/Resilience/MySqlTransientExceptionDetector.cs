namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlTransientExceptionDetector : IMySqlTransientExceptionDetector
{
    private const int MaxInnerExceptionDepth = 20;

    [Flags]
    private enum TerminalCondition
    {
        None = 0,
        Cancellation = 1,
        CommandTimeout = 2,
    }

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
        if (ContainsMigrationSessionCleanupFailure(exception))
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

        while (current is not null
               && depth < MaxInnerExceptionDepth)
        {
            if (current is MySqlException mySqlException)
            {
                return IsKnownRetryableErrorCode(mySqlException.ErrorCode) || mySqlException.IsTransient;
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

    private static bool ContainsMigrationSessionCleanupFailure(
        Exception exception
    )
    {
        var current = exception;
        var depth = 0;

        while (current is not null
               && depth < MaxInnerExceptionDepth)
        {
            if (current is MySqlMigrationSessionCleanupException)
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

        while (current is not null
               && depth < MaxInnerExceptionDepth)
        {
            if (current is OperationCanceledException)
            {
                classification |= TerminalCondition.Cancellation;
            }

            if (current is TimeoutException
                || current is MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired })
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
}
