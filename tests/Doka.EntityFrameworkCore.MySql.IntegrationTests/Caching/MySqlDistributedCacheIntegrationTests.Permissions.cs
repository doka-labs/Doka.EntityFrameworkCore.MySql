namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies DML-only cache access and cleanup-failure isolation.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Verifies the cache lifecycle with DML rights and no schema-change permission.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Dml_only_permissions_allow_cache_operations_without_schema_access(
        string targetName
    )
    {
        // Arrange
        await using var store = await CreateDeployedStoreAsync(ParseTarget(targetName)).ConfigureAwait(false);
        var userName = "cache_" + Guid.NewGuid().ToString("N")[..20];
        var password = Guid.NewGuid().ToString("N");
        await store.ExecuteAsync($"CREATE USER '{userName}'@'%' IDENTIFIED BY '{password}';").ConfigureAwait(false);

        try
        {
            var connectionString = await GrantDmlOnlyAccessAsync(store, userName, password).ConfigureAwait(false);
            await using var cache = store.CreateCache(
                new CacheManualTimeProvider(),
                connectionString: connectionString);
            byte[] value = [1, 2];
            var destination = new ExactBufferWriter(value.Length);

            // Act
            // WHY: One restricted-user lifecycle proves that ordinary cache use
            // succeeds without accidentally relying on deployment privileges.
            await cache.SetAsync("dml-only", value, new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);
            var written = await cache.GetAsync("dml-only", CancellationToken.None).ConfigureAwait(false);
            await cache.RefreshAsync("dml-only", CancellationToken.None).ConfigureAwait(false);
            var found = await cache.TryGetAsync("dml-only", destination, CancellationToken.None).ConfigureAwait(false);
            await cache.RemoveAsync("dml-only", CancellationToken.None).ConfigureAwait(false);
            var removed = await cache.GetAsync("dml-only", CancellationToken.None).ConfigureAwait(false);

            await using var restrictedConnection = new MySqlConnection(connectionString);
            await restrictedConnection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await using var ddl = restrictedConnection.CreateCommand();
            ddl.CommandText = $"ALTER TABLE {store.QualifiedTableName} COMMENT='not-authorized';";
            var ddlFailure = await Record.ExceptionAsync(() => ddl.ExecuteNonQueryAsync(CancellationToken.None))
                .ConfigureAwait(false);

            // Assert
            Assert.Equal(value, written);
            Assert.True(found);
            Assert.Equal(value, destination.WrittenBytes);
            Assert.Null(removed);
            Assert.IsType<MySqlException>(ddlFailure);
        }
        finally
        {
            await store.ExecuteAsync($"DROP USER IF EXISTS '{userName}'@'%';").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies nonfatal cleanup failures, redacted diagnostics, and recovery after permission restoration.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Cleanup_permission_failure_preserves_foreground_access_and_recovers(
        string targetName
    )
    {
        // Arrange
        await using var store = await CreateDeployedStoreAsync(ParseTarget(targetName)).ConfigureAwait(false);
        var userName = "cache_" + Guid.NewGuid().ToString("N")[..20];
        var password = Guid.NewGuid().ToString("N");
        await store.ExecuteAsync($"CREATE USER '{userName}'@'%' IDENTIFIED BY '{password}';").ConfigureAwait(false);

        try
        {
            var connectionString = await GrantDmlOnlyAccessAsync(store, userName, password).ConfigureAwait(false);
            var time = new CacheManualTimeProvider();
            var logger = new CacheRecordingLogger();
            await using var cache = store.CreateCache(time, logger, connectionString);
            const string privateKey = "private-cache-key";
            var privateValue = Encoding.UTF8.GetBytes("private-cache-value");
            await cache.SetAsync(privateKey, privateValue, new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            // Act
            // WHY: Revocation and restoration share the cache clock so the test
            // observes failure backoff and recovery rather than a fresh cache.
            await store.ExecuteAsync($"REVOKE DELETE ON {store.QualifiedTableName} FROM '{userName}'@'%';")
                .ConfigureAwait(false);
            time.Advance(TimeSpan.FromMinutes(5));
            var emptyCandidateValue = await cache.GetAsync(privateKey, CancellationToken.None).ConfigureAwait(false);
            var emptyCandidateEntries = logger.Entries.ToArray();
            await store.InsertExpiredEntriesAsync(1).ConfigureAwait(false);
            time.Advance(TimeSpan.FromMinutes(5));
            var failureValue = await cache.GetAsync(privateKey, CancellationToken.None).ConfigureAwait(false);
            var failureEntries = logger.Entries.ToArray();
            var failedExpiredCount = await store.CountExpiredAsync().ConfigureAwait(false);
            var backoffValue = await cache.GetAsync(privateKey, CancellationToken.None).ConfigureAwait(false);
            var backoffEntries = logger.Entries.ToArray();
            await store.ExecuteAsync($"GRANT DELETE ON {store.QualifiedTableName} TO '{userName}'@'%';")
                .ConfigureAwait(false);
            time.Advance(TimeSpan.FromMinutes(5));
            var recoveredValue = await cache.GetAsync(privateKey, CancellationToken.None).ConfigureAwait(false);
            var recoveredEntries = logger.Entries.ToArray();
            var recoveredExpiredCount = await store.CountExpiredAsync().ConfigureAwait(false);

            // Assert
            Assert.Equal(privateValue, emptyCandidateValue);
            // WHY: An empty candidate read issues no DELETE even when that privilege is absent.
            Assert.Empty(emptyCandidateEntries);
            Assert.Equal(privateValue, failureValue);
            var log = Assert.Single(failureEntries);
            Assert.Equal(LogLevel.Warning, log.Level);
            Assert.Equal(1, log.EventId.Id);
            Assert.Null(log.Exception);
            var rendered = log.Message + log.Exception;
            Assert.DoesNotContain(privateKey, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("private-cache-value", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(password, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(connectionString, rendered, StringComparison.Ordinal);
            Assert.Equal(1L, failedExpiredCount);
            Assert.Equal(privateValue, backoffValue);
            Assert.Single(backoffEntries);
            Assert.Equal(privateValue, recoveredValue);
            Assert.Single(recoveredEntries);
            Assert.Equal(0L, recoveredExpiredCount);
        }
        finally
        {
            await store.ExecuteAsync($"DROP USER IF EXISTS '{userName}'@'%';").ConfigureAwait(false);
        }
    }

    /// <summary>Grants only cache DML privileges and returns the restricted nonpooled connection.</summary>
    private static async Task<string> GrantDmlOnlyAccessAsync(
        CacheIntegrationStore store,
        string userName,
        string password
    )
    {
        await store.ExecuteAsync(
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON {store.QualifiedTableName} TO '{userName}'@'%';")
            .ConfigureAwait(false);

        return new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            UserID = userName,
            Password = password,
            Pooling = false,
        }.ConnectionString;
    }
}
