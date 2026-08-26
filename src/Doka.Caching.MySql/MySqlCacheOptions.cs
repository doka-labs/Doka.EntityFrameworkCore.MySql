namespace Doka.Caching.MySql;

/// <summary>
/// Configures the MySQL-backed distributed cache.
/// </summary>
public sealed class MySqlCacheOptions
{
    /// <summary>
    /// Gets or sets the MySQL connection string used by the cache when <see cref="DataSource"/> is not supplied.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a caller-owned MySQL data source used instead of <see cref="ConnectionString"/>.
    /// The data source must disable automatic ambient transaction enlistment with <c>AutoEnlist=false</c>.
    /// The caller must keep it alive until the cache is disposed; the cache never disposes a supplied data source.
    /// </summary>
    public MySqlDataSource? DataSource { get; set; }

    /// <summary>
    /// Gets or sets the database schema containing the cache table.
    /// </summary>
    public string SchemaName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cache table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sliding expiration used when an entry declares no
    /// absolute or sliding expiration.
    /// </summary>
    public TimeSpan DefaultSlidingExpiration { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Gets or sets the interval after an expired-item cleanup drains the backlog or fails.
    /// A full batch leaves cleanup due for the next cache operation; each operation processes at most one batch.
    /// </summary>
    public TimeSpan ExpiredItemsDeletionInterval { get; set; } = TimeSpan.FromMinutes(30);
}
