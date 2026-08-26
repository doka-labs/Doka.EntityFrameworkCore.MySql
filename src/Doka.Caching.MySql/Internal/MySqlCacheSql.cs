namespace Doka.Caching.MySql;

internal sealed class MySqlCacheSql
{
    public MySqlCacheSql(
        string qualifiedTableName
    )
    {
        const string absoluteExpiration = """
                                          CASE
                                              WHEN @absoluteExpirationUtc IS NOT NULL THEN @absoluteExpirationUtc
                                              WHEN @absoluteExpirationRelativeMicroseconds IS NOT NULL
                                                  THEN TIMESTAMPADD(MICROSECOND, @absoluteExpirationRelativeMicroseconds, UTC_TIMESTAMP(6))
                                              ELSE NULL
                                          END
                                          """;

        const string expiration = """
                                  CASE
                                      WHEN @absoluteExpirationUtc IS NOT NULL AND @slidingExpirationMicroseconds IS NOT NULL
                                          THEN LEAST(@absoluteExpirationUtc, TIMESTAMPADD(MICROSECOND, @slidingExpirationMicroseconds, UTC_TIMESTAMP(6)))
                                      WHEN @absoluteExpirationUtc IS NOT NULL THEN @absoluteExpirationUtc
                                      WHEN @absoluteExpirationRelativeMicroseconds IS NOT NULL AND @slidingExpirationMicroseconds IS NOT NULL
                                          THEN TIMESTAMPADD(MICROSECOND, LEAST(@absoluteExpirationRelativeMicroseconds, @slidingExpirationMicroseconds), UTC_TIMESTAMP(6))
                                      WHEN @absoluteExpirationRelativeMicroseconds IS NOT NULL
                                          THEN TIMESTAMPADD(MICROSECOND, @absoluteExpirationRelativeMicroseconds, UTC_TIMESTAMP(6))
                                      ELSE TIMESTAMPADD(MICROSECOND, @slidingExpirationMicroseconds, UTC_TIMESTAMP(6))
                                  END
                                  """;

        Set = $"""
               INSERT INTO {qualifiedTableName}
                   (`Id`, `Value`, `ExpiresAtUtc`, `SlidingExpirationMicroseconds`, `AbsoluteExpirationUtc`, `Revision`)
               SELECT * FROM
               (
                   SELECT CAST(@key AS BINARY) AS `NewId`,
                       @value AS `NewValue`,
                       {expiration} AS `NewExpiresAtUtc`,
                       @slidingExpirationMicroseconds AS `NewSlidingExpirationMicroseconds`,
                       {absoluteExpiration} AS `NewAbsoluteExpirationUtc`,
                       @revision AS `NewRevision`
               ) AS `incoming`
               ON DUPLICATE KEY UPDATE
                   `Value` = `incoming`.`NewValue`,
                   `ExpiresAtUtc` = `incoming`.`NewExpiresAtUtc`,
                   `SlidingExpirationMicroseconds` = `incoming`.`NewSlidingExpirationMicroseconds`,
                   `AbsoluteExpirationUtc` = `incoming`.`NewAbsoluteExpirationUtc`,
                   `Revision` = `incoming`.`NewRevision`;
               """;

        Get = $"""
               SELECT `Revision`, `SlidingExpirationMicroseconds`, `Value`
               FROM {qualifiedTableName}
               WHERE `Id` = CAST(@key AS BINARY)
                   AND `ExpiresAtUtc` > UTC_TIMESTAMP(6)
               LIMIT 1;
               """;

        var refresh = $"""
                       UPDATE {qualifiedTableName}
                       SET `ExpiresAtUtc` = CASE
                           WHEN `AbsoluteExpirationUtc` IS NULL
                               THEN TIMESTAMPADD(MICROSECOND, `SlidingExpirationMicroseconds`, UTC_TIMESTAMP(6))
                           ELSE LEAST(
                               `AbsoluteExpirationUtc`,
                               TIMESTAMPADD(MICROSECOND, `SlidingExpirationMicroseconds`, UTC_TIMESTAMP(6)))
                           END
                       WHERE `Id` = CAST(@key AS BINARY)
                           AND `ExpiresAtUtc` > UTC_TIMESTAMP(6)
                           AND `SlidingExpirationMicroseconds` IS NOT NULL
                       """;

        LockForRefresh = $"""
                          SELECT 1 FROM {qualifiedTableName}
                          WHERE `Id` = CAST(@key AS BINARY) FOR UPDATE;
                          """;

        Refresh = refresh + ";";
        RefreshAfterRead = refresh + " AND `Revision` = @revision;";
        Remove = $"""
                  DELETE FROM {qualifiedTableName}
                  WHERE `Id` = CAST(@key AS BINARY);
                  """;

        SelectExpired = $"""
                         SELECT `Id` FROM {qualifiedTableName}
                         WHERE `ExpiresAtUtc` <= UTC_TIMESTAMP(6)
                         ORDER BY `ExpiresAtUtc`, `Id`
                         LIMIT @batchSize;
                         """;

        // Read candidates without locks, then acquire primary records before changing their expiration-index entries.
        DeleteExpiredPrefix = $"""
                               DELETE cache_entry FROM {qualifiedTableName} AS cache_entry FORCE INDEX (PRIMARY)
                               WHERE cache_entry.`ExpiresAtUtc` <= UTC_TIMESTAMP(6) AND (
                               """;
    }

    public string Set { get; }
    public string Get { get; }
    public string LockForRefresh { get; }
    public string Refresh { get; }
    public string RefreshAfterRead { get; }
    public string Remove { get; }
    public string SelectExpired { get; }
    public string DeleteExpiredPrefix { get; }
}
