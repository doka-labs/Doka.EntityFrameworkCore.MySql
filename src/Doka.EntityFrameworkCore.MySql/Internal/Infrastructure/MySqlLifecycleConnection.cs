namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Owns or temporarily borrows one connection used by database lifecycle
/// operations while preserving the configured connection object's state.
/// </summary>
internal sealed class MySqlLifecycleConnection : IDisposable, IAsyncDisposable
{
    private readonly bool _ownsConnection;
    private readonly string? _originalConnectionString;
    private readonly bool _restoreConnectionString;
    private bool _openedConnection;
    private bool _disposed;

    private MySqlLifecycleConnection(
        DbConnection connection,
        bool ownsConnection,
        string? originalConnectionString,
        bool restoreConnectionString
    )
    {
        Connection = connection;
        _ownsConnection = ownsConnection;
        _originalConnectionString = originalConnectionString;
        _restoreConnectionString = restoreConnectionString;
    }

    /// <summary>
    /// Gets the callback-preserving connection used by the lifecycle command.
    /// </summary>
    public DbConnection Connection { get; }

    /// <summary>
    /// Creates a lease that owns and disposes a provider-created connection.
    /// </summary>
    public static MySqlLifecycleConnection Own(
        DbConnection connection
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new MySqlLifecycleConnection(
            connection,
            ownsConnection: true,
            originalConnectionString: null,
            restoreConnectionString: false);
    }

    /// <summary>
    /// Creates a lease over a caller-owned custom connection and restores any
    /// temporary server-scoped connection string when the operation completes.
    /// </summary>
    public static MySqlLifecycleConnection Borrow(
        DbConnection connection,
        string connectionString
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var originalConnectionString = connection.ConnectionString;
        var restoreConnectionString = connection.State == ConnectionState.Closed
            && !string.Equals(originalConnectionString, connectionString, StringComparison.Ordinal);

        if (restoreConnectionString)
        {
            connection.ConnectionString = connectionString;
        }

        return new MySqlLifecycleConnection(
            connection,
            ownsConnection: false,
            originalConnectionString,
            restoreConnectionString);
    }

    /// <summary>
    /// Opens the connection only when the lease did not receive an already-open
    /// caller-owned connection.
    /// </summary>
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Connection.State != ConnectionState.Closed)
        {
            return;
        }

        _openedConnection = true;
        Connection.Open();
    }

    /// <summary>
    /// Asynchronously opens the connection only when required by this lease.
    /// </summary>
    public async Task OpenAsync(
        CancellationToken cancellationToken
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Connection.State != ConnectionState.Closed)
        {
            return;
        }

        _openedConnection = true;
        await Connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_ownsConnection)
            {
                Connection.Dispose();
            }
            else if (_openedConnection)
            {
                Connection.Close();
            }
        }
        finally
        {
            RestoreBorrowedConnectionString();
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_ownsConnection)
            {
                await Connection
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            else if (_openedConnection)
            {
                await Connection
                    .CloseAsync()
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            RestoreBorrowedConnectionString();
            _disposed = true;
        }
    }

    private void RestoreBorrowedConnectionString()
    {
        if (_restoreConnectionString)
        {
            Connection.ConnectionString = _originalConnectionString;
        }
    }
}
