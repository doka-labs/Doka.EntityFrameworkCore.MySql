namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Comprehensive MariaDB-specific integration tests covering:
/// - Database lifecycle (Create/Exists/HasTables/Delete)
/// - JSON alias columns (longtext + CHECK JSON_VALID)
/// - Native sequences (CREATE SEQUENCE / NEXT VALUE FOR)
/// - CRUD baseline on both 11.4 and 11.8
/// - Query translations verified with real MariaDB data
/// </summary>
[IntegrationDatabaseTest]
public sealed class MariaDbFeatureIntegrationTests
{
    // -- Database Lifecycle (with root access) --

    // -- Database Exists on MariaDB --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_database_exists_returns_true()
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new SimpleContext(
            CreateOptions<SimpleContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var creator = context.GetService<IRelationalDatabaseCreator>();

        // Act
        var exists = await creator.ExistsAsync(CancellationToken.None);

        // Assert
        Assert.True(exists);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_database_exists_returns_true()
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);
        await using var context = new SimpleContext(
            CreateOptions<SimpleContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 4, 0))));

        var creator = context.GetService<IRelationalDatabaseCreator>();

        // Act
        var exists = await creator.ExistsAsync(CancellationToken.None);

        // Assert
        Assert.True(exists);
    }

    // -- EnsureCreated + HasTables on MariaDB --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_ensure_created_and_has_tables()
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new SimpleContext(
            CreateOptions<SimpleContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `SimpleItems`;", CancellationToken.None);

        var creator = context.GetService<IRelationalDatabaseCreator>();

        try
        {
            // Act
            await context.Database.EnsureCreatedAsync(CancellationToken.None);
            var hasTables = await creator.HasTablesAsync(CancellationToken.None);

            // Assert
            Assert.True(hasTables);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `SimpleItems`;", CancellationToken.None);
        }
    }

    // -- MariaDB JSON Alias (longtext + CHECK) CRUD --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_json_alias_column_crud_roundtrip() =>
        await RunJsonAliasCrudTest(IntegrationDatabaseTarget.MariaDb1011, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_json_alias_column_crud_roundtrip() =>
        await RunJsonAliasCrudTest(IntegrationDatabaseTarget.MariaDb118, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_json_alias_column_crud_roundtrip() =>
        await RunJsonAliasCrudTest(IntegrationDatabaseTarget.MariaDb114, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_json_alias_column_crud_roundtrip() =>
        await RunJsonAliasCrudTest(IntegrationDatabaseTarget.MariaDb123, CancellationToken.None);

    private static async Task RunJsonAliasCrudTest(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var serverVersion = GetServerVersion(target);
        await using var context = new JsonContext(CreateOptions<JsonContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbJsonItems`;", cancellationToken);

        // MariaDB stores JSON as longtext with CHECK constraint.
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbJsonItems` (`Id` int NOT NULL AUTO_INCREMENT, `Data` longtext COLLATE utf8mb4_bin NOT NULL CHECK (JSON_VALID(`Data`)), PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;",
            cancellationToken);

        try
        {
            var jsonValue = """{"name":"MariaDB","version":11}""";

            // Act
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO `MdbJsonItems` (`Data`) VALUES ({0});",
                new object[] { jsonValue },
                cancellationToken);

            // Read back.
            var item = await context.Items.FirstAsync(cancellationToken);

            var insertedData = item.Data;

            // Update.
            item.Data = """{"name":"MariaDB","version":12}""";
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();

            var updated = await context.Items.FirstAsync(cancellationToken);

            // Assert
            Assert.Contains("MariaDB", insertedData, StringComparison.Ordinal);
            Assert.Contains("12", updated.Data, StringComparison.Ordinal);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbJsonItems`;", CancellationToken.None);
        }
    }

    // -- MariaDB Native Sequence via EF Migration DDL --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_native_sequence_ddl_creates_and_drops()
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        await using var context = new SimpleContext(CreateOptions<SimpleContext>(connectionString, serverVersion));
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var seqName = $"mdb_seq_{Guid.NewGuid():N}"[..25];

        // Backtick-escaped form for raw-SQL interpolation. The GUID:N format cannot
        // produce backticks today; escaping keeps the pattern safe-by-construction.
        var quotedSeqName = seqName.Replace("`", "``", StringComparison.Ordinal);

        // Act: Creation, allocation, and removal form one native sequence lifecycle.
        var createOps = generator.Generate(
            [
                new CreateSequenceOperation
                {
                    Name = seqName,
                    StartValue = 100,
                    IncrementBy = 5,
                    ClrType = typeof(long),
                },
            ],
            context.Model);

        // Execute the DDL.
        foreach (var cmd in createOps)
        {
            // Safe: SQL emitted by IMigrationsSqlGenerator from a fixture-controlled CreateSequenceOperation.
            await context.Database.ExecuteSqlRawAsync(cmd.CommandText, CancellationToken.None);
        }

        try
        {
            // Verify sequence works.
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync(CancellationToken.None);
            await using var fetchCmd = conn.CreateCommand();
            fetchCmd.CommandText = $"SELECT NEXT VALUE FOR `{quotedSeqName}`;";

            var value = Convert.ToInt64(
                await fetchCmd.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture);

            // Generate DROP SEQUENCE DDL.
            var dropOps = generator.Generate([new DropSequenceOperation { Name = seqName }], context.Model);
            foreach (var cmd in dropOps)
            {
                // Safe: SQL emitted by IMigrationsSqlGenerator from a fixture-controlled DropSequenceOperation.
                await context.Database.ExecuteSqlRawAsync(cmd.CommandText, CancellationToken.None);
            }

            // Assert
            Assert.Equal(100, value);
        }
        catch
        {
            // Cleanup on failure.
            await context.Database.ExecuteSqlRawAsync(
                $"DROP SEQUENCE IF EXISTS `{quotedSeqName}`;",
                CancellationToken.None);

            throw;
        }
    }

    // -- MariaDB 11.4 CRUD Baseline --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_crud_baseline()
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        await using var context = new CrudContext(CreateOptions<CrudContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbCrudItems`;", CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbCrudItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(200) NOT NULL, `Score` double NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;",
            CancellationToken.None);

        try
        {
            // Act: Every later operation depends on this row's preceding persisted state.
            context.Items.Add(
                new CrudItem
                {
                    Name = "First",
                    Score = 1.5,
                });

            context.Items.Add(
                new CrudItem
                {
                    Name = "Second",
                    Score = 2.5,
                });

            await context.SaveChangesAsync(CancellationToken.None);

            var insertedCount = await context.Items.CountAsync(CancellationToken.None);

            // Read.
            var first = await context
                .Items
                .Where(e => e.Name == "First")
                .FirstAsync(CancellationToken.None);

            var insertedScore = first.Score;

            // Update.
            first.Score = 99.9;
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            var updated = await context.Items.FindAsync(new object[] { first.Id }, CancellationToken.None);

            var updatedScore = updated!.Score;

            // Delete.
            context.Items.Remove(updated);
            await context.SaveChangesAsync(CancellationToken.None);

            var remainingCount = await context.Items.CountAsync(CancellationToken.None);

            // Assert
            Assert.Equal(2, insertedCount);
            Assert.Equal(1.5, insertedScore, 1);
            Assert.Equal(99.9, updatedScore, 1);
            Assert.Equal(1, remainingCount);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbCrudItems`;", CancellationToken.None);
        }
    }

    // -- MariaDB 11.8 CRUD Baseline --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_crud_baseline()
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        await using var context = new CrudContext(CreateOptions<CrudContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbCrudItems`;", CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbCrudItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(200) NOT NULL, `Score` double NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;",
            CancellationToken.None);

        try
        {
            context.Items.Add(
                new CrudItem
                {
                    Name = "Alpha",
                    Score = 10.0,
                });

            context.Items.Add(
                new CrudItem
                {
                    Name = "Beta",
                    Score = 20.0,
                });

            context.Items.Add(
                new CrudItem
                {
                    Name = "Gamma",
                    Score = 30.0,
                });

            await context.SaveChangesAsync(CancellationToken.None);

            // Act
            var filtered = await context
                .Items
                .Where(e => e.Score > 15)
                .OrderBy(e => e.Name)
                .ToListAsync(CancellationToken.None);

            context.Items.RemoveRange(filtered);
            await context.SaveChangesAsync(CancellationToken.None);
            var remainingCount = await context.Items.CountAsync(CancellationToken.None);

            // Assert
            Assert.Equal(2, filtered.Count);
            Assert.Equal("Beta", filtered[0].Name);
            Assert.Equal("Gamma", filtered[1].Name);
            Assert.Equal(1, remainingCount);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbCrudItems`;", CancellationToken.None);
        }
    }

    // -- MariaDB DateTime/DateOnly Arithmetic --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_datetime_arithmetic_executes_correctly() =>
        AssertDateTimeArithmeticAsync(addMonths: false, cancellationToken: CancellationToken.None);

    /// <summary>Verifies calendar-month arithmetic independently from DateTime day arithmetic.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_dateonly_month_arithmetic_executes_correctly() =>
        AssertDateTimeArithmeticAsync(addMonths: true, cancellationToken: CancellationToken.None);

    private static async Task AssertDateTimeArithmeticAsync(
        bool addMonths,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        await using var context = new TemporalContext(CreateOptions<TemporalContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbTemporal`;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbTemporal` (`Id` int NOT NULL AUTO_INCREMENT, `Created` datetime(6) NOT NULL, `BirthDate` date NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;",
            cancellationToken);

        try
        {
            context.Items.Add(
                new TemporalItem
                {
                    Created = new DateTime(2025, 3, 15, 9, 0, 0),
                    BirthDate = new DateOnly(1990, 6, 1),
                });

            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();

            // Act
            var actual = addMonths
                ? (await context
                    .Items
                    .Select(e => e.BirthDate.AddMonths(6))
                    .FirstAsync(cancellationToken)).Month
                : (await context
                    .Items
                    .Select(e => e.Created.AddDays(10))
                    .FirstAsync(cancellationToken)).Day;

            // Assert
            Assert.Equal(
                addMonths
                    ? 12
                    : 25,
                actual);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbTemporal`;", CancellationToken.None);
        }
    }

    // -- Helpers --

    private static DbContextOptions<T> CreateOptions<T>(
        string cs,
        MySqlServerVersion sv
    )
        where T : DbContext
    {
        var b = IntegrationTestDbContextOptions.Create<T>();
        b.UseMySql(cs, sv);

        return b.Options;
    }

    private static MySqlServerVersion GetServerVersion(
        IntegrationDatabaseTarget target
    )
    {
        return target == IntegrationDatabaseTarget.MySql80
            ? MySqlServerVersion.MySql(
                new Version(8, 0, 0),
                MySqlServerVersionCompatibilityMode.AllowUnsupported)
            : IntegrationTestEnvironment.GetServerVersion(target);
    }

    // -- Entities / Contexts --

    private sealed class SimpleItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class SimpleContext : DbContext
    {
        public SimpleContext(
            DbContextOptions<SimpleContext> o
        ) : base(o) { }

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<SimpleItem>(e =>
            {
                e.ToTable("SimpleItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(200);
            });
        }
    }

    private sealed class JsonItem
    {
        public int Id { get; set; }
        public string Data { get; set; } = "{}";
    }

    private sealed class JsonContext : DbContext
    {
        public JsonContext(
            DbContextOptions<JsonContext> o
        ) : base(o) { }

        public DbSet<JsonItem> Items => Set<JsonItem>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<JsonItem>(e =>
            {
                e.ToTable("MdbJsonItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class CrudItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Score { get; set; }
    }

    private sealed class CrudContext : DbContext
    {
        public CrudContext(
            DbContextOptions<CrudContext> o
        ) : base(o) { }

        public DbSet<CrudItem> Items => Set<CrudItem>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<CrudItem>(e =>
            {
                e.ToTable("MdbCrudItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(200);
            });
        }
    }

    private sealed class TemporalItem
    {
        public int Id { get; set; }
        public DateTime Created { get; set; }
        public DateOnly BirthDate { get; set; }
    }

    private sealed class TemporalContext : DbContext
    {
        public TemporalContext(
            DbContextOptions<TemporalContext> o
        ) : base(o) { }

        public DbSet<TemporalItem> Items => Set<TemporalItem>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<TemporalItem>(e =>
            {
                e.ToTable("MdbTemporal");
                e.HasKey(x => x.Id);
            });
        }
    }
}
