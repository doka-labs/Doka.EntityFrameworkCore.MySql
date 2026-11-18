namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlRelationalCommand
{
    /// <summary>Publishes each adopted command before EF creation callbacks or parameter binding can fail.</summary>
    private void CreateDirectWriteCommand(
        RelationalCommandParameterObject parameterObject,
        Guid commandId,
        // ReSharper disable once RedundantAssignment
        [NotNull] ref DbCommand? command
    )
    {
        var connection = parameterObject.Connection;
        var context = parameterObject.Context;
        var logger = parameterObject.Logger;
        var connectionId = connection.ConnectionId;
        var startTime = DateTimeOffset.UtcNow;
        var startTimestamp = Stopwatch.GetTimestamp();
        var logCommandCreate = logger?.ShouldLogCommandCreate(startTime) == true;

        // WHY: EF Core 11's CreateDbCommand keeps the native command in a local until
        // every callback and binder succeeds. Publishing ownership immediately lets
        // the direct-write cleanup retain both initialization and disposal failures.
        // Replacement assignments preserve EF's existing interceptor ownership order.
        if (logCommandCreate)
        {
            var interception = logger!.CommandCreating(
                connection,
                DbCommandMethod.ExecuteNonQuery,
                context,
                commandId,
                connectionId,
                startTime,
                parameterObject.CommandSource);

            command = interception.HasResult
                ? interception.Result
                : connection.DbConnection.CreateCommand();

            command = logger.CommandCreated(
                connection,
                command,
                DbCommandMethod.ExecuteNonQuery,
                context,
                commandId,
                connectionId,
                startTime,
                Stopwatch.GetElapsedTime(startTimestamp),
                parameterObject.CommandSource);
        }
        else
        {
            command = connection.DbConnection.CreateCommand();
        }

        command.CommandText = CommandText;

        if (connection.CurrentTransaction is { } transaction)
        {
            command.Transaction = transaction.GetDbTransaction();
        }

        if (connection.CommandTimeout is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        // WHY: Parameters is an IReadOnlyList; indexing preserves the existing EF
        // binding order without allocating its interface enumerator on this path.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Parameters.Count; index++)
        {
            Parameters[index]
                .AddDbParameter(command, parameterObject.ParameterValues);
        }

        if (logCommandCreate)
        {
            command = logger!.CommandInitialized(
                connection,
                command,
                DbCommandMethod.ExecuteNonQuery,
                context,
                commandId,
                connectionId,
                startTime,
                Stopwatch.GetElapsedTime(startTimestamp),
                parameterObject.CommandSource);
        }
    }
}
