namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlRelationalConnection : RelationalConnection
{
    private readonly IMySqlDriverFacade _driverFacade;
    private readonly MySqlOptionsExtension _optionsExtension;
    private bool _activeConnectionPathIsBorrowed;
    private bool _connectionStringOverridden;
    private string? _runtimeConnectionString;

    public MySqlRelationalConnection(
        RelationalConnectionDependencies dependencies,
        IMySqlDriverFacade driverFacade
    ) : base(dependencies)
    {
        _driverFacade = driverFacade ?? throw new ArgumentNullException(nameof(driverFacade));
        _optionsExtension = dependencies.ContextOptions.FindExtension<MySqlOptionsExtension>()
            ?? throw new InvalidOperationException("The Doka MySQL options extension is not configured.");

        _activeConnectionPathIsBorrowed = _optionsExtension.Connection is not null
            || _optionsExtension.DataSource is not null;

        if (!_activeConnectionPathIsBorrowed
            && base.ConnectionString is { } connectionString)
        {
            try
            {
                base.ConnectionString = NormalizeConnectionString(connectionString);
            }
            catch (MySqlConnectionContractException exception)
            {
                _optionsExtension.LogInvalidConfiguration(
                    dependencies.ContextOptions,
                    exception.Reason,
                    "ConnectionString");
                throw;
            }
        }
    }

    protected override bool SupportsAmbientTransactions => true;

    public override string? ConnectionString
    {
        get => _connectionStringOverridden ? _runtimeConnectionString : base.ConnectionString;
        set
        {
            if (_activeConnectionPathIsBorrowed)
            {
                _optionsExtension.LogInvalidConfiguration(
                    Dependencies.ContextOptions,
                    MySqlConfigurationFailureReason.BorrowedConnectionStringMutation,
                    _optionsExtension.DataSource is null ? nameof(DbConnection) : nameof(MySqlDataSource));

                throw new InvalidOperationException(
                    "Database.SetConnectionString cannot replace caller-owned MySQL connection configuration.");
            }

            string? normalizedConnectionString = null;

            if (value is not null)
            {
                try
                {
                    normalizedConnectionString = NormalizeConnectionString(value);
                }
                catch (MySqlConnectionContractException exception)
                {
                    _optionsExtension.LogInvalidConfiguration(
                        Dependencies.ContextOptions,
                        exception.Reason,
                        "ConnectionString");
                    throw;
                }
            }

            base.ConnectionString = normalizedConnectionString;
            _runtimeConnectionString = value;
            _connectionStringOverridden = true;
        }
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

        var connectionString = base.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A MySQL connection string, DbConnection, or MySqlDataSource must be configured.");
        }

        return _driverFacade.CreateConnection(connectionString);
    }

    public override void SetDbConnection(
        DbConnection? value,
        bool contextOwnsConnection
    )
    {
        if (value is not null)
        {
            try
            {
                MySqlConnectionContract.ValidateBorrowed(value, _optionsExtension.UserVariablesRequired);
            }
            catch (MySqlConnectionContractException exception)
            {
                _optionsExtension.LogInvalidConfiguration(
                    Dependencies.ContextOptions,
                    exception.Reason,
                    nameof(DbConnection));
                throw;
            }
        }

        base.SetDbConnection(value, contextOwnsConnection);

        _activeConnectionPathIsBorrowed = value is not null
            || _optionsExtension.Connection is not null
            || _optionsExtension.DataSource is not null;

        _runtimeConnectionString = null;
        _connectionStringOverridden = false;
    }

    /// <summary>
    /// Creates one lifecycle connection without reducing an object-based
    /// configuration to its serializable connection string.
    /// </summary>
    internal MySqlLifecycleConnection CreateLifecycleConnection(
        string connectionString
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (_optionsExtension.DataSource is not null)
        {
            var configuredConnection = _optionsExtension.DataSource.CreateConnection();

            if (string.Equals(configuredConnection.ConnectionString, connectionString, StringComparison.Ordinal))
            {
                return MySqlLifecycleConnection.Own(configuredConnection);
            }

            try
            {
                return MySqlLifecycleConnection.Own(configuredConnection.CloneWith(connectionString));
            }
            finally
            {
                configuredConnection.Dispose();
            }
        }

        if (_optionsExtension.Connection is MySqlConnection mySqlConnection)
        {
            return MySqlLifecycleConnection.Own(mySqlConnection.CloneWith(connectionString));
        }

        return _optionsExtension.Connection is not null
            // A provider cannot manufacture an equivalent arbitrary
            // DbConnection. Borrow the configured object so its custom TLS,
            // authentication, interception, and command behavior remain active.
            ? MySqlLifecycleConnection.Borrow(_optionsExtension.Connection, connectionString)
            : MySqlLifecycleConnection.Own(
                _driverFacade.CreateConnection(NormalizeConnectionString(connectionString)));
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
    ) => MySqlConnectionContract.NormalizeProviderOwned(
        connectionString,
        _optionsExtension.UserVariablesRequired);

    // MySqlConnector already emits REPEATABLE READ when callers pass
    // Unspecified, but retains Unspecified on MySqlTransaction.IsolationLevel.
    // Normalize the enum before the driver call so interception and public ADO.NET
    // state report the isolation level that the driver actually sends.
    private static IsolationLevel NormalizeIsolationLevel(
        IsolationLevel isolationLevel
    ) => isolationLevel == IsolationLevel.Unspecified ? IsolationLevel.RepeatableRead : isolationLevel;
}
