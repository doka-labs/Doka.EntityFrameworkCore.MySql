namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Integration tests for database creator lifecycle, advisory locks, and
/// sequence value generation against supported live engines.
/// </summary>
[IntegrationDatabaseTest]
public sealed class MySqlDatabaseLifecycleTests
{
    /// <summary>Verifies database exists returns true for existing database.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Database_exists_returns_true_for_existing_database()
    {
        await AssertDatabaseExistsAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies database exists returns true on mysql97.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task Database_exists_returns_true_on_mysql97()
    {
        await AssertDatabaseExistsAsync(
                IntegrationDatabaseTarget.MySql97,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MySql97))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies database exists returns true on mariadb1011.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task Database_exists_returns_true_on_mariadb1011()
    {
        await AssertDatabaseExistsAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb1011))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies database exists returns true on mariadb114.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task Database_exists_returns_true_on_mariadb114()
    {
        await AssertDatabaseExistsAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies database exists returns true on mariadb118.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Database_exists_returns_true_on_mariadb118()
    {
        await AssertDatabaseExistsAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies database exists returns true on mariadb123.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task Database_exists_returns_true_on_mariadb123()
    {
        await AssertDatabaseExistsAsync(
                IntegrationDatabaseTarget.MariaDb123,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb123))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies advisory lock contract holds on mysql84.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Advisory_lock_contract_holds_on_mysql84()
    {
        await AssertAdvisoryLockContractAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies advisory lock contract holds on mysql97.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task Advisory_lock_contract_holds_on_mysql97()
    {
        await AssertAdvisoryLockContractAsync(
                IntegrationDatabaseTarget.MySql97,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MySql97))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies advisory lock contract holds on mariadb1011.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task Advisory_lock_contract_holds_on_mariadb1011()
    {
        await AssertAdvisoryLockContractAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb1011))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies advisory lock contract holds on mariadb114.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task Advisory_lock_contract_holds_on_mariadb114()
    {
        await AssertAdvisoryLockContractAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies advisory lock contract holds on mariadb118.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Advisory_lock_contract_holds_on_mariadb118()
    {
        await AssertAdvisoryLockContractAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies advisory lock contract holds on mariadb123.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task Advisory_lock_contract_holds_on_mariadb123()
    {
        await AssertAdvisoryLockContractAsync(
                IntegrationDatabaseTarget.MariaDb123,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb123))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies guid binary16 and char36 roundtrip on mariadb1011.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task Guid_binary16_and_char36_roundtrip_on_mariadb1011()
    {
        await AssertGuidRoundTripAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb1011))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies guid binary16 and char36 roundtrip on mariadb114.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task Guid_binary16_and_char36_roundtrip_on_mariadb114()
    {
        await AssertGuidRoundTripAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies guid binary16 and char36 roundtrip on mariadb118.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Guid_binary16_and_char36_roundtrip_on_mariadb118()
    {
        await AssertGuidRoundTripAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies guid binary16 and char36 roundtrip on mariadb123.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task Guid_binary16_and_char36_roundtrip_on_mariadb123()
    {
        await AssertGuidRoundTripAsync(
                IntegrationDatabaseTarget.MariaDb123,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb123))
            .ConfigureAwait(false);
    }

    private static async Task AssertDatabaseExistsAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await using var context = new LifecycleContext(CreateOptions(connectionString, serverVersion));

        var creator = context.GetService<IRelationalDatabaseCreator>();

        // Act
        var exists = await creator.ExistsAsync().ConfigureAwait(false);

        // Assert
        Assert.True(exists);
    }

    private static async Task AssertAdvisoryLockContractAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await using var context = new LifecycleContext(CreateOptions(connectionString, serverVersion));
        var historyRepository = context.GetService<IHistoryRepository>();
        await using var databaseLock = await historyRepository
            .AcquireDatabaseLockAsync()
            .ConfigureAwait(false);

        var lockName = MySqlAdvisoryLockNaming.BuildLockName(connectionString);

        await using var competingConnection = new MySqlConnection(connectionString);
        await competingConnection
            .OpenAsync()
            .ConfigureAwait(false);

        await using (var ownerCommand = competingConnection.CreateCommand())
        {
            ownerCommand.CommandText = "SELECT IS_USED_LOCK(@name);";
            ownerCommand.Parameters.AddWithValue("@name", lockName);

            Assert.NotNull(
                await ownerCommand
                    .ExecuteScalarAsync()
                    .ConfigureAwait(false));
        }

        await using var contentionCommand = competingConnection.CreateCommand();
        contentionCommand.CommandText = "SELECT GET_LOCK(@name, 0);";
        contentionCommand.Parameters.AddWithValue("@name", lockName);

        // Act
        var result = await contentionCommand
            .ExecuteScalarAsync()
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(0L, Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    private static async Task AssertGuidRoundTripAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await using var context = new GuidContext(CreateGuidOptions(connectionString, serverVersion));

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

            // Act
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var loadedBinary = await context.BinItems.FirstAsync();
            var loadedChar = await context.CharItems.FirstAsync();

            // Assert
            Assert.Equal(binId, loadedBinary.Id);
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
    /// Verifies the synchronous table-emulation API, including its first-value,
    /// increment, and singleton-row contracts.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public void Sequence_table_emulation_sync_fetch_returns_start_then_increment() =>
        AssertTableSequenceSync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies that the asynchronous table-emulation API returns the same first
    /// value and increment semantics without falling back to synchronous I/O.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Sequence_table_emulation_async_fetch_returns_start_then_increment()
    {
        await AssertTableSequenceAsync(IntegrationDatabaseTarget.MySql84)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies both table-emulation I/O paths on MySQL 9.7, whose engine still
    /// has no native sequence object.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_sequence_table_emulation_preserves_sync_and_async_contracts()
    {
        AssertTableSequenceSync(IntegrationDatabaseTarget.MySql97);
        await AssertTableSequenceAsync(IntegrationDatabaseTarget.MySql97)
            .ConfigureAwait(false);
    }

    private static void AssertTableSequenceSync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var sequenceName = $"test_seq_{Guid.NewGuid():N}"[..30];

        using var connection = new MySqlConnection(connectionString);
        connection.Open();

        try
        {
            using (var createCommand = connection.CreateCommand())
            {
                createCommand.CommandText = CreateTableSequenceSql(sequenceName);
                createCommand.ExecuteNonQuery();
            }

            // Act
            // WHY: Consecutive allocations qualify one sequence's state transition.
            var firstValue = MySqlSequenceValueGenerator.GetNextValue(
                connection,
                sequenceName,
                7,
                supportsNativeSequences: false);

            var secondValue = MySqlSequenceValueGenerator.GetNextValue(
                connection,
                sequenceName,
                7,
                supportsNativeSequences: false);

            using var duplicateRowCommand = connection.CreateCommand();
            duplicateRowCommand.CommandText =
                $"INSERT INTO `__efsequence_{sequenceName}` (`id`, `value`, `is_called`) VALUES (2, 100, FALSE);";

            var duplicateRowFailure = Record.Exception(() => duplicateRowCommand.ExecuteNonQuery());

            // Assert
            Assert.Equal(42, firstValue);
            Assert.Equal(49, secondValue);
            Assert.True(secondValue > firstValue);
            Assert.IsType<MySqlException>(duplicateRowFailure);
        }
        finally
        {
            using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP TABLE IF EXISTS `__efsequence_{sequenceName}`;";
            dropCommand.ExecuteNonQuery();
        }
    }

    private static async Task AssertTableSequenceAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var sequenceName = $"test_seq_{Guid.NewGuid():N}"[..30];

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        try
        {
            await using (var createCommand = connection.CreateCommand())
            {
                createCommand.CommandText = CreateTableSequenceSql(sequenceName);
                await createCommand
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            // Act
            var firstValue = await MySqlSequenceValueGenerator
                .GetNextValueAsync(
                    connection,
                    sequenceName,
                    7,
                    supportsNativeSequences: false)
                .ConfigureAwait(false);

            var secondValue = await MySqlSequenceValueGenerator
                .GetNextValueAsync(
                    connection,
                    sequenceName,
                    7,
                    supportsNativeSequences: false)
                .ConfigureAwait(false);

            // Assert
            Assert.Equal(42, firstValue);
            Assert.Equal(49, secondValue);
            Assert.True(secondValue > firstValue);
        }
        finally
        {
            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP TABLE IF EXISTS `__efsequence_{sequenceName}`;";
            await dropCommand
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }
    }

    private static string CreateTableSequenceSql(
        string sequenceName
    ) => $"CREATE TABLE IF NOT EXISTS `__efsequence_{sequenceName}` ("
        + "  `id` TINYINT UNSIGNED NOT NULL,"
        + "  `value` BIGINT NOT NULL,"
        + "  `is_called` BOOLEAN NOT NULL,"
        + "  PRIMARY KEY (`id`),"
        + "  CHECK (`id` = 1)"
        + ") ENGINE=InnoDB;"
        + $"INSERT INTO `__efsequence_{sequenceName}` (`id`, `value`, `is_called`) VALUES (1, 42, FALSE);";

    // -- MariaDB Native Sequence --

    /// <summary>Verifies mariadb114 native sequence sync fetch returns start then increment.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public void Mariadb114_native_sequence_sync_fetch_returns_start_then_increment() =>
        AssertNativeSequenceSync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>Verifies mariadb1011 native sequence sync fetch returns start then increment.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public void Mariadb1011_native_sequence_sync_fetch_returns_start_then_increment() =>
        AssertNativeSequenceSync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>Verifies mariadb118 native sequence sync fetch returns start then increment.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public void Mariadb118_native_sequence_sync_fetch_returns_start_then_increment() =>
        AssertNativeSequenceSync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>Verifies mariadb123 native sequence sync fetch returns start then increment.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public void Mariadb123_native_sequence_sync_fetch_returns_start_then_increment() =>
        AssertNativeSequenceSync(IntegrationDatabaseTarget.MariaDb123);

    /// <summary>Verifies mariadb114 native sequence async fetch returns start then increment.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task Mariadb114_native_sequence_async_fetch_returns_start_then_increment()
    {
        await AssertNativeSequenceAsync(IntegrationDatabaseTarget.MariaDb114)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies mariadb1011 native sequence async fetch returns start then increment.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task Mariadb1011_native_sequence_async_fetch_returns_start_then_increment()
    {
        await AssertNativeSequenceAsync(IntegrationDatabaseTarget.MariaDb1011)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies mariadb118 native sequence async fetch returns start then increment.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Mariadb118_native_sequence_async_fetch_returns_start_then_increment()
    {
        await AssertNativeSequenceAsync(IntegrationDatabaseTarget.MariaDb118)
            .ConfigureAwait(false);
    }

    /// <summary>Verifies mariadb123 native sequence async fetch returns start then increment.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task Mariadb123_native_sequence_async_fetch_returns_start_then_increment()
    {
        await AssertNativeSequenceAsync(IntegrationDatabaseTarget.MariaDb123)
            .ConfigureAwait(false);
    }

    private static void AssertNativeSequenceSync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var seqName = $"test_seq_{Guid.NewGuid():N}"[..30];

        using var connection = new MySqlConnection(connectionString);
        connection.Open();

        try
        {
            using (var createCmd = connection.CreateCommand())
            {
                createCmd.CommandText = $"CREATE SEQUENCE `{seqName}` START WITH 1 INCREMENT BY 1;";
                createCmd.ExecuteNonQuery();
            }

            // Act
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

            // Assert
            Assert.Equal(1, value1);
            Assert.Equal(2, value2);
        }
        finally
        {
            using var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = $"DROP SEQUENCE IF EXISTS `{seqName}`;";
            dropCmd.ExecuteNonQuery();
        }
    }

    private static async Task AssertNativeSequenceAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var seqName = $"test_seq_{Guid.NewGuid():N}"[..30];

        await using var connection = new MySqlConnection(connectionString);
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

            // Act
            var value1 = await MySqlSequenceValueGenerator
                .GetNextValueAsync(
                    connection,
                    seqName,
                    1,
                    supportsNativeSequences: true)
                .ConfigureAwait(false);

            var value2 = await MySqlSequenceValueGenerator
                .GetNextValueAsync(
                    connection,
                    seqName,
                    1,
                    supportsNativeSequences: true)
                .ConfigureAwait(false);

            // Assert
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
        string connectionString,
        MySqlServerVersion serverVersion
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<LifecycleContext>();
        builder.UseMySql(connectionString, serverVersion);

        return builder.Options;
    }

    private static DbContextOptions<GuidContext> CreateGuidOptions(
        string connectionString,
        MySqlServerVersion serverVersion
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<GuidContext>();
        builder.UseMySql(connectionString, serverVersion);
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
