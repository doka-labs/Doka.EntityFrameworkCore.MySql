namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlTransientExceptionDetector : IMySqlTransientExceptionDetector
{
    private const int MaxInnerExceptionDepth = 20;

    public bool IsCommandTimeout(
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        var current = exception;
        var depth = 0;

        while (current is not null
               && depth < MaxInnerExceptionDepth)
        {
            if (current is MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired })
            {
                return true;
            }

            if (current is TimeoutException)
            {
                return true;
            }

            current = current.InnerException;
            depth++;
        }

        return false;
    }

    public bool ShouldRetryOn(
        Exception exception,
        ServerCapabilities capabilities
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(capabilities);

        var current = exception;
        var depth = 0;

        while (current is not null
               && depth < MaxInnerExceptionDepth)
        {
            if (current is OperationCanceledException)
            {
                return false;
            }

            if (IsCommandTimeout(current))
            {
                return false;
            }

            if (current is MySqlException mySqlException)
            {
                return mySqlException.IsTransient || IsKnownRetryableErrorCode(mySqlException.ErrorCode);
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
