namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Proves a borrowed local transaction is still active without writing data or altering caller ownership.</summary>
/// <remarks>
/// The receipt assumes EF's transaction-control operations execute without interceptor suppression.
/// Like normal EF execution, authorized interceptors may replace or suppress native operations;
/// the void/Task savepoint contract does not expose their interception result to this caller.
/// SAVEPOINT can succeed without creating a marker outside a native transaction; ROLLBACK TO
/// supplies the liveness proof. See <see href="https://mariadb.com/docs/server/reference/sql-statements/transactions/savepoint">MariaDB SAVEPOINT errors</see>
/// and MySQL's <see href="https://github.com/mysql/mysql-server/blob/mysql-8.4.13/sql/transaction.cc">trans_savepoint</see>.
/// After an uncertain result, cleanup targets only the unique marker and retains operation
/// and release failures independently, including a native missing-marker response.
/// </remarks>
internal static class MySqlLocalTransactionGuard
{
    /// <summary>Creates, tests and releases one uniquely owned marker before a transaction-sensitive command.</summary>
    internal static void Validate(
        IRelationalConnection connection
    )
    {
        var transaction = RequireLocalTransaction(connection);
        var marker = NewMarker();
        Exception? failure = null;

        try
        {
            transaction.CreateSavepoint(marker);
            transaction.RollbackToSavepoint(marker);
            RequireSameTransaction(connection, transaction);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            // WHY: SAVEPOINT may have reached the server before its call failed. Releasing
            // only our unique marker never commits, rolls back or disposes the caller's transaction.
            transaction.ReleaseSavepoint(marker);
        }
        catch (Exception exception)
        {
            failure = Combine(failure, exception);
        }

        ThrowFailure(failure);
        RequireSameTransaction(connection, transaction);
    }

    /// <summary>Validates and releases the same local receipt while forwarding request cancellation to its creation.</summary>
    internal static async Task ValidateAsync(
        IRelationalConnection connection,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = RequireLocalTransaction(connection);
        var marker = await CreateValidatedSavepointAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        // WHY: A cancelled request must not abandon its own marker. Data execution
        // remains forbidden until uncancellable release and the final cancellation check finish.
        await transaction
            .ReleaseSavepointAsync(marker, CancellationToken.None)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        RequireSameTransaction(connection, transaction);
    }

    /// <summary>Returns a live, validated invocation marker for a caller that needs its own rollback boundary.</summary>
    internal static async Task<string> CreateValidatedSavepointAsync(
        IRelationalConnection connection,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = RequireLocalTransaction(connection);
        var marker = NewMarker();

        try
        {
            await transaction
                .CreateSavepointAsync(marker, cancellationToken)
                .ConfigureAwait(false);

            // WHY: Immediate ROLLBACK TO proves marker existence; SAVEPOINT success alone
            // cannot establish native liveness, as documented by the engine sources above.
            await transaction
                .RollbackToSavepointAsync(marker, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            RequireSameTransaction(connection, transaction);

            return marker;
        }
        catch (Exception primaryFailure)
        {
            try
            {
                // WHY: The create/rollback response can fail after the server accepted it.
                // Cleanup targets only the unique invocation marker and cannot be cancelled.
                await transaction
                    .ReleaseSavepointAsync(marker, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(primaryFailure, cleanupFailure);
            }

            throw;
        }
    }

    private static IDbContextTransaction RequireLocalTransaction(
        IRelationalConnection connection
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        var transaction = connection.CurrentTransaction;

        if (connection.DbConnection.State != ConnectionState.Open
            || transaction is null
            || !ReferenceEquals(transaction.GetDbTransaction()
                    .Connection,
                connection.DbConnection)
            || !transaction.SupportsSavepoints)
        {
            throw new InvalidOperationException(
                "This operation requires an active local transaction on the same open database connection with savepoint support.");
        }

        return transaction;
    }

    private static void RequireSameTransaction(
        IRelationalConnection connection,
        IDbContextTransaction transaction
    )
    {
        if (!ReferenceEquals(RequireLocalTransaction(connection), transaction))
        {
            throw new InvalidOperationException(
                "The local transaction changed while its active state was being validated.");
        }
    }

    private static string NewMarker() => "__doka_tx_"
        + Guid
            .NewGuid()
            .ToString("N");

    private static Exception Combine(
        Exception? primary,
        Exception cleanup
    ) => primary is null
        ? cleanup
        : new AggregateException(primary, cleanup);

    private static void ThrowFailure(
        Exception? failure
    )
    {
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }
}
