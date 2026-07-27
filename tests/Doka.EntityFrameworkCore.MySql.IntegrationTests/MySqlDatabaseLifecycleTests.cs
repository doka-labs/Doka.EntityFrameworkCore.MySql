namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Integration tests for database creator lifecycle (Exists, Create, Delete, HasTables),
/// advisory lock mechanism (GET_LOCK/RELEASE_LOCK), sequence value generation,
/// and HiLo runtime against live MySQL 8.4.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlDatabaseLifecycleTests
{
    // -- Database Creator: Exists returns true for existing database --

    /// <summary>
    /// Verifies that Exists returns true for the existing Docker-provisioned database.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Database_exists_returns_true_for_existing_database()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await using var context = new LifecycleContext(CreateOptions(connectionString));
        var creator = context.GetService<IRelationalDatabaseCreator>();

        Assert.True(
            await creator
                .ExistsAsync()
                .ConfigureAwait(false));
    }

    // -- Advisory Lock: GET_LOCK / RELEASE_LOCK --

    /// <summary>
    /// Verifies advisory lock acquire and release on MySQL with IS_USED_LOCK verification.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Advisory_lock_acquires_and_releases_on_mysql84()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new LifecycleContext(CreateOptions(connectionString));
        var historyRepository = context.GetService<IHistoryRepository>();

        var databaseLock = await historyRepository
            .AcquireDatabaseLockAsync()
            .ConfigureAwait(false);
        Assert.NotNull(databaseLock);

        // Verify lock is actually held via a separate connection. The lock name is
        // database-scoped (per ADR D-002), so we derive it from the connection string.
        var lockName = MySqlAdvisoryLockNaming.BuildLockName(connectionString);

        await using var checkConn = new MySqlConnector.MySqlConnection(connectionString);
        await checkConn
            .OpenAsync()
            .ConfigureAwait(false);
        await using var checkCmd = checkConn.CreateCommand();
        checkCmd.CommandText = "SELECT IS_USED_LOCK(@name);";
        var nameParam = checkCmd.CreateParameter();
        nameParam.ParameterName = "@name";
        nameParam.Value = lockName;
        checkCmd.Parameters.Add(nameParam);
        var lockHolder = await checkCmd
            .ExecuteScalarAsync()
            .ConfigureAwait(false);
        Assert.NotNull(lockHolder); // Non-null means lock is held.

        // Release.
        await databaseLock
            .DisposeAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that a second session cannot acquire the lock while the first holds it.
    /// Uses GET_LOCK with timeout=0 to avoid waiting.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Advisory_lock_contention_blocks_second_session()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new LifecycleContext(CreateOptions(connectionString));
        var historyRepository = context.GetService<IHistoryRepository>();

        // Session 1: Acquire lock.
        var databaseLock = await historyRepository
            .AcquireDatabaseLockAsync()
            .ConfigureAwait(false);

        try
        {
            // Session 2: Try to acquire the same lock with immediate timeout. The lock
            // name is database-scoped (per ADR D-002).
            var lockName = MySqlAdvisoryLockNaming.BuildLockName(connectionString);

            await using var conn2 = new MySqlConnector.MySqlConnection(connectionString);
            await conn2
                .OpenAsync()
                .ConfigureAwait(false);
            await using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT GET_LOCK(@name, 0);";
            var nameParam = cmd2.CreateParameter();
            nameParam.ParameterName = "@name";
            nameParam.Value = lockName;
            cmd2.Parameters.Add(nameParam);
            var result = await cmd2
                .ExecuteScalarAsync()
                .ConfigureAwait(false);

            // GET_LOCK returns 0 when timeout expires (lock not acquired).
            Assert.Equal(0L, Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            // Release lock from session 1.
            await databaseLock
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies advisory lock acquire and release on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Advisory_lock_acquires_and_releases_on_mariadb118()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new LifecycleContext(CreateMariaDbOptions(connectionString));
        var historyRepository = context.GetService<IHistoryRepository>();

        await using var databaseLock = await historyRepository
            .AcquireDatabaseLockAsync()
            .ConfigureAwait(false);
        Assert.NotNull(databaseLock);
    }

    /// <summary>
    /// Verifies that Exists returns true on MariaDB 11.8 for existing database.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Database_exists_returns_true_on_mariadb118()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new LifecycleContext(CreateMariaDbOptions(connectionString));
        var creator = context.GetService<IRelationalDatabaseCreator>();
        Assert.True(
            await creator
                .ExistsAsync()
                .ConfigureAwait(false));
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Guid_binary16_and_char36_roundtrip_on_mariadb118()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new GuidContext(CreateMariaDbGuidOptions(connectionString));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbGuidBin`;");
        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbGuidChar`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbGuidBin` (`Id` binary(16) NOT NULL, `Name` varchar(100) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbGuidChar` (`Id` char(36) NOT NULL, `Name` varchar(100) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            var binId = Guid.NewGuid();
            var charId = Guid.NewGuid();
            context.BinItems.Add(
                new GuidBinEntity
                {
                    Id = binId,
                    Name = "BinTest"
                });
            context.CharItems.Add(
                new GuidCharEntity
                {
                    Id = charId,
                    Name = "CharTest"
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var loadedBin = await context.BinItems.FirstAsync();
            Assert.Equal(binId, loadedBin.Id);

            var loadedChar = await context.CharItems.FirstAsync();
            Assert.Equal(charId, loadedChar.Id);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbGuidBin`;");
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbGuidChar`;");
        }
    }

    // -- Sequence Value Generation: Table-Based Emulation --

    /// <summary>
    /// Verifies that the table-based sequence emulation creates the sequence table and fetches values.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Sequence_table_emulation_creates_table_and_fetches_values()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var seqName = $"test_seq_{Guid.NewGuid():N}"[..30];

        await using var connection = new MySqlConnector.MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        try
        {
            // Create the emulated sequence table.
            await using (var createCmd = connection.CreateCommand())
            {
                createCmd.CommandText =
                    $"CREATE TABLE IF NOT EXISTS `__efsequence_{seqName}` (`value` BIGINT NOT NULL) ENGINE=InnoDB;"
                    + $"INSERT INTO `__efsequence_{seqName}` (`value`) VALUES (0);";
                await createCmd
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            // Fetch values via the sequence generator.
            var value1 = MySqlSequenceValueGenerator.GetNextValue(
                connection,
                seqName,
                1,
                supportsNativeSequences: false);
            var value2 = MySqlSequenceValueGenerator.GetNextValue(
                connection,
                seqName,
                1,
                supportsNativeSequences: false);

            Assert.Equal(1, value1);
            Assert.Equal(2, value2);
            Assert.True(value2 > value1);
        }
        finally
        {
            await using var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = $"DROP TABLE IF EXISTS `__efsequence_{seqName}`;";
            await dropCmd
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }
    }

    // -- MariaDB Native Sequence --

    /// <summary>
    /// Verifies that native MariaDB sequences create and fetch values.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Mariadb_native_sequence_creates_and_fetches_values()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var seqName = $"test_seq_{Guid.NewGuid():N}"[..30];

        await using var connection = new MySqlConnector.MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        try
        {
            await using (var createCmd = connection.CreateCommand())
            {
                createCmd.CommandText = $"CREATE SEQUENCE `{seqName}` START WITH 1 INCREMENT BY 1;";
                await createCmd
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            var value1 = MySqlSequenceValueGenerator.GetNextValue(
                connection,
                seqName,
                1,
                supportsNativeSequences: true);
            var value2 = MySqlSequenceValueGenerator.GetNextValue(
                connection,
                seqName,
                1,
                supportsNativeSequences: true);

            Assert.Equal(1, value1);
            Assert.Equal(2, value2);
        }
        finally
        {
            await using var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = $"DROP SEQUENCE IF EXISTS `{seqName}`;";
            await dropCmd
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }
    }

    // -- Database Creator: Exists returns false for non-existent database --

    // -- Helpers --

    private static DbContextOptions<LifecycleContext> CreateOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<LifecycleContext>();
        builder.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    private static DbContextOptions<LifecycleContext> CreateMariaDbOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<LifecycleContext>();
        builder.UseMySql(connectionString, MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        return builder.Options;
    }

    private static DbContextOptions<GuidContext> CreateMariaDbGuidOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<GuidContext>();
        builder.UseMySql(connectionString, MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        return builder.Options;
    }

    // -- Context --

    private sealed class LifecycleContext : DbContext
    {
        public LifecycleContext(
            DbContextOptions<LifecycleContext> options
        ) : base(options) { }

        public DbSet<LifecycleEntity> Items => Set<LifecycleEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<LifecycleEntity>(e =>
            {
                e.ToTable("LifecycleItems");
                e.HasKey(x => x.Id);
            });
        }
    }

    private sealed class LifecycleEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // -- GUID entities --

    private sealed class GuidBinEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class GuidCharEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class GuidContext : DbContext
    {
        public GuidContext(
            DbContextOptions<GuidContext> options
        ) : base(options) { }

        public DbSet<GuidBinEntity> BinItems => Set<GuidBinEntity>();
        public DbSet<GuidCharEntity> CharItems => Set<GuidCharEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<GuidBinEntity>(e =>
            {
                e.ToTable("MdbGuidBin");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(100);
            });
            modelBuilder.Entity<GuidCharEntity>(e =>
            {
                e.ToTable("MdbGuidChar");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(100);
            });
        }
    }
}
