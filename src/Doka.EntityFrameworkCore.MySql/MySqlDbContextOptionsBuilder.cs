namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides access to provider-specific configuration options for <c>UseMySql(...)</c>.
/// </summary>
public sealed class MySqlDbContextOptionsBuilder
{
    private readonly DbContextOptionsBuilder _optionsBuilder;

    /// <summary>
    /// The current options extension snapshot. This follows the standard EF Core mutable-extension
    /// pattern: the initial constructor value is a snapshot that is replaced on the first fluent
    /// call via <see cref="UpdateExtension"/>. Each fluent method clones the extension, mutates
    /// the clone, and re-registers it on the options builder.
    /// </summary>
    private MySqlOptionsExtension _extension;

    internal DbContextOptionsBuilder OptionsBuilder => _optionsBuilder;

    internal MySqlDbContextOptionsBuilder(
        DbContextOptionsBuilder optionsBuilder,
        MySqlOptionsExtension extension
    )
    {
        _optionsBuilder = optionsBuilder ?? throw new ArgumentNullException(nameof(optionsBuilder));
        _extension = extension ?? throw new ArgumentNullException(nameof(extension));
    }

    /// <summary>
    /// Enables the opt-in retry configuration surface for the provider.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retry attempts.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder EnableRetryOnFailure(
        int maxRetryCount = MySqlRetryOptions.DefaultMaxRetryCount,
        TimeSpan? maxRetryDelay = null
    ) => UpdateExtension(currentExtension =>
        currentExtension.WithRetryOptions(MySqlRetryOptions.Create(maxRetryCount, maxRetryDelay)));

    /// <summary>
    /// Configures the default provider-level GUID storage format.
    /// </summary>
    /// <param name="format">The default GUID storage format.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder DefaultGuidFormat(
        MySqlGuidFormat format
    ) => Enum.IsDefined(format)
        ? UpdateExtension(currentExtension => currentExtension.WithDefaultGuidFormat(format))
        : throw new ArgumentOutOfRangeException(nameof(format));

    /// <summary>
    /// Configures the default command timeout (in seconds) the provider applies to every
    /// command it issues, including reads, writes, migrations, and the migration advisory
    /// lock acquire. The runtime translates the value via MySqlConnector's
    /// <c>DefaultCommandTimeout</c> connection-string parameter.
    /// </summary>
    /// <param name="commandTimeout">Command timeout in seconds; must be positive.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder CommandTimeout(
        int commandTimeout
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandTimeout);
        return UpdateExtension(currentExtension =>
            (MySqlOptionsExtension)currentExtension.WithCommandTimeout(commandTimeout));
    }

    /// <summary>
    /// Configures the maximum number of statements the provider packs into a single
    /// modification-command batch. The MySQL hard ceiling is 65535 placeholders + the
    /// negotiated <c>max_allowed_packet</c>; the provider further splits batches per
    /// <see cref="MySqlEventId.BulkInsertParameterCountCapped"/> when either ceiling
    /// would be exceeded.
    /// </summary>
    /// <param name="maxBatchSize">Maximum number of statements per batch; must be positive.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder MaxBatchSize(
        int maxBatchSize
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBatchSize);
        return UpdateExtension(currentExtension =>
            (MySqlOptionsExtension)currentExtension.WithMaxBatchSize(maxBatchSize));
    }

    /// <summary>
    /// Configures the minimum number of statements that must accumulate before the
    /// provider flushes a modification-command batch. Values smaller than this
    /// threshold fall back to per-command execution.
    /// </summary>
    /// <param name="minBatchSize">Minimum number of statements per batch; must be positive.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder MinBatchSize(
        int minBatchSize
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minBatchSize);
        return UpdateExtension(currentExtension =>
            (MySqlOptionsExtension)currentExtension.WithMinBatchSize(minBatchSize));
    }

    /// <summary>
    /// Configures the migrations history table the provider creates and reads to track
    /// applied migrations. The <paramref name="schema"/> argument is preserved on the
    /// extension snapshot, but the MySQL engine treats schema and database as synonyms
    /// so the value has no effect on the emitted DDL.
    /// </summary>
    /// <param name="tableName">The history table name; must not be null or whitespace.</param>
    /// <param name="schema">Reserved for engines that distinguish schema from database; ignored by MySQL.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder MigrationsHistoryTable(
        string tableName,
        string? schema = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return UpdateExtension(currentExtension =>
        {
            var withTable = (MySqlOptionsExtension)currentExtension.WithMigrationsHistoryTableName(tableName);
            return (MySqlOptionsExtension)withTable.WithMigrationsHistoryTableSchema(schema);
        });
    }

    /// <summary>
    /// Configures the default <see cref="QuerySplittingBehavior"/> the provider applies
    /// to queries with collection includes. The EF Core default is
    /// <see cref="QuerySplittingBehavior.SingleQuery"/>; consumers that prefer per-include
    /// round-trips opt into <see cref="QuerySplittingBehavior.SplitQuery"/> here once
    /// rather than per query call.
    /// </summary>
    /// <param name="querySplittingBehavior">The query splitting behavior to apply.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder UseQuerySplittingBehavior(
        QuerySplittingBehavior querySplittingBehavior
    )
    {
        if (!Enum.IsDefined(querySplittingBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(querySplittingBehavior));
        }

        return UpdateExtension(currentExtension =>
            (MySqlOptionsExtension)currentExtension.WithUseQuerySplittingBehavior(querySplittingBehavior));
    }

    private MySqlDbContextOptionsBuilder UpdateExtension(
        Func<MySqlOptionsExtension, MySqlOptionsExtension> update
    )
    {
        _extension = update(_extension);
        ((IDbContextOptionsBuilderInfrastructure)_optionsBuilder).AddOrUpdateExtension(_extension);

        return this;
    }
}
