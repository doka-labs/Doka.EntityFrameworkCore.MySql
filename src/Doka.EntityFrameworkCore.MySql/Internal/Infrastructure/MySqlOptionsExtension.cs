namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Stores the immutable provider options consumed by EF Core infrastructure.
/// </summary>
/// <remarks>
/// This type is public because EF Core's standard relational options-builder base includes
/// its extension type in the public generic signature. Application code should configure it
/// through <see cref="MySqlDbContextOptionsBuilder"/> instead of constructing it directly.
/// </remarks>
public sealed partial class MySqlOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    internal MySqlDataSource? DataSource { get; private set; }

    internal MySqlServerVersion? ServerVersion { get; private set; }

    internal MySqlRetryOptions? RetryOptions { get; private set; }

    internal MySqlGuidFormat DefaultGuidFormat { get; private set; } = MySqlGuidFormat.Binary16;

    /// <inheritdoc />
    public override DbContextOptionsExtensionInfo Info => _info ??= new MySqlOptionsExtensionInfo(this);

    /// <summary>
    /// Creates an empty provider-options snapshot for EF Core infrastructure.
    /// </summary>
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

    /// <inheritdoc />
    protected override RelationalOptionsExtension Clone() => new MySqlOptionsExtension(this);

    /// <inheritdoc />
    public override void ApplyServices(
        IServiceCollection services
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEntityFrameworkDokaMySql();
    }

    /// <inheritdoc />
    public override void Validate(
        IDbContextOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        base.Validate(options);

        if (ServerVersion is null)
        {
            LogInvalidConfiguration(
                options,
                MySqlConfigurationFailureReason.MissingServerVersion);

            throw new InvalidOperationException("A MySQL server version must be configured.");
        }

        if (ServerVersion.SupportStatus != MySqlServerVersionSupportStatus.Supported
            && ServerVersion.CompatibilityMode != MySqlServerVersionCompatibilityMode.AllowUnsupported)
        {
            var message = ServerVersionSupportPolicy.CreateRejectionMessage(ServerVersion);

            LogInvalidConfiguration(
                options,
                MySqlConfigurationFailureReason.UnsupportedServerVersion);

            throw new NotSupportedException(message);
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
                MySqlConfigurationFailureReason.MissingConnectionPath);

            throw new InvalidOperationException(
                "A MySQL connection string, DbConnection, or MySqlDataSource must be configured.");
        }

        if (configuredInputCount > 1)
        {
            LogInvalidConfiguration(
                options,
                MySqlConfigurationFailureReason.ConflictingConnectionPaths);

            throw new InvalidOperationException(
                "Configure exactly one MySQL connection path: connection string, DbConnection, or MySqlDataSource.");
        }
    }

    internal new MySqlOptionsExtension WithConnectionString(
        string connectionString
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var clone = (MySqlOptionsExtension)base.WithConnectionString(connectionString);
        return clone.ResetOtherConnectionPaths(ConnectionPath.ConnectionString);
    }

    internal new MySqlOptionsExtension WithConnection(
        DbConnection connection
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        var clone = (MySqlOptionsExtension)base.WithConnection(connection, owned: false);
        return clone.ResetOtherConnectionPaths(ConnectionPath.Connection);
    }

    internal MySqlOptionsExtension WithDataSource(
        MySqlDataSource dataSource
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var clone = (MySqlOptionsExtension)Clone();
        clone.DataSource = dataSource;
        return clone.ResetOtherConnectionPaths(ConnectionPath.DataSource);
    }

    internal MySqlOptionsExtension WithServerVersion(
        MySqlServerVersion serverVersion
    )
    {
        ArgumentNullException.ThrowIfNull(serverVersion);

        var clone = (MySqlOptionsExtension)Clone();
        clone.ServerVersion = serverVersion;

        return clone;
    }

    internal MySqlOptionsExtension WithRetryOptions(
        MySqlRetryOptions retryOptions
    )
    {
        ArgumentNullException.ThrowIfNull(retryOptions);

        var clone = (MySqlOptionsExtension)Clone();
        clone.RetryOptions = retryOptions;

        return clone;
    }

    internal MySqlOptionsExtension WithDefaultGuidFormat(
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
        MySqlConfigurationFailureReason reason
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
            reason,
            GetConnectionPath());
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
