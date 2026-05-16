namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlSingletonOptions : ISingletonOptions
{
    private readonly Lock _initLock = new();
    private volatile bool _initialized;

    public MySqlServerVersion? ServerVersion { get; private set; }

    public ServerCapabilities? Capabilities { get; private set; }

    public MySqlRetryOptions? RetryOptions { get; private set; }

    public MySqlGuidFormat DefaultGuidFormat { get; private set; } = MySqlGuidFormat.Binary16;

    public bool UsesDataSource { get; private set; }

    internal ILogger? ProviderLogger { get; private set; }

    internal ILogger? ResilienceLogger { get; private set; }

    /// <summary>
    /// Materializes the configuration snapshot from the supplied options. Idempotent
    /// and thread-safe: under <c>AddDbContextPool</c> the framework can resolve
    /// singleton services from multiple threads before the first Initialize completes;
    /// without the double-checked-lock guard a consumer could observe a torn snapshot
    /// where ServerVersion is set but Capabilities is still null. Subsequent calls
    /// return without touching state.
    /// </summary>
    public void Initialize(
        IDbContextOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_initialized)
        {
            return;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            var extension = options.FindExtension<MySqlOptionsExtension>()
                ?? throw new InvalidOperationException("The Doka MySQL options extension is not configured.");

            ServerVersion = extension.ServerVersion
                ?? throw new InvalidOperationException("A MySQL server version must be configured.");
            Capabilities = extension.ServerVersion.Capabilities;
            RetryOptions = extension.RetryOptions;
            DefaultGuidFormat = extension.DefaultGuidFormat;
            UsesDataSource = extension.DataSource is not null;

            var loggerFactory = options.FindExtension<CoreOptionsExtension>()
                ?.LoggerFactory;

            if (loggerFactory is not null)
            {
                ProviderLogger = loggerFactory.CreateLogger(MySqlLoggerCategory.Configuration);
                ResilienceLogger = loggerFactory.CreateLogger(MySqlLoggerCategory.Resilience);
                MySqlLoggerMessages.ServerVersionResolved(ProviderLogger, extension.ServerVersion);
            }

            // volatile-write: publishes every property write above to any thread that
            // subsequently observes _initialized == true through the fast-path check.
            _initialized = true;
        }
    }

    public void Validate(
        IDbContextOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        var extension = options.FindExtension<MySqlOptionsExtension>()
            ?? throw new InvalidOperationException("The Doka MySQL options extension is not configured.");

        if (!Equals(ServerVersion, extension.ServerVersion))
        {
            LogConfigurationMismatch(
                options,
                "The configured MySQL server version changed for the shared service provider.");
            throw new InvalidOperationException(
                "The configured MySQL server version changed for the shared service provider.");
        }

        if (!Equals(RetryOptions, extension.RetryOptions))
        {
            LogConfigurationMismatch(
                options,
                "The configured MySQL retry settings changed for the shared service provider.");
            throw new InvalidOperationException(
                "The configured MySQL retry settings changed for the shared service provider.");
        }

        if (DefaultGuidFormat != extension.DefaultGuidFormat)
        {
            LogConfigurationMismatch(
                options,
                "The configured MySQL GUID format changed for the shared service provider.");
            throw new InvalidOperationException(
                "The configured MySQL GUID format changed for the shared service provider.");
        }

        if (UsesDataSource != (extension.DataSource is not null))
        {
            LogConfigurationMismatch(
                options,
                "The configured MySQL connection path changed for the shared service provider.");
            throw new InvalidOperationException(
                "The configured MySQL connection path changed for the shared service provider.");
        }
    }

    private void LogConfigurationMismatch(
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

        MySqlLoggerMessages.InvalidConfiguration(
            loggerFactory.CreateLogger(MySqlLoggerCategory.Configuration),
            message,
            UsesDataSource ? nameof(MySqlDataSource) : "ConnectionStringOrDbConnection",
            "<not-logged>");
    }
}
