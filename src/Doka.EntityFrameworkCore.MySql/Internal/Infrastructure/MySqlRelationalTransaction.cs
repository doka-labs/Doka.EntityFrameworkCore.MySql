namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlRelationalTransaction : RelationalTransaction
{
    private static readonly MySqlTransientExceptionDetector s_transientExceptionDetector = new();

    private readonly MySqlSingletonOptions _singletonOptions;
    private readonly DbTransaction _transaction;
    private readonly Guid _transactionId;

    public MySqlRelationalTransaction(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned,
        ISqlGenerationHelper sqlGenerationHelper,
        MySqlSingletonOptions singletonOptions
    ) : base(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
    {
        _singletonOptions = singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _transactionId = transactionId;
    }

    public override bool SupportsSavepoints =>
        base.SupportsSavepoints && (_singletonOptions.Capabilities?.SupportsSavepoints ?? false);

    public override void CreateSavepoint(
        string name
    )
    {
        EnsureSavepointsSupported();
        base.CreateSavepoint(name);
    }

    public override Task CreateSavepointAsync(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        EnsureSavepointsSupported();
        return base.CreateSavepointAsync(name, cancellationToken);
    }

    public override void RollbackToSavepoint(
        string name
    )
    {
        EnsureSavepointsSupported();
        base.RollbackToSavepoint(name);
    }

    public override Task RollbackToSavepointAsync(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        EnsureSavepointsSupported();
        return base.RollbackToSavepointAsync(name, cancellationToken);
    }

    public override void ReleaseSavepoint(
        string name
    )
    {
        EnsureSavepointsSupported();
        base.ReleaseSavepoint(name);
    }

    public override Task ReleaseSavepointAsync(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        EnsureSavepointsSupported();
        return base.ReleaseSavepointAsync(name, cancellationToken);
    }

    public override void Commit()
    {
        try
        {
            base.Commit();
        }
        catch (Exception exception) when (LogCommitUnknown(exception))
        {
            throw;
        }
    }

    public override async Task CommitAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await base
                .CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (LogCommitUnknown(exception))
        {
            throw;
        }
    }

    private void EnsureSavepointsSupported()
    {
        if (!SupportsSavepoints)
        {
            throw new NotSupportedException(
                "Savepoints are not supported for the configured MySQL driver/server capability profile.");
        }
    }

    private bool LogCommitUnknown(
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        var logger = _singletonOptions.ResilienceLogger;
        var capabilities = _singletonOptions.Capabilities;

        if (logger is null
            || capabilities is null
            || !s_transientExceptionDetector.ShouldRetryOn(exception, capabilities))
        {
            return false;
        }

        MySqlLoggerMessages.CommitUnknown(
            logger,
            _transactionId,
            _transaction.Connection?.State.ToString() ?? "Unknown",
            exception);

        return false;
    }
}
