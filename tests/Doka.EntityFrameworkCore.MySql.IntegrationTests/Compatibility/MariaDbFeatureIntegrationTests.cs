namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Comprehensive MariaDB-specific integration tests covering:
/// - Database lifecycle (Create/Exists/HasTables/Delete)
/// - JSON alias columns (longtext + CHECK JSON_VALID)
/// - Native sequences (CREATE SEQUENCE / NEXT VALUE FOR)
/// - CRUD baseline on both 11.4 and 11.8
/// - Query translations verified with real MariaDB data
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MariaDbFeatureIntegrationTests
{
    // -- Database Lifecycle (with root access) --

    // -- Database Exists on MariaDB --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_database_exists_returns_true()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new SimpleContext(
            CreateOptions<SimpleContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 8, 0))));
        var creator = context.GetService<IRelationalDatabaseCreator>();
        Assert.True(await creator.ExistsAsync());
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_database_exists_returns_true()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);
        await using var context = new SimpleContext(
            CreateOptions<SimpleContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 4, 0))));
        var creator = context.GetService<IRelationalDatabaseCreator>();
        Assert.True(await creator.ExistsAsync());
    }

    // -- EnsureCreated + HasTables on MariaDB --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_ensure_created_and_has_tables()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var context = new SimpleContext(
            CreateOptions<SimpleContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 8, 0))));
        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `SimpleItems`;");

        await context.Database.EnsureCreatedAsync();
        var creator = context.GetService<IRelationalDatabaseCreator>();
        Assert.True(await creator.HasTablesAsync());

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `SimpleItems`;");
    }

    // -- MariaDB JSON Alias (longtext + CHECK) CRUD --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_json_alias_column_crud_roundtrip() =>
        await RunJsonAliasCrudTest(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_json_alias_column_crud_roundtrip() =>
        await RunJsonAliasCrudTest(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_json_alias_column_crud_roundtrip() =>
        await RunJsonAliasCrudTest(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_json_alias_column_crud_roundtrip() =>
        await RunJsonAliasCrudTest(IntegrationDatabaseTarget.MariaDb123);

    private static async Task RunJsonAliasCrudTest(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var serverVersion = GetServerVersion(target);
        await using var context = new JsonContext(CreateOptions<JsonContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbJsonItems`;");
        // MariaDB stores JSON as longtext with CHECK constraint.
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbJsonItems` (`Id` int NOT NULL AUTO_INCREMENT, `Data` longtext COLLATE utf8mb4_bin NOT NULL CHECK (JSON_VALID(`Data`)), PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            // Insert.
            var jsonValue = """{"name":"MariaDB","version":11}""";
            await context.Database.ExecuteSqlRawAsync("INSERT INTO `MdbJsonItems` (`Data`) VALUES ({0});", jsonValue);

            // Read back.
            var item = await context.Items.FirstAsync();
            Assert.Contains("MariaDB", item.Data, StringComparison.Ordinal);

            // Update.
            item.Data = """{"name":"MariaDB","version":12}""";
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var updated = await context.Items.FirstAsync();
            Assert.Contains("12", updated.Data, StringComparison.Ordinal);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbJsonItems`;");
        }
    }

    // -- MariaDB Native Sequence via EF Migration DDL --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_native_sequence_ddl_creates_and_drops()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        await using var context = new SimpleContext(CreateOptions<SimpleContext>(connectionString, serverVersion));
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var seqName = $"mdb_seq_{Guid.NewGuid():N}"[..25];
        // Backtick-escaped form for raw-SQL interpolation. The GUID:N format cannot
        // produce backticks today; escaping keeps the pattern safe-by-construction.
        var quotedSeqName = seqName.Replace("`", "``", StringComparison.Ordinal);

        // Generate CREATE SEQUENCE DDL.
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
            await context.Database.ExecuteSqlRawAsync(cmd.CommandText);
        }

        try
        {
            // Verify sequence works.
            await using var conn = new MySqlConnector.MySqlConnection(connectionString);
            await conn.OpenAsync();
            await using var fetchCmd = conn.CreateCommand();
            fetchCmd.CommandText = $"SELECT NEXT VALUE FOR `{quotedSeqName}`;";
            var value = Convert.ToInt64(
                await fetchCmd.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(100, value);

            // Generate DROP SEQUENCE DDL.
            var dropOps = generator.Generate([new DropSequenceOperation { Name = seqName }], context.Model);
            foreach (var cmd in dropOps)
            {
                // Safe: SQL emitted by IMigrationsSqlGenerator from a fixture-controlled DropSequenceOperation.
                await context.Database.ExecuteSqlRawAsync(cmd.CommandText);
            }
        }
        catch
        {
            // Cleanup on failure.
            await context.Database.ExecuteSqlRawAsync($"DROP SEQUENCE IF EXISTS `{quotedSeqName}`;");
            throw;
        }
    }

    // -- MariaDB 11.4 CRUD Baseline --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_crud_baseline()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 4, 0));
        await using var context = new CrudContext(CreateOptions<CrudContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbCrudItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbCrudItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(200) NOT NULL, `Score` double NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            // Create.
            context.Items.Add(
                new CrudItem
                {
                    Name = "First",
                    Score = 1.5
                });
            context.Items.Add(
                new CrudItem
                {
                    Name = "Second",
                    Score = 2.5
                });
            await context.SaveChangesAsync();
            Assert.Equal(2, await context.Items.CountAsync());

            // Read.
            var first = await context
                .Items.Where(e => e.Name == "First")
                .FirstAsync();
            Assert.Equal(1.5, first.Score, 1);

            // Update.
            first.Score = 99.9;
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var updated = await context.Items.FindAsync(first.Id);
            Assert.Equal(99.9, updated!.Score, 1);

            // Delete.
            context.Items.Remove(updated);
            await context.SaveChangesAsync();
            Assert.Equal(1, await context.Items.CountAsync());
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbCrudItems`;");
        }
    }

    // -- MariaDB 11.8 CRUD Baseline --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_crud_baseline()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        await using var context = new CrudContext(CreateOptions<CrudContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbCrudItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbCrudItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(200) NOT NULL, `Score` double NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Items.Add(
                new CrudItem
                {
                    Name = "Alpha",
                    Score = 10.0
                });
            context.Items.Add(
                new CrudItem
                {
                    Name = "Beta",
                    Score = 20.0
                });
            context.Items.Add(
                new CrudItem
                {
                    Name = "Gamma",
                    Score = 30.0
                });
            await context.SaveChangesAsync();

            var filtered = await context
                .Items.Where(e => e.Score > 15)
                .OrderBy(e => e.Name)
                .ToListAsync();
            Assert.Equal(2, filtered.Count);
            Assert.Equal("Beta", filtered[0].Name);
            Assert.Equal("Gamma", filtered[1].Name);

            context.Items.RemoveRange(filtered);
            await context.SaveChangesAsync();
            Assert.Equal(1, await context.Items.CountAsync());
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbCrudItems`;");
        }
    }

    // -- MariaDB DateTime/DateOnly Arithmetic --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_datetime_arithmetic_executes_correctly()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        await using var context = new TemporalContext(CreateOptions<TemporalContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbTemporal`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MdbTemporal` (`Id` int NOT NULL AUTO_INCREMENT, `Created` datetime(6) NOT NULL, `BirthDate` date NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Items.Add(
                new TemporalItem
                {
                    Created = new DateTime(2025, 3, 15, 9, 0, 0),
                    BirthDate = new DateOnly(1990, 6, 1)
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var addedDays = await context
                .Items.Select(e => e.Created.AddDays(10))
                .FirstAsync();
            Assert.Equal(25, addedDays.Day);

            var addedMonths = await context
                .Items.Select(e => e.BirthDate.AddMonths(6))
                .FirstAsync();
            Assert.Equal(12, addedMonths.Month);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MdbTemporal`;");
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

    private static string ReplaceDatabase(
        string connectionString,
        string newDatabase
    )
    {
        var csb = new MySqlConnectionStringBuilder(connectionString) { Database = newDatabase };
        return csb.ConnectionString;
    }

    private static async Task DropDatabaseIfExistsAsync(
        string connectionString,
        string databaseName
    )
    {
        var csb = new MySqlConnectionStringBuilder(connectionString) { Database = string.Empty };
        await using var connection = new MySqlConnection(csb.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`;";
        await command.ExecuteNonQueryAsync();
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
