namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlScopedMigrationCommand : MigrationCommand
{
    private const string PreviousSqlModeParameterName = "__doka_previous_sql_mode";
    private readonly IRelationalCommand? _captureCommand;
    private readonly IRelationalCommand? _enableCommand;
    private readonly IRelationalCommand? _bodyCommand;
    private readonly DbContext _context;
    private readonly IRelationalCommand[] _handlerCleanupCommands;
    private readonly IRelationalCommand? _handlerBodyCommand;
    private readonly IRelationalCommand[] _handlerSetupCommands;
    private readonly IRelationalCommand? _restoreCommand;

    public MySqlScopedMigrationCommand(
        MySqlMigrationCommandLayout layout,
        MigrationsSqlGeneratorDependencies dependencies,
        bool transactionSuppressed
    ) : base(
        BuildCommand(dependencies, layout.CommandText),
        dependencies.CurrentContext.Context,
        dependencies.Logger,
        transactionSuppressed)
    {
        Layout = layout;
        _context = dependencies.CurrentContext.Context;

        if (layout.ScopeKind == MySqlMigrationCommandScopeKind.Handler)
        {
            _handlerSetupCommands = new IRelationalCommand[layout.Setup.Count];

            for (var index = 0; index < layout.Setup.Count; index++)
            {
                _handlerSetupCommands[index] = BuildCommand(
                    dependencies,
                    layout
                        .Setup[index]
                        .ToString());
            }

            _handlerBodyCommand = BuildCommand(dependencies, layout.Body.ToString());
            _handlerCleanupCommands = new IRelationalCommand[layout.Cleanup.Count];

            for (var index = 0; index < layout.Cleanup.Count; index++)
            {
                _handlerCleanupCommands[index] = BuildCommand(
                    dependencies,
                    layout
                        .Cleanup[index]
                        .ToString());
            }

            return;
        }

        _handlerSetupCommands = [];
        _handlerCleanupCommands = [];
        _captureCommand = BuildCommand(dependencies, "SELECT @@SESSION.sql_mode;");
        _enableCommand = BuildCommand(
            dependencies,
            "SET SESSION sql_mode = IF("
            + "FIND_IN_SET('NO_BACKSLASH_ESCAPES', @@SESSION.sql_mode), "
            + "@@SESSION.sql_mode, "
            + "CONCAT_WS(',', NULLIF(@@SESSION.sql_mode, ''), 'NO_BACKSLASH_ESCAPES'));");

        _bodyCommand = BuildCommand(dependencies, layout.Body.ToString());
        _restoreCommand = BuildRestoreCommand(dependencies);
    }

    internal MySqlMigrationCommandLayout Layout { get; }

    public override int ExecuteNonQuery(
        IRelationalConnection connection,
        IReadOnlyDictionary<string, object?>? parameterValues = null
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (_handlerBodyCommand is not null)
        {
            return ExecuteHandlerScope(connection, parameterValues);
        }

        var openedConnection = connection.Open();
        string? originalSqlMode = null;
        Exception? primaryException = null;
        var result = 0;

        try
        {
            originalSqlMode = CaptureOriginalSqlMode(connection);
            Execute(_enableCommand!, connection);
            result = Execute(_bodyCommand!, connection, parameterValues);
        }
        catch (Exception exception)
        {
            primaryException = exception;
        }

        var cleanupException = originalSqlMode is null ? null : RestoreSqlMode(connection, originalSqlMode);

        if (openedConnection || cleanupException is not null)
        {
            cleanupException = CombineCleanupFailures(cleanupException, CloseConnection(connection));
        }

        ThrowIfFailed(connection, primaryException, cleanupException);

        return result;
    }

    public override async Task<int> ExecuteNonQueryAsync(
        IRelationalConnection connection,
        IReadOnlyDictionary<string, object?>? parameterValues = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (_handlerBodyCommand is not null)
        {
            return await ExecuteHandlerScopeAsync(connection, parameterValues, cancellationToken)
                .ConfigureAwait(false);
        }

        var openedConnection = await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        string? originalSqlMode = null;
        Exception? primaryException = null;
        var result = 0;

        try
        {
            originalSqlMode = await CaptureOriginalSqlModeAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            await ExecuteAsync(_enableCommand!, connection, null, cancellationToken)
                .ConfigureAwait(false);
            result = await ExecuteAsync(_bodyCommand!, connection, parameterValues, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryException = exception;
        }

        var cleanupException = originalSqlMode is null
            ? null
            : await RestoreSqlModeAsync(connection, originalSqlMode)
                .ConfigureAwait(false);

        if (openedConnection || cleanupException is not null)
        {
            cleanupException = CombineCleanupFailures(
                cleanupException,
                await CloseConnectionAsync(connection)
                    .ConfigureAwait(false));
        }

        await ThrowIfFailedAsync(connection, primaryException, cleanupException)
            .ConfigureAwait(false);

        return result;
    }

    private string CaptureOriginalSqlMode(
        IRelationalConnection connection
    )
    {
        var value = _captureCommand!.ExecuteScalar(CreateParameters(connection));

        return value as string ?? throw new InvalidOperationException("The server returned no session sql_mode value.");
    }

    private async Task<string> CaptureOriginalSqlModeAsync(
        IRelationalConnection connection,
        CancellationToken cancellationToken
    )
    {
        var value = await _captureCommand!
            .ExecuteScalarAsync(CreateParameters(connection), cancellationToken)
            .ConfigureAwait(false);

        return value as string ?? throw new InvalidOperationException("The server returned no session sql_mode value.");
    }

    private Exception? RestoreSqlMode(
        IRelationalConnection connection,
        string originalSqlMode
    )
    {
        try
        {
            Execute(
                _restoreCommand!,
                connection,
                new Dictionary<string, object?>
                {
                    [PreviousSqlModeParameterName] = originalSqlMode,
                });

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async Task<Exception?> RestoreSqlModeAsync(
        IRelationalConnection connection,
        string originalSqlMode
    )
    {
        try
        {
            // Session cleanup must outlive caller cancellation so pooled state cannot leak.
            await ExecuteAsync(
                    _restoreCommand!,
                    connection,
                    new Dictionary<string, object?>
                    {
                        [PreviousSqlModeParameterName] = originalSqlMode,
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private int ExecuteHandlerScope(
        IRelationalConnection connection,
        IReadOnlyDictionary<string, object?>? parameterValues
    )
    {
        var openedConnection = connection.Open();
        Exception? primaryException = null;
        var result = 0;

        try
        {
            foreach (var setupCommand in _handlerSetupCommands)
            {
                _ = Execute(setupCommand, connection);
            }

            result = Execute(_handlerBodyCommand!, connection, parameterValues);
        }
        catch (Exception exception)
        {
            primaryException = exception;
        }

        var cleanupException = ExecuteHandlerCleanup(connection);

        if (openedConnection || cleanupException is not null)
        {
            cleanupException = CombineCleanupFailures(cleanupException, CloseConnection(connection));
        }

        ThrowIfFailed(connection, primaryException, cleanupException);

        return result;
    }

    private async Task<int> ExecuteHandlerScopeAsync(
        IRelationalConnection connection,
        IReadOnlyDictionary<string, object?>? parameterValues,
        CancellationToken cancellationToken
    )
    {
        var openedConnection = await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        Exception? primaryException = null;
        var result = 0;

        try
        {
            foreach (var setupCommand in _handlerSetupCommands)
            {
                _ = await ExecuteAsync(setupCommand, connection, parameterValues: null, cancellationToken)
                    .ConfigureAwait(false);
            }

            result = await ExecuteAsync(_handlerBodyCommand!, connection, parameterValues, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryException = exception;
        }

        var cleanupException = await ExecuteHandlerCleanupAsync(connection)
            .ConfigureAwait(false);

        if (openedConnection || cleanupException is not null)
        {
            cleanupException = CombineCleanupFailures(
                cleanupException,
                await CloseConnectionAsync(connection)
                    .ConfigureAwait(false));
        }

        await ThrowIfFailedAsync(connection, primaryException, cleanupException)
            .ConfigureAwait(false);

        return result;
    }

    private Exception? ExecuteHandlerCleanup(
        IRelationalConnection connection
    )
    {
        Exception? cleanupException = null;

        foreach (var cleanupCommand in _handlerCleanupCommands)
        {
            try
            {
                Execute(cleanupCommand, connection);
            }
            catch (Exception exception)
            {
                cleanupException = CombineCleanupFailures(cleanupException, exception);
            }
        }

        return cleanupException;
    }

    private async Task<Exception?> ExecuteHandlerCleanupAsync(
        IRelationalConnection connection
    )
    {
        Exception? cleanupException = null;

        foreach (var cleanupCommand in _handlerCleanupCommands)
        {
            try
            {
                await ExecuteAsync(cleanupCommand, connection, parameterValues: null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupException = CombineCleanupFailures(cleanupException, exception);
            }
        }

        return cleanupException;
    }

    private int Execute(
        IRelationalCommand command,
        IRelationalConnection connection,
        IReadOnlyDictionary<string, object?>? parameterValues = null
    ) => command.ExecuteNonQuery(CreateParameters(connection, parameterValues));

    private Task<int> ExecuteAsync(
        IRelationalCommand command,
        IRelationalConnection connection,
        IReadOnlyDictionary<string, object?>? parameterValues,
        CancellationToken cancellationToken
    ) => command.ExecuteNonQueryAsync(CreateParameters(connection, parameterValues), cancellationToken);

    private RelationalCommandParameterObject CreateParameters(
        IRelationalConnection connection,
        IReadOnlyDictionary<string, object?>? parameterValues = null
    ) => new(connection, parameterValues, readerColumns: null, _context, CommandLogger, CommandSource.Migrations);

    private static IRelationalCommand BuildCommand(
        MigrationsSqlGeneratorDependencies dependencies,
        string commandText
    )
    {
        var builder = dependencies.CommandBuilderFactory.Create();
        builder.Append(commandText);

        return builder.Build();
    }

    private static IRelationalCommand BuildRestoreCommand(
        MigrationsSqlGeneratorDependencies dependencies
    )
    {
        var stringMapping = dependencies.TypeMappingSource.FindMapping(typeof(string))
            ?? throw new InvalidOperationException("The provider has no string type mapping for sql_mode recovery.");

        var builder = dependencies.CommandBuilderFactory.Create();
        var generatedParameterName = dependencies.SqlGenerationHelper.GenerateParameterName(
            PreviousSqlModeParameterName);

        builder
            .Append("SET SESSION sql_mode = ")
            .Append(generatedParameterName)
            .Append(";")
            .AddParameter(
                PreviousSqlModeParameterName,
                generatedParameterName,
                stringMapping,
                nullable: false,
                ParameterDirection.Input);

        return builder.Build();
    }

    private static void ThrowIfFailed(
        IRelationalConnection connection,
        Exception? primaryException,
        Exception? cleanupException
    )
    {
        if (cleanupException is not null)
        {
            TryClearPool(connection, cleanupException);
        }

        ThrowFailures(primaryException, cleanupException);
    }

    private static Exception? CloseConnection(
        IRelationalConnection connection
    )
    {
        Exception? relationalCloseException = null;

        try
        {
            _ = connection.Close();
        }
        catch (Exception exception)
        {
            relationalCloseException = exception;
        }

        Exception? physicalCloseException = null;
        if (connection.DbConnection.State != ConnectionState.Closed)
        {
            try
            {
                connection.DbConnection.Close();
            }
            catch (Exception exception)
            {
                physicalCloseException = exception;
            }
        }

        return CombineCleanupFailures(relationalCloseException, physicalCloseException);
    }

    private static async Task<Exception?> CloseConnectionAsync(
        IRelationalConnection connection
    )
    {
        Exception? relationalCloseException = null;

        try
        {
            _ = await connection
                .CloseAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            relationalCloseException = exception;
        }

        Exception? physicalCloseException = null;
        if (connection.DbConnection.State != ConnectionState.Closed)
        {
            try
            {
                await connection
                    .DbConnection
                    .CloseAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                physicalCloseException = exception;
            }
        }

        return CombineCleanupFailures(relationalCloseException, physicalCloseException);
    }

    private static Exception? CombineCleanupFailures(
        Exception? firstException,
        Exception? secondException
    )
    {
        if (firstException is null)
        {
            return secondException;
        }

        if (secondException is null)
        {
            return firstException;
        }

        return new AggregateException(
            "Multiple migration cleanup actions failed.",
            firstException,
            secondException);
    }

    private static async Task ThrowIfFailedAsync(
        IRelationalConnection connection,
        Exception? primaryException,
        Exception? cleanupException
    )
    {
        if (cleanupException is not null)
        {
            await TryClearPoolAsync(connection, cleanupException)
                .ConfigureAwait(false);
        }

        ThrowFailures(primaryException, cleanupException);
    }

    internal static void ThrowFailures(
        Exception? primaryException,
        Exception? cleanupException
    )
    {
        if (primaryException is not null
            && cleanupException is not null)
        {
            throw new MySqlMigrationSessionCleanupException(primaryException, cleanupException);
        }

        if (primaryException is not null)
        {
            ExceptionDispatchInfo
                .Capture(primaryException)
                .Throw();
        }

        if (cleanupException is not null)
        {
            throw new MySqlMigrationSessionCleanupException(cleanupException);
        }
    }

    private static void TryClearPool(
        IRelationalConnection connection,
        Exception cleanupException
    )
    {
        if (connection.DbConnection is not MySqlConnection mySqlConnection)
        {
            return;
        }

        // A failed restore makes the physical session unsafe for any pooled borrower.
        try
        {
            MySqlConnection.ClearPool(mySqlConnection);
        }
        catch (Exception poolException)
        {
            cleanupException.Data["DokaMySqlPoolClearFailure"] = poolException;
        }
    }

    private static async Task TryClearPoolAsync(
        IRelationalConnection connection,
        Exception cleanupException
    )
    {
        if (connection.DbConnection is not MySqlConnection mySqlConnection)
        {
            return;
        }

        try
        {
            // Pool cleanup must complete even when the migration was cancelled.
            await MySqlConnection
                .ClearPoolAsync(mySqlConnection, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception poolException)
        {
            cleanupException.Data["DokaMySqlPoolClearFailure"] = poolException;
        }
    }
}
