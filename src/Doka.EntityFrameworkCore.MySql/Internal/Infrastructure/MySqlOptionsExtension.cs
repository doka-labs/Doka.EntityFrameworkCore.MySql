namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public MySqlDataSource? DataSource { get; private set; }

    public MySqlServerVersion? ServerVersion { get; private set; }

    public MySqlRetryOptions? RetryOptions { get; private set; }

    public MySqlGuidFormat DefaultGuidFormat { get; private set; } = MySqlGuidFormat.Binary16;

    public override DbContextOptionsExtensionInfo Info => _info ??= new MySqlOptionsExtensionInfo(this);

    public MySqlOptionsExtension() { }

    private MySqlOptionsExtension(
        MySqlOptionsExtension copyFrom
    ) : base(copyFrom)
    {
        DataSource = copyFrom.DataSource;
        ServerVersion = copyFrom.ServerVersion;
        RetryOptions = copyFrom.RetryOptions;
        DefaultGuidFormat = copyFrom.DefaultGuidFormat;
    }

    protected override RelationalOptionsExtension Clone() => new MySqlOptionsExtension(this);

    public override void ApplyServices(
        IServiceCollection services
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEntityFrameworkDokaMySql();
    }

    public override void Validate(
        IDbContextOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        base.Validate(options);

        if (ServerVersion is null)
        {
            LogInvalidConfiguration(options, "A MySQL server version must be configured.");
            throw new InvalidOperationException("A MySQL server version must be configured.");
        }

        var configuredInputCount = 0;

        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            configuredInputCount++;
        }

        if (Connection is not null)
        {
            configuredInputCount++;
        }

        if (DataSource is not null)
        {
            configuredInputCount++;
        }

        if (configuredInputCount == 0)
        {
            LogInvalidConfiguration(
                options,
                "A MySQL connection string, DbConnection, or MySqlDataSource must be configured.");
            throw new InvalidOperationException(
                "A MySQL connection string, DbConnection, or MySqlDataSource must be configured.");
        }

        if (configuredInputCount > 1)
        {
            LogInvalidConfiguration(
                options,
                "Configure exactly one MySQL connection path: connection string, DbConnection, or MySqlDataSource.");
            throw new InvalidOperationException(
                "Configure exactly one MySQL connection path: connection string, DbConnection, or MySqlDataSource.");
        }
    }

    public new MySqlOptionsExtension WithConnectionString(
        string connectionString
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var clone = (MySqlOptionsExtension)base.WithConnectionString(connectionString);
        return clone.ResetOtherConnectionPaths(ConnectionPath.ConnectionString);
    }

    public new MySqlOptionsExtension WithConnection(
        DbConnection connection
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        var clone = (MySqlOptionsExtension)base.WithConnection(connection, owned: false);
        return clone.ResetOtherConnectionPaths(ConnectionPath.Connection);
    }

    public MySqlOptionsExtension WithDataSource(
        MySqlDataSource dataSource
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var clone = (MySqlOptionsExtension)Clone();
        clone.DataSource = dataSource;
        return clone.ResetOtherConnectionPaths(ConnectionPath.DataSource);
    }

    public MySqlOptionsExtension WithServerVersion(
        MySqlServerVersion serverVersion
    )
    {
        ArgumentNullException.ThrowIfNull(serverVersion);

        var clone = (MySqlOptionsExtension)Clone();
        clone.ServerVersion = serverVersion;

        return clone;
    }

    public MySqlOptionsExtension WithRetryOptions(
        MySqlRetryOptions retryOptions
    )
    {
        ArgumentNullException.ThrowIfNull(retryOptions);

        var clone = (MySqlOptionsExtension)Clone();
        clone.RetryOptions = retryOptions;

        return clone;
    }

    public MySqlOptionsExtension WithDefaultGuidFormat(
        MySqlGuidFormat defaultGuidFormat
    )
    {
        if (!Enum.IsDefined(defaultGuidFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(defaultGuidFormat));
        }

        var clone = (MySqlOptionsExtension)Clone();
        clone.DefaultGuidFormat = defaultGuidFormat;

        return clone;
    }

    // The three connection-path properties (ConnectionString, Connection, DataSource) are
    // mutex per Validate -- exactly one must be configured. The active path is set by the
    // calling With*-method; this helper nulls out the other two. The base setters accept
    // null when invoked through the RelationalOptionsExtension surface, so the casts route
    // around the provider's public ArgumentNullException-throwing overrides.
    private MySqlOptionsExtension ResetOtherConnectionPaths(
        ConnectionPath keep
    )
    {
        var clone = this;

        if (keep != ConnectionPath.ConnectionString
            && !string.IsNullOrEmpty(ConnectionString))
        {
            clone = (MySqlOptionsExtension)((RelationalOptionsExtension)clone).WithConnectionString(null);
        }

        if (keep != ConnectionPath.Connection
            && Connection is not null)
        {
            clone = (MySqlOptionsExtension)((RelationalOptionsExtension)clone).WithConnection(null, owned: false);
        }

        if (keep != ConnectionPath.DataSource
            && DataSource is not null)
        {
            clone.DataSource = null;
        }

        return clone;
    }

    private enum ConnectionPath
    {
        ConnectionString,
        Connection,
        DataSource,
    }

    private void LogInvalidConfiguration(
        IDbContextOptions options,
        string message
    )
    {
        var loggerFactory = options.FindExtension<CoreOptionsExtension>()
            ?.LoggerFactory;

        if (loggerFactory is null)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger(MySqlLoggerCategory.Configuration);

        MySqlLoggerMessages.InvalidConfiguration(
            logger,
            message,
            GetConnectionPath(),
            MySqlConnectionStringRedactor.Redact(ConnectionString));
    }

    private string GetConnectionPath()
    {
        if (DataSource is not null)
        {
            return nameof(MySqlDataSource);
        }

        if (Connection is not null)
        {
            return nameof(DbConnection);
        }

        return !string.IsNullOrWhiteSpace(ConnectionString) ? "ConnectionString" : "Unconfigured";
    }
}
