namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlSingletonOptions : ISingletonOptions
{
    private readonly Lock _initLock = new();
    private volatile bool _initialized;

    public ProviderProfile? Profile { get; private set; }

    public MySqlRetryOptions? RetryOptions { get; private set; }

    public MySqlGuidFormat DefaultGuidFormat { get; private set; } = MySqlGuidFormat.Binary16;

    public bool UsesDataSource { get; private set; }

    /// <summary>
    /// Materializes the configuration snapshot from the supplied options. Idempotent
    /// and thread-safe: under <c>AddDbContextPool</c> the framework can resolve
    /// singleton services from multiple threads before the first Initialize completes;
    /// without the double-checked-lock guard a consumer could observe a torn snapshot
    /// where the profile is set but retry or format settings are still stale.
    /// Subsequent calls
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

            var serverVersion = extension.ServerVersion
                ?? throw new InvalidOperationException("A MySQL server version must be configured.");

            using var activity = MySqlActivitySource.StartServerVersionResolve(serverVersion.Profile.Engine.Family);

            Profile = serverVersion.Profile;
            RetryOptions = extension.RetryOptions;
            DefaultGuidFormat = extension.DefaultGuidFormat;
            UsesDataSource = extension.DataSource is not null;

            activity?.SetTag(MySqlDiagnosticTags.EngineFamilyName, Profile.Engine.Family.ToString());
            activity?.SetTag(MySqlDiagnosticTags.ServerVersion, Profile.Engine.Version.ToString());
            activity?.SetTag(MySqlDiagnosticTags.SupportStatus, serverVersion.SupportStatus.ToString());
            activity?.SetTag(MySqlDiagnosticTags.CompatibilityMode, serverVersion.CompatibilityMode.ToString());

            MySqlMeter.ServerVersionResolutionTotal.Add(
                1,
                MySqlDiagnosticTags.CreateEngineMetricTag(Profile.Engine.Family),
                new KeyValuePair<string, object?>(
                    MySqlDiagnosticTags.MetricSupportStatus,
                    serverVersion.SupportStatus.ToString()),
                new KeyValuePair<string, object?>(
                    MySqlDiagnosticTags.MetricCompatibilityMode,
                    serverVersion.CompatibilityMode.ToString()));

            var loggerFactory = options.FindExtension<CoreOptionsExtension>()
                ?.LoggerFactory;

            if (loggerFactory is not null)
            {
                var logger = loggerFactory.CreateLogger(MySqlLoggerCategory.Configuration);

                MySqlLoggerMessages.ServerVersionResolved(logger, serverVersion);

                if (serverVersion.SupportStatus != MySqlServerVersionSupportStatus.Supported
                    && serverVersion.CompatibilityMode == MySqlServerVersionCompatibilityMode.AllowUnsupported)
                {
                    MySqlLoggerMessages.UnsupportedServerVersion(logger, serverVersion);
                }
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

        if (!Equals(Profile, extension.ServerVersion?.Profile))
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
