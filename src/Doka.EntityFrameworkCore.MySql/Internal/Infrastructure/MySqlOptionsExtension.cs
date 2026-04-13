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
        clone.DataSource = null;

        return clone;
    }

    public new MySqlOptionsExtension WithConnection(
        DbConnection connection
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        var clone = (MySqlOptionsExtension)base.WithConnection(connection, owned: false);
        clone.DataSource = null;

        return clone;
    }

    public MySqlOptionsExtension WithDataSource(
        MySqlDataSource dataSource
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var clone = CopyRelationalOptionsTo(new MySqlOptionsExtension());
        clone.DataSource = dataSource;
        clone.ServerVersion = ServerVersion;
        clone.RetryOptions = RetryOptions;
        clone.DefaultGuidFormat = DefaultGuidFormat;

        return clone;
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

    private MySqlOptionsExtension CopyRelationalOptionsTo(
        MySqlOptionsExtension target
    )
    {
        var clone = target;

        if (CommandTimeout is not null)
        {
            clone = (MySqlOptionsExtension)clone.WithCommandTimeout(CommandTimeout);
        }

        if (MaxBatchSize is not null)
        {
            clone = (MySqlOptionsExtension)clone.WithMaxBatchSize(MaxBatchSize);
        }

        if (MinBatchSize is not null)
        {
            clone = (MySqlOptionsExtension)clone.WithMinBatchSize(MinBatchSize);
        }

        if (UseRelationalNulls)
        {
            clone = (MySqlOptionsExtension)clone.WithUseRelationalNulls(true);
        }

        if (QuerySplittingBehavior is not null)
        {
            clone = (MySqlOptionsExtension)clone.WithUseQuerySplittingBehavior(QuerySplittingBehavior.Value);
        }

        if (MigrationsAssemblyObject is not null)
        {
            clone = (MySqlOptionsExtension)clone.WithMigrationsAssembly(MigrationsAssemblyObject);
        }
        else if (!string.IsNullOrWhiteSpace(MigrationsAssembly))
        {
            clone = (MySqlOptionsExtension)clone.WithMigrationsAssembly(MigrationsAssembly);
        }

        if (!string.IsNullOrWhiteSpace(MigrationsHistoryTableName))
        {
            clone = (MySqlOptionsExtension)clone.WithMigrationsHistoryTableName(MigrationsHistoryTableName);
        }

        if (!string.IsNullOrWhiteSpace(MigrationsHistoryTableSchema))
        {
            clone = (MySqlOptionsExtension)clone.WithMigrationsHistoryTableSchema(MigrationsHistoryTableSchema);
        }

        if (ExecutionStrategyFactory is not null)
        {
            clone = (MySqlOptionsExtension)clone.WithExecutionStrategyFactory(ExecutionStrategyFactory);
        }

        clone = (MySqlOptionsExtension)clone.WithUseParameterizedCollectionMode(ParameterizedCollectionMode);

        return clone;
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

        return !string.IsNullOrWhiteSpace(ConnectionString)
            ? "ConnectionString"
            : "Unconfigured";
    }
}
