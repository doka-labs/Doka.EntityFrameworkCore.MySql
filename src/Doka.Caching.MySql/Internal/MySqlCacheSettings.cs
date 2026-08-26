namespace Doka.Caching.MySql;

internal sealed class MySqlCacheSettings
{
    public MySqlCacheSettings(
        MySqlCacheOptions options
    )
    {
        DataSource = options.DataSource;
        ConnectionString = DataSource?.ConnectionString ?? new MySqlConnectionStringBuilder(options.ConnectionString)
        {
            AutoEnlist = false,
        }.ConnectionString;

        QualifiedTableName = MySqlCacheIdentifier.GetQualifiedName(options.SchemaName, options.TableName);
        DefaultSlidingExpirationMicroseconds = MySqlCacheExpiration.ToMicroseconds(
            options.DefaultSlidingExpiration,
            nameof(options.DefaultSlidingExpiration));

        ExpiredItemsDeletionInterval = options.ExpiredItemsDeletionInterval;
    }

    public string ConnectionString { get; }
    public MySqlDataSource? DataSource { get; }
    public string QualifiedTableName { get; }
    public long DefaultSlidingExpirationMicroseconds { get; }
    public TimeSpan ExpiredItemsDeletionInterval { get; }
}
