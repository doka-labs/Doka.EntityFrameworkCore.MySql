namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Executes one resolved native INSERT SELECT without an implicit execution-strategy retry.</summary>
internal static class MySqlInsertSelectExecutor
{
    /// <summary>Uses current query parameters, normal command diagnostics and the existing connection command pool.</summary>
#pragma warning disable EF1001 // The adapter consumes EF's existing cache rather than copying its private resolver builder.
    public static async Task<int> ExecuteAsync(
        RelationalQueryContext queryContext,
        RelationalCommandCache commandCache,
        Type contextType,
        bool threadSafetyChecksEnabled,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var criticalSection = threadSafetyChecksEnabled
            ? (ConcurrencyDetectorCriticalSectionDisposer?)queryContext.ConcurrencyDetector.EnterCriticalSection()
            : null;

        IRelationalCommand? command = null;
        Exception? failure = null;
        var result = 0;

        try
        {
            var template = commandCache.GetRelationalCommandTemplate(queryContext.Parameters);
            command = queryContext.Connection.RentCommand();
            command.PopulateFrom(template);
            var parameters = new RelationalCommandParameterObject(
                queryContext.Connection,
                queryContext.Parameters,
                null,
                queryContext.Context,
                queryContext.CommandLogger,
                CommandSource.LinqQuery);

            if (command is not MySqlRelationalCommand providerCommand)
            {
                throw new InvalidOperationException(
                    "INSERT SELECT requires the provider's direct-write command policy.");
            }

            // WHY: Validate cache-hit values before any transaction-control I/O. A client
            // transaction can remain associated after the server aborted it; its reference
            // alone cannot prevent a subsequent INSERT from accidentally autocommitting.
            providerCommand.ValidateExecutionParameters(parameters);

            if (queryContext.Connection.CurrentTransaction is not null)
            {
                await MySqlLocalTransactionGuard
                    .ValidateAsync(queryContext.Connection, cancellationToken)
                    .ConfigureAwait(false);
            }

            // WHY: The native write is deliberately outside ExecutionStrategy.ExecuteAsync.
            // Replaying an insert after an unknown outcome can duplicate committed rows.
            result = await command
                .ExecuteNonQueryAsync(parameters, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;

            try
            {
                if (queryContext.ExceptionDetector.IsCancellation(exception, cancellationToken))
                {
                    queryContext.QueryLogger.QueryCanceled(contextType);
                }
                else
                {
                    queryContext.QueryLogger.NonQueryOperationFailed(contextType, exception);
                }
            }
            catch (Exception diagnosticFailure)
            {
                failure = new AggregateException(failure, diagnosticFailure);
            }
        }

        if (command is not null)
        {
            try
            {
                // WHY: Return only this invocation's rented wrapper. Its underlying DbCommand
                // cleanup belongs to the typed direct-write path, including cancellation.
                queryContext.Connection.ReturnCommand(command);
            }
            catch (Exception returnFailure)
            {
                failure = failure is null
                    ? returnFailure
                    : new AggregateException(failure, returnFailure);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }

        return result;
    }
#pragma warning restore EF1001
}
