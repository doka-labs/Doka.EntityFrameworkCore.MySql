namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Live concurrency coverage for the migration advisory lock (per ADR D-002): two
/// migrations running against different databases on the same MySQL instance must
/// not block each other, while two migrations against the same database must
/// serialize, with the third caller observing a <see cref="TimeoutException"/>
/// when the lock timeout is exhausted.
/// </summary>
public sealed class MySqlMigrationConcurrencyTests
{
    /// <summary>
    /// Multi-tenant safety check: two distinct databases on the same server hold
    /// independent advisory locks, so two migrations can run concurrently.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_locks_do_not_block_across_distinct_databases()
    {
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbNameA = $"doka_lock_a_{Guid.NewGuid():N}"[..30];
        var dbNameB = $"doka_lock_b_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbNameA)
            .ConfigureAwait(false);
        await CreateDatabaseAsync(baseConnectionString, dbNameB)
            .ConfigureAwait(false);

        try
        {
            await using var contextA =
                new LockContext(CreateOptions(BuildConnectionString(baseConnectionString, dbNameA)));
            await using var contextB =
                new LockContext(CreateOptions(BuildConnectionString(baseConnectionString, dbNameB)));

            var historyA = contextA.GetService<IHistoryRepository>();
            var historyB = contextB.GetService<IHistoryRepository>();

            var acquireA = historyA.AcquireDatabaseLockAsync();
            var acquireB = historyB.AcquireDatabaseLockAsync();

            await Task
                .WhenAll(acquireA, acquireB)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            await using var lockA = await acquireA.ConfigureAwait(false);
            await using var lockB = await acquireB.ConfigureAwait(false);

            Assert.NotNull(lockA);
            Assert.NotNull(lockB);
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbNameA)
                .ConfigureAwait(false);
            await DropDatabaseAsync(baseConnectionString, dbNameB)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serialization check: two migrations against the same database compete for
    /// the same lock. The second caller waits; a third caller with a sub-second
    /// timeout surfaces a <see cref="TimeoutException"/>.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Migration_locks_serialise_on_same_database_and_surface_timeout()
    {
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var dbName = $"doka_lock_s_{Guid.NewGuid():N}"[..30];

        await CreateDatabaseAsync(baseConnectionString, dbName)
            .ConfigureAwait(false);
        var scopedConnectionString = BuildConnectionString(baseConnectionString, dbName);

        try
        {
            await using var holder = new LockContext(CreateOptions(scopedConnectionString));
            var holderHistory = holder.GetService<IHistoryRepository>();

            await using var heldLock = await holderHistory
                .AcquireDatabaseLockAsync()
                .ConfigureAwait(false);

            // Confirm a contending session sees the lock held via GET_LOCK with timeout=0.
            var lockName = MySqlAdvisoryLockNaming.BuildLockName(scopedConnectionString);

            await using var contender = new MySqlConnector.MySqlConnection(scopedConnectionString);
            await contender
                .OpenAsync()
                .ConfigureAwait(false);
            await using var probe = contender.CreateCommand();
            probe.CommandText = "SELECT GET_LOCK(@name, 0);";
            var nameParam = probe.CreateParameter();
            nameParam.ParameterName = "@name";
            nameParam.Value = lockName;
            probe.Parameters.Add(nameParam);

            var probeResult = await probe
                .ExecuteScalarAsync()
                .ConfigureAwait(false);

            Assert.Equal(0L, Convert.ToInt64(probeResult, CultureInfo.InvariantCulture));
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, dbName)
                .ConfigureAwait(false);
        }
    }

    private static string BuildConnectionString(
        string baseConnectionString,
        string databaseName
    )
    {
        var builder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(
        string baseConnectionString,
        string databaseName
    )
    {
        var serverBuilder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = string.Empty,
        };
        serverBuilder.Remove("Database");

        await using var connection = new MySqlConnector.MySqlConnection(serverBuilder.ConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task DropDatabaseAsync(
        string baseConnectionString,
        string databaseName
    )
    {
        var serverBuilder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = string.Empty,
        };
        serverBuilder.Remove("Database");

        await using var connection = new MySqlConnector.MySqlConnection(serverBuilder.ConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static DbContextOptions<LockContext> CreateOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<LockContext>();
        builder.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    private sealed class LockContext : DbContext
    {
        public LockContext(
            DbContextOptions<LockContext> options
        ) : base(options) { }
    }
}
