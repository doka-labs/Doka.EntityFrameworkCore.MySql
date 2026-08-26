namespace Doka.Caching.MySql;

/// <summary>
/// Provides the versioned deployment-time schema for the Doka MySQL cache.
/// </summary>
public static class MySqlCacheSchema
{
    /// <summary>
    /// Gets the current Doka cache schema version.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Creates an idempotent SQL script for the current cache schema.
    /// The application runtime never executes this script automatically.
    /// </summary>
    /// <param name="schemaName">The existing database schema.</param>
    /// <param name="tableName">The cache table name.</param>
    /// <returns>The deployment-time SQL script.</returns>
    public static string GetCreateScript(
        string schemaName,
        string tableName
    )
    {
        var qualifiedTableName = MySqlCacheIdentifier.GetQualifiedName(schemaName, tableName);

        return $"""
                CREATE TABLE IF NOT EXISTS {qualifiedTableName} (
                    `Id` varbinary(1024) NOT NULL,
                    `Value` longblob NOT NULL,
                    `ExpiresAtUtc` datetime(6) NOT NULL,
                    `SlidingExpirationMicroseconds` bigint NULL,
                    `AbsoluteExpirationUtc` datetime(6) NULL,
                    `Revision` bigint NOT NULL,
                    CONSTRAINT `PK_DokaCache` PRIMARY KEY (`Id`),
                    INDEX `IX_DokaCache_ExpiresAtUtc` (`ExpiresAtUtc`)
                ) ENGINE=InnoDB COMMENT='Doka.Caching.MySql schema version {Version}';
                """;
    }
}
