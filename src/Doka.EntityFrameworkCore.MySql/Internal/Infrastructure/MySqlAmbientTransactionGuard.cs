namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Proves native liveness for a confirmed non-XA ambient enlistment without modifying caller ownership.</summary>
/// <remarks>
/// An uncertain batch still attempts release of its unique marker. Native marker absence
/// remains independent cleanup evidence; neither that response nor callback/disposal failures
/// may replace or suppress the original operation failure.
/// </remarks>
internal static class MySqlAmbientTransactionGuard
{
    /// <summary>Checks one fresh three-control batch on the actual enlisted physical connection.</summary>
    internal static void Validate(
        MySqlRelationalConnection connection
    )
    {
        var physical = connection.DbConnection;
        var receipt = connection.CaptureEmulatedAmbientReceipt();
        var timeout = connection.CommandTimeout;
        var marker = "__doka_ambient_"
            + Guid
                .NewGuid()
                .ToString("N");

        DbCommand? command = null;
        Exception? failure = null;
        var attempted = false;
        var completed = false;

        try
        {
            command = CreateControlCommand(physical, timeout, Batch(marker));
            RequireReceipt(connection, physical, receipt);
            attempted = true;
            command.ExecuteNonQuery();
            completed = true;
            RequireReceipt(connection, physical, receipt);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        failure = DisposeCommand(command, failure);

        if (attempted && !completed)
        {
            failure = ReleaseUncertainMarker(physical, timeout, marker, failure);
        }

        if (failure is null)
        {
            try
            {
                RequireReceipt(connection, physical, receipt);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        ThrowFailure(failure);
    }

    /// <summary>Forwards request cancellation to the receipt and completes uncertain-marker cleanup independently.</summary>
    internal static async Task ValidateAsync(
        MySqlRelationalConnection connection,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var physical = connection.DbConnection;
        var receipt = connection.CaptureEmulatedAmbientReceipt();
        var timeout = connection.CommandTimeout;
        var marker = "__doka_ambient_"
            + Guid
                .NewGuid()
                .ToString("N");

        DbCommand? command = null;
        Exception? failure = null;
        var attempted = false;
        var completed = false;

        try
        {
            command = CreateControlCommand(physical, timeout, Batch(marker));
            cancellationToken.ThrowIfCancellationRequested();
            RequireReceipt(connection, physical, receipt);
            attempted = true;
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            completed = true;
            cancellationToken.ThrowIfCancellationRequested();
            RequireReceipt(connection, physical, receipt);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        failure = await DisposeCommandAsync(command, failure)
            .ConfigureAwait(false);

        if (attempted && !completed)
        {
            failure = await ReleaseUncertainMarkerAsync(physical, timeout, marker, failure)
                .ConfigureAwait(false);
        }

        if (failure is null)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireReceipt(connection, physical, receipt);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        ThrowFailure(failure);
    }

    private static DbCommand CreateControlCommand(
        DbConnection physical,
        int? timeout,
        string sql
    )
    {
        var command = physical.CreateCommand();

        try
        {
            // WHY: MySqlConnector owns the emulated ambient transaction internally.
            // Passing an EF/local transaction would target a different ownership contract.
            command.Transaction = null;
            command.CommandText = sql;

            if (timeout is { } seconds)
            {
                command.CommandTimeout = seconds;
            }

            return command;
        }
        catch (Exception primaryFailure)
        {
            // WHY: Configuration failed before any async I/O; this shared synchronous
            // constructor releases its newly created command without starting execution.
            var failure = DisposeCommand(command, primaryFailure);
            ThrowFailure(failure);

            throw;
        }
    }

    private static string Batch(
            string marker
        )

        // WHY: An emulated ambient transaction can stay managed-Active after native rollback.
        // SAVEPOINT alone succeeds outside a native transaction; the immediate ROLLBACK TO
        // rejects that state. One batch avoids exposing an intermediate receipt to caller work.
        // MySqlConnector always permits command batches; this is not an AllowBatch opt-in.
        // Sources: https://mariadb.com/docs/server/reference/sql-statements/transactions/savepoint
        // https://mysqlconnector.net/connection-options/#unsupported-options
        => "SAVEPOINT " + marker + ";ROLLBACK TO SAVEPOINT " + marker + ";RELEASE SAVEPOINT " + marker + ";";

    private static void RequireReceipt(
        MySqlRelationalConnection connection,
        DbConnection physical,
        object receipt
    )
    {
        if (connection.CurrentTransaction is not null
            || !connection.MatchesEmulatedAmbientReceipt(physical, receipt))
        {
            throw new InvalidOperationException(
                "The confirmed emulated ambient transaction changed while its native state was being validated.");
        }
    }

    private static Exception? ReleaseUncertainMarker(
        DbConnection physical,
        int? timeout,
        string marker,
        Exception? failure
    )
    {
        DbCommand? cleanup = null;

        try
        {
            cleanup = CreateControlCommand(physical, timeout, "RELEASE SAVEPOINT " + marker + ";");
            cleanup.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            failure = Combine(failure, exception);
        }

        return DisposeCommand(cleanup, failure);
    }

    private static async Task<Exception?> ReleaseUncertainMarkerAsync(
        DbConnection physical,
        int? timeout,
        string marker,
        Exception? failure
    )
    {
        DbCommand? cleanup = null;

        try
        {
            cleanup = CreateControlCommand(physical, timeout, "RELEASE SAVEPOINT " + marker + ";");

            // WHY: Only our unique marker is released after an uncertain response.
            // Request cancellation cannot skip this cleanup; no caller rollback/disposal occurs.
            await cleanup
                .ExecuteNonQueryAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = Combine(failure, exception);
        }

        return await DisposeCommandAsync(cleanup, failure)
            .ConfigureAwait(false);
    }

    private static Exception? DisposeCommand(
        DbCommand? command,
        Exception? failure
    )
    {
        try
        {
            command?.Dispose();
        }
        catch (Exception exception)
        {
            failure = Combine(failure, exception);
        }

        return failure;
    }

    private static async Task<Exception?> DisposeCommandAsync(
        DbCommand? command,
        Exception? failure
    )
    {
        try
        {
            if (command is not null)
            {
                // WHY: Command cleanup must finish even when the receipt was cancelled.
                await command
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = Combine(failure, exception);
        }

        return failure;
    }

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
