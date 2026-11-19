namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Preserves ordinary execution and enables the connection guard only for typed locking reads.</summary>
internal sealed partial class MySqlRelationalCommand : RelationalCommand
{
    private const int MaxConflictCauseDepth = 20;

    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlRelationalCommand(
        RelationalCommandBuilderDependencies dependencies,
        string commandText,
        string logCommandText,
        IReadOnlyList<IRelationalParameter> parameters
    ) : base(dependencies, commandText, logCommandText, parameters) { }

    /// <summary>Gets or sets the policy copied from the immutable query command template.</summary>
    internal MySqlLockingReadMode LockingReadMode { get; set; }

    /// <summary>Gets or sets the immutable recursive SELECT reader and parameter contract.</summary>
    internal MySqlRecursiveQueryPolicy? RecursiveQueryPolicy { get; set; }

    /// <summary>Gets or sets immutable window parameter constraints checked on each execution.</summary>
    internal MySqlWindowParameterConstraints? WindowParameterConstraints { get; set; }

    /// <summary>Gets or sets whether the explicit direct-write execution path owns this command.</summary>
    internal bool IsDirectWrite { get; set; }

    /// <summary>Gets or sets the privately generated runtime depth names used by recursive direct writes.</summary>
    internal ReadOnlyMemory<string> RecursiveWriteDepthParameters { get; set; }

    /// <inheritdoc />
    public override void PopulateFrom(
        IRelationalCommandTemplate commandTemplate
    )
    {
        base.PopulateFrom(commandTemplate);

        // WHY: EF reuses commands across unrelated queries. Ordinary templates must
        // clear a previous policy rather than inherit another query's transaction guard.
        if (commandTemplate is MySqlRelationalCommand providerCommand)
        {
            LockingReadMode = providerCommand.LockingReadMode;
            RecursiveQueryPolicy = providerCommand.RecursiveQueryPolicy;
            WindowParameterConstraints = providerCommand.WindowParameterConstraints;
            IsDirectWrite = providerCommand.IsDirectWrite;
            RecursiveWriteDepthParameters = providerCommand.RecursiveWriteDepthParameters;
        }
        else
        {
            LockingReadMode = MySqlLockingReadMode.None;
            RecursiveQueryPolicy = null;
            WindowParameterConstraints = null;
            IsDirectWrite = false;
            RecursiveWriteDepthParameters = default;
        }
    }

    /// <inheritdoc />
    public override RelationalDataReader ExecuteReader(
        RelationalCommandParameterObject parameterObject
    )
    {
        ValidateExecutionParameters(parameterObject);

        return RecursiveQueryPolicy is { } policy
            ? ExecuteRecursiveReader(parameterObject, policy)
            : ExecuteOriginalReader(parameterObject);
    }

    /// <inheritdoc />
    public override Task<RelationalDataReader> ExecuteReaderAsync(
        RelationalCommandParameterObject parameterObject,
        CancellationToken cancellationToken = default
    )
    {
        ValidateExecutionParameters(parameterObject);

        // WHY: Ordinary reads keep EF's original task and allocation behavior.
        return RecursiveQueryPolicy is { } policy
            ? ExecuteRecursiveReaderAsync(parameterObject, policy, cancellationToken)
            : ExecuteOriginalReaderAsync(parameterObject, cancellationToken);
    }

    /// <summary>Rejects current cached-query arguments before direct-write transaction probes or data execution.</summary>
    internal void ValidateExecutionParameters(
        RelationalCommandParameterObject parameterObject
    )
    {
        WindowParameterConstraints?.Validate(parameterObject.ParameterValues
            ?? throw new ArgumentOutOfRangeException(nameof(parameterObject), "Window parameter values are required."));

        RecursiveQueryPolicy?.Validate(parameterObject.ParameterValues);

        if (!RecursiveWriteDepthParameters.IsEmpty)
        {
            MySqlRecursiveQueryPolicy.ValidateDepthParameters(RecursiveWriteDepthParameters.Span,
                parameterObject.ParameterValues);
        }
    }

    private RelationalDataReader ExecuteOriginalReader(
        RelationalCommandParameterObject parameterObject
    ) => LockingReadMode == MySqlLockingReadMode.None
        ? base.ExecuteReader(parameterObject)
        : ExecuteLockingReader(parameterObject);

    private Task<RelationalDataReader> ExecuteOriginalReaderAsync(
        RelationalCommandParameterObject parameterObject,
        CancellationToken cancellationToken
    ) => LockingReadMode == MySqlLockingReadMode.None
        ? base.ExecuteReaderAsync(parameterObject, cancellationToken)
        : ExecuteLockingReaderAsync(parameterObject, cancellationToken);

    private RelationalDataReader ExecuteLockingReader(
        RelationalCommandParameterObject parameterObject
    )
    {
        var connection = GetLockingConnection(parameterObject.Connection);
        var previousGuard = connection.BeginLockingReadCommand();

        try
        {
            // WHY: EF opens before creating the DbCommand. The connection validates
            // after that successful open, without a second logical open or hidden transaction.
            return base.ExecuteReader(parameterObject);
        }
        catch (Exception exception)
        {
            MarkNoWaitConflict(exception);

            throw;
        }
        finally
        {
            connection.EndLockingReadCommand(previousGuard);
        }
    }

    private async Task<RelationalDataReader> ExecuteLockingReaderAsync(
        RelationalCommandParameterObject parameterObject,
        CancellationToken cancellationToken
    )
    {
        var connection = GetLockingConnection(parameterObject.Connection);
        var previousGuard = connection.BeginLockingReadCommand();

        try
        {
            return await base
                .ExecuteReaderAsync(parameterObject, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            MarkNoWaitConflict(exception);

            throw;
        }
        finally
        {
            connection.EndLockingReadCommand(previousGuard);
        }
    }

    private static MySqlRelationalConnection GetLockingConnection(
        IRelationalConnection connection
    ) => connection as MySqlRelationalConnection
        ?? throw new InvalidOperationException("A locking read requires the Doka relational connection.");

    private void MarkNoWaitConflict(
        Exception exception
    )
    {
        if (LockingReadMode != MySqlLockingReadMode.NoWait)
        {
            return;
        }

        var cause = exception;

        // WHY: Diagnostic or driver wrappers preserve the native conflict as a cause.
        // Match the detector's depth bound; the fixed cap also terminates cycles without a visited-set allocation.
        for (var depth = 0; depth < MaxConflictCauseDepth && cause is not null; depth++)
        {
            if (cause is MySqlException { Number: 1205 or 3572 })
            {
                MySqlLockingReadMetadata.MarkNoWaitConflict(cause);
            }

            cause = cause.InnerException;
        }
    }
}
