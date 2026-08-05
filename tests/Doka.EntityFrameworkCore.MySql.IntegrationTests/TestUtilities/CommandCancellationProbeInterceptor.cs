namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Records the cancellation token delivered to the relational command boundary.
/// </summary>
/// <remarks>
/// The interceptor throws before network I/O so cancellation propagation can be
/// verified deterministically. Separate operability tests exercise cancellation
/// inside MySqlConnector against a running database command.
/// </remarks>
internal sealed class CommandCancellationProbeInterceptor : DbCommandInterceptor
{
    private int _invocationCount;

    /// <summary>
    /// Gets the token observed on the most recent asynchronous reader command.
    /// </summary>
    public CancellationToken ReceivedCancellationToken { get; private set; }

    /// <summary>
    /// Gets the number of intercepted asynchronous reader commands.
    /// </summary>
    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        ReceivedCancellationToken = cancellationToken;
        Interlocked.Increment(ref _invocationCount);

        throw new OperationCanceledException(cancellationToken);
    }
}
