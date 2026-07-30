namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides access to provider-specific configuration options for <c>UseMySql(...)</c>.
/// </summary>
public sealed class MySqlDbContextOptionsBuilder
    : RelationalDbContextOptionsBuilder<MySqlDbContextOptionsBuilder, MySqlOptionsExtension>
{
    /// <summary>
    /// Creates a provider builder over the supplied EF Core options builder.
    /// </summary>
    /// <param name="optionsBuilder">The core options builder to configure.</param>
    public MySqlDbContextOptionsBuilder(
        DbContextOptionsBuilder optionsBuilder
    ) : base(optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
    }

    internal DbContextOptionsBuilder InfrastructureOptionsBuilder => OptionsBuilder;

    /// <summary>
    /// Enables the opt-in retry configuration surface for the provider.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retry attempts.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder EnableRetryOnFailure(
        int maxRetryCount = MySqlRetryOptions.DefaultMaxRetryCount,
        TimeSpan? maxRetryDelay = null
    ) => WithOption(currentExtension =>
        currentExtension.WithRetryOptions(MySqlRetryOptions.Create(maxRetryCount, maxRetryDelay)));

    /// <summary>
    /// Configures the default provider-level GUID storage format.
    /// </summary>
    /// <param name="format">The default GUID storage format.</param>
    /// <returns>The current builder instance.</returns>
    public MySqlDbContextOptionsBuilder DefaultGuidFormat(
        MySqlGuidFormat format
    ) => Enum.IsDefined(format)
        ? WithOption(currentExtension => currentExtension.WithDefaultGuidFormat(format))
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
        return WithOption(currentExtension =>
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
    public override MySqlDbContextOptionsBuilder MaxBatchSize(
        int maxBatchSize
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBatchSize);
        return WithOption(currentExtension =>
            (MySqlOptionsExtension)currentExtension.WithMaxBatchSize(maxBatchSize));
    }

    /// <summary>
    /// Configures the minimum number of statements that must accumulate before the
    /// provider flushes a modification-command batch. Values smaller than this
    /// threshold fall back to per-command execution.
    /// </summary>
    /// <param name="minBatchSize">Minimum number of statements per batch; must be positive.</param>
    /// <returns>The current builder instance.</returns>
    public override MySqlDbContextOptionsBuilder MinBatchSize(
        int minBatchSize
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minBatchSize);
        return WithOption(currentExtension =>
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
    public override MySqlDbContextOptionsBuilder MigrationsHistoryTable(
        string tableName,
        string? schema = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return WithOption(currentExtension =>
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
    public override MySqlDbContextOptionsBuilder UseQuerySplittingBehavior(
        QuerySplittingBehavior querySplittingBehavior
    )
    {
        if (!Enum.IsDefined(querySplittingBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(querySplittingBehavior));
        }

        return WithOption(currentExtension =>
            (MySqlOptionsExtension)currentExtension.WithUseQuerySplittingBehavior(querySplittingBehavior));
    }
}
