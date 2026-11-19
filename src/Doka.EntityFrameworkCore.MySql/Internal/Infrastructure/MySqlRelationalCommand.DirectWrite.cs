namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlRelationalCommand
{
    /// <inheritdoc />
    public override int ExecuteNonQuery(
        RelationalCommandParameterObject parameterObject
    ) => IsDirectWrite
        ? ExecuteDirectWrite(parameterObject)
        : base.ExecuteNonQuery(parameterObject);

    /// <inheritdoc />
    public override Task<int> ExecuteNonQueryAsync(
        RelationalCommandParameterObject parameterObject,
        CancellationToken cancellationToken = default
    ) => IsDirectWrite
        ? ExecuteDirectWriteAsync(parameterObject, cancellationToken)

        // WHY: Only the new explicit write contracts need the extended failure ownership.
        // Ordinary commands retain EF's original task, callbacks and execution behavior.
        : base.ExecuteNonQueryAsync(parameterObject, cancellationToken);

    /// <summary>Retains both the direct statement failure and every relevant command/connection cleanup failure.</summary>
    private int ExecuteDirectWrite(
        RelationalCommandParameterObject parameterObject
    )
    {
        ValidateExecutionParameters(parameterObject);
        var connection = parameterObject.Connection;
        var logger = parameterObject.Logger;
        var startTime = DateTimeOffset.UtcNow;
        var shouldLogExecute = logger?.ShouldLogCommandExecute(startTime) == true;
        var commandId = shouldLogExecute || logger?.ShouldLogCommandCreate(startTime) == true
            ? Guid.NewGuid()
            : default;

        DbCommand? command = null;
        Exception? failure = null;
        var opened = false;
        var startTimestamp = 0L;
        var result = 0;

        try
        {
            CreateDirectWriteCommand(parameterObject, commandId, ref command);
            connection.Open();
            opened = true;
            startTimestamp = Stopwatch.GetTimestamp();

            if (connection is MySqlRelationalConnection { RequiresEmulatedAmbientReceipt: true } providerConnection)
            {
                MySqlAmbientTransactionGuard.Validate(providerConnection);
            }

            var interception = shouldLogExecute
                ? logger!.CommandNonQueryExecuting(
                    connection,
                    command,
                    LogCommandText,
                    parameterObject.Context,
                    commandId,
                    connection.ConnectionId,
                    startTime,
                    parameterObject.CommandSource)
                : default;

            result = interception.HasResult
                ? interception.Result
                : command.ExecuteNonQuery();

            if (shouldLogExecute)
            {
                result = logger!.CommandNonQueryExecuted(
                    connection,
                    command,
                    LogCommandText,
                    parameterObject.Context,
                    commandId,
                    connection.ConnectionId,
                    result,
                    startTime,
                    Stopwatch.GetElapsedTime(startTimestamp),
                    parameterObject.CommandSource);
            }
        }
        catch (Exception exception)
        {
            failure = exception;

            if (opened
                && command is not null
                && logger is not null)
            {
                try
                {
                    if (Dependencies.ExceptionDetector.IsCancellation(exception))
                    {
                        logger.CommandCanceled(connection,
                            command,
                            LogCommandText,
                            parameterObject.Context,
                            DbCommandMethod.ExecuteNonQuery,
                            commandId,
                            connection.ConnectionId,
                            startTime,
                            Stopwatch.GetElapsedTime(startTimestamp),
                            parameterObject.CommandSource);
                    }
                    else
                    {
                        logger.CommandError(connection,
                            command,
                            LogCommandText,
                            parameterObject.Context,
                            DbCommandMethod.ExecuteNonQuery,
                            commandId,
                            connection.ConnectionId,
                            exception,
                            startTime,
                            Stopwatch.GetElapsedTime(startTimestamp),
                            parameterObject.CommandSource);
                    }
                }
                catch (Exception diagnosticFailure)
                {
                    failure = CombineDirectWriteFailures(failure, diagnosticFailure);
                }
            }
        }

        failure = CleanupDirectWrite(command, connection, opened, failure);

        if (failure is not null)
        {
            ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }

        return result;
    }

    /// <summary>Executes exactly once with normal EF diagnostics and uncancellable failure cleanup.</summary>
    private async Task<int> ExecuteDirectWriteAsync(
        RelationalCommandParameterObject parameterObject,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateExecutionParameters(parameterObject);
        var connection = parameterObject.Connection;
        var logger = parameterObject.Logger;
        var startTime = DateTimeOffset.UtcNow;
        var shouldLogExecute = logger?.ShouldLogCommandExecute(startTime) == true;
        var commandId = shouldLogExecute || logger?.ShouldLogCommandCreate(startTime) == true
            ? Guid.NewGuid()
            : default;

        DbCommand? command = null;
        Exception? failure = null;
        var opened = false;
        var startTimestamp = 0L;
        var result = 0;

        try
        {
            CreateDirectWriteCommand(parameterObject, commandId, ref command);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            opened = true;
            startTimestamp = Stopwatch.GetTimestamp();

            if (connection is MySqlRelationalConnection { RequiresEmulatedAmbientReceipt: true } providerConnection)
            {
                await MySqlAmbientTransactionGuard
                    .ValidateAsync(providerConnection, cancellationToken)
                    .ConfigureAwait(false);
            }

            var interception = shouldLogExecute
                ? await logger!
                    .CommandNonQueryExecutingAsync(
                        connection,
                        command,
                        LogCommandText,
                        parameterObject.Context,
                        commandId,
                        connection.ConnectionId,
                        startTime,
                        parameterObject.CommandSource,
                        cancellationToken)
                    .ConfigureAwait(false)
                : default;

            result = interception.HasResult
                ? interception.Result
                : await command
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (shouldLogExecute)
            {
                result = await logger!
                    .CommandNonQueryExecutedAsync(
                        connection,
                        command,
                        LogCommandText,
                        parameterObject.Context,
                        commandId,
                        connection.ConnectionId,
                        result,
                        startTime,
                        Stopwatch.GetElapsedTime(startTimestamp),
                        parameterObject.CommandSource,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = exception;

            if (opened
                && command is not null
                && logger is not null)
            {
                try
                {
                    if (Dependencies.ExceptionDetector.IsCancellation(exception, cancellationToken))
                    {
                        await logger
                            .CommandCanceledAsync(connection,
                                command,
                                LogCommandText,
                                parameterObject.Context,
                                DbCommandMethod.ExecuteNonQuery,
                                commandId,
                                connection.ConnectionId,
                                startTime,
                                Stopwatch.GetElapsedTime(startTimestamp),
                                parameterObject.CommandSource,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await logger
                            .CommandErrorAsync(connection,
                                command,
                                LogCommandText,
                                parameterObject.Context,
                                DbCommandMethod.ExecuteNonQuery,
                                commandId,
                                connection.ConnectionId,
                                exception,
                                startTime,
                                Stopwatch.GetElapsedTime(startTimestamp),
                                parameterObject.CommandSource,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception diagnosticFailure)
                {
                    failure = CombineDirectWriteFailures(failure, diagnosticFailure);
                }
            }
        }

        failure = await CleanupDirectWriteAsync(command, connection, opened, failure)
            .ConfigureAwait(false);

        if (failure is not null)
        {
            ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }

        return result;
    }

    /// <summary>Closes only the successfully acquired EF reference, even if clearing or disposing the command fails.</summary>
    private static Exception? CleanupDirectWrite(
        DbCommand? command,
        IRelationalConnection connection,
        bool opened,
        Exception? failure
    )
    {
        if (command is not null)
        {
            try
            {
                command.Parameters.Clear();
            }
            catch (Exception clearFailure)
            {
                failure = CombineDirectWriteFailures(failure, clearFailure);
            }

            try
            {
                command.Dispose();
            }
            catch (Exception disposeFailure)
            {
                failure = CombineDirectWriteFailures(failure, disposeFailure);
            }
        }

        if (opened)
        {
            try
            {
                connection.Close();
            }
            catch (Exception closeFailure)
            {
                failure = CombineDirectWriteFailures(failure, closeFailure);
            }
        }

        return failure;
    }

    /// <summary>Completes invariant-restoring cleanup after cancellation without replacing the original failure.</summary>
    private static async Task<Exception?> CleanupDirectWriteAsync(
        DbCommand? command,
        IRelationalConnection connection,
        bool opened,
        Exception? failure
    )
    {
        // WHY: Cancellation cannot abandon an acquired connection reference or command.
        // Each cleanup step remains independent so an earlier failure cannot skip closing.
        if (command is not null)
        {
            try
            {
                command.Parameters.Clear();
            }
            catch (Exception clearFailure)
            {
                failure = CombineDirectWriteFailures(failure, clearFailure);
            }

            try
            {
                await command
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception disposeFailure)
            {
                failure = CombineDirectWriteFailures(failure, disposeFailure);
            }
        }

        if (opened)
        {
            try
            {
                await connection
                    .CloseAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception closeFailure)
            {
                failure = CombineDirectWriteFailures(failure, closeFailure);
            }
        }

        return failure;
    }

    /// <summary>Retains original exception identity when cleanup succeeds and both causes when it does not.</summary>
    private static Exception CombineDirectWriteFailures(
        Exception? primary,
        Exception cleanup
    ) => primary is null
        ? cleanup
        : new AggregateException(primary, cleanup);
}
