namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlRelationalConnection : RelationalConnection
{
    private readonly IMySqlDriverFacade _driverFacade;
    private readonly MySqlOptionsExtension _optionsExtension;

    public MySqlRelationalConnection(
        RelationalConnectionDependencies dependencies,
        IMySqlDriverFacade driverFacade
    ) : base(dependencies)
    {
        _driverFacade = driverFacade ?? throw new ArgumentNullException(nameof(driverFacade));
        _optionsExtension = dependencies.ContextOptions.FindExtension<MySqlOptionsExtension>()
            ?? throw new InvalidOperationException("The Doka MySQL options extension is not configured.");
    }

    protected override bool SupportsAmbientTransactions => true;

    public override string? ConnectionString
    {
        get
        {
            var connectionString = base.ConnectionString;

            return _optionsExtension.ConnectionString is not null && connectionString is not null
                ? NormalizeConnectionString(connectionString)
                : connectionString;
        }
        set =>
            base.ConnectionString = _optionsExtension.ConnectionString is not null && value is not null
                ? NormalizeConnectionString(value)
                : value;
    }

    protected override DbConnection CreateDbConnection()
    {
        if (_optionsExtension.DataSource is not null)
        {
            return _optionsExtension.DataSource.CreateConnection();
        }

        if (_optionsExtension.Connection is not null)
        {
            return _optionsExtension.Connection;
        }

        if (string.IsNullOrWhiteSpace(_optionsExtension.ConnectionString))
        {
            throw new InvalidOperationException(
                "A MySQL connection string, DbConnection, or MySqlDataSource must be configured.");
        }

        return _driverFacade.CreateConnection(NormalizeConnectionString(_optionsExtension.ConnectionString));
    }

    protected override DbTransaction ConnectionBeginTransaction(
        IsolationLevel isolationLevel
    ) => base.ConnectionBeginTransaction(NormalizeIsolationLevel(isolationLevel));

    protected override ValueTask<DbTransaction> ConnectionBeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default
    ) => base.ConnectionBeginTransactionAsync(
        NormalizeIsolationLevel(isolationLevel),
        cancellationToken);

    public override void EnlistTransaction(
        System.Transactions.Transaction? transaction
    )
    {
        if (transaction is not null)
        {
            if (CurrentTransaction is not null)
            {
                throw new InvalidOperationException(
                    RelationalStrings.TransactionAlreadyStarted);
            }

            if (CurrentAmbientTransaction is not null)
            {
                throw new InvalidOperationException(
                    RelationalStrings.ConflictingAmbientTransaction);
            }

            if (EnlistedTransaction is not null)
            {
                throw new InvalidOperationException(
                    RelationalStrings.ConflictingEnlistedTransaction);
            }
        }

        // Guard conflicts at the EF Core boundary so callers receive the
        // provider-neutral transaction contract instead of driver-specific
        // exception types and messages.
        base.EnlistTransaction(transaction);
    }

    private string NormalizeConnectionString(
        string connectionString
    )
    {
        var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString)
        {
            GuidFormat = _optionsExtension.DefaultGuidFormat switch
            {
                MySqlGuidFormat.Char36 => MySqlConnector.MySqlGuidFormat.Char36,
                _ => MySqlConnector.MySqlGuidFormat.Binary16,
            },
        };

        return connectionStringBuilder.ConnectionString;
    }

    // MySqlConnector 2.6.1 already emits REPEATABLE READ when callers pass
    // Unspecified, but retains Unspecified on MySqlTransaction.IsolationLevel.
    // Normalize the enum before the driver call so interception and public ADO.NET
    // state report the isolation level that the driver actually sends.
    private static IsolationLevel NormalizeIsolationLevel(
        IsolationLevel isolationLevel
    ) => isolationLevel == IsolationLevel.Unspecified ? IsolationLevel.RepeatableRead : isolationLevel;
}
