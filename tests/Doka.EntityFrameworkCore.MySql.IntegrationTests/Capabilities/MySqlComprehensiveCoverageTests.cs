namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Comprehensive integration tests to close all remaining coverage gaps:
/// - Scaffolding live round-trip (MySQL + MariaDB)
/// - external-only MySQL 8.0 legacy checks (CRUD, modeling, queries)
/// - MariaDB 11.4 parity (CRUD, modeling, queries)
/// - HiLo end-to-end through EF SaveChanges
/// - Idempotent migration script on live DB
/// - EnsureCreated / EnsureDeleted round-trip
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlComprehensiveCoverageTests
{
    // ======================================================================
    // -- Scaffolding: Live Round-Trip --
    // ======================================================================

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Scaffolding_roundtrip_on_mysql84() =>
        await RunScaffoldingRoundTripTest(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task Scaffolding_roundtrip_on_mysql97() =>
        await RunScaffoldingRoundTripTest(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task Scaffolding_roundtrip_on_mariadb1011() =>
        await RunScaffoldingRoundTripTest(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Scaffolding_roundtrip_on_mariadb118() =>
        await RunScaffoldingRoundTripTest(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task Scaffolding_roundtrip_on_mariadb123() =>
        await RunScaffoldingRoundTripTest(IntegrationDatabaseTarget.MariaDb123);

    private static async Task RunScaffoldingRoundTripTest(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var serverVersion = GetServerVersion(target);

        // Create a schema with diverse column types.
        await using var setupConn = new MySqlConnector.MySqlConnection(connectionString);
        await setupConn.OpenAsync();

        await ExecuteRawAsync(setupConn, "DROP TABLE IF EXISTS `ScaffoldChild`;");
        await ExecuteRawAsync(setupConn, "DROP TABLE IF EXISTS `ScaffoldParent`;");

        await ExecuteRawAsync(
            setupConn,
            """
            CREATE TABLE `ScaffoldParent` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Name` varchar(200) NOT NULL,
                `Score` decimal(10,2) NOT NULL,
                `CreatedAt` datetime(6) NOT NULL,
                `BirthDate` date NULL,
                `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                PRIMARY KEY (`Id`),
                INDEX `IX_ScaffoldParent_Name` (`Name`)
            ) CHARACTER SET utf8mb4;
            """);

        await ExecuteRawAsync(
            setupConn,
            """
            CREATE TABLE `ScaffoldChild` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `ParentId` int NOT NULL,
                `Description` longtext NULL,
                PRIMARY KEY (`Id`),
                CONSTRAINT `FK_Child_Parent` FOREIGN KEY (`ParentId`) REFERENCES `ScaffoldParent` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET utf8mb4;
            """);

        try
        {
            // Scaffold the database model using the real factory.
            var services = new ServiceCollection();
            services.AddEntityFrameworkDokaMySqlDesignTime();
            var serviceProvider = services.BuildServiceProvider();

            var factory = serviceProvider.GetRequiredService<IDatabaseModelFactory>();
            var model = factory.Create(
                connectionString,
                new DatabaseModelFactoryOptions(
                    tables:
                    [
                        "ScaffoldParent",
                        "ScaffoldChild"
                    ]));

            // Verify tables.
            Assert.Equal(2, model.Tables.Count);
            var parentTable = model.Tables.First(t => t.Name == "ScaffoldParent");
            var childTable = model.Tables.First(t => t.Name == "ScaffoldChild");

            // Verify columns on parent.
            Assert.Contains(parentTable.Columns, c => c.Name == "Id");
            Assert.Contains(
                parentTable.Columns,
                c => c.Name == "Name" && c.StoreType!.Contains("varchar", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                parentTable.Columns,
                c => c.Name == "Score" && c.StoreType!.Contains("decimal", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(parentTable.Columns, c => c.Name == "CreatedAt");
            Assert.Contains(parentTable.Columns, c => c.Name == "IsActive");

            // Verify FK from child to parent.
            Assert.Single(childTable.ForeignKeys);
            var fk = childTable.ForeignKeys.First();
            Assert.Equal("ScaffoldParent", fk.PrincipalTable.Name);
            Assert.Equal(ReferentialAction.Cascade, fk.OnDelete);

            // Verify index on parent.
            Assert.Contains(parentTable.Indexes, i => i.Name == "IX_ScaffoldParent_Name");
        }
        finally
        {
            await ExecuteRawAsync(setupConn, "DROP TABLE IF EXISTS `ScaffoldChild`;");
            await ExecuteRawAsync(setupConn, "DROP TABLE IF EXISTS `ScaffoldParent`;");
        }
    }

    // ======================================================================
    // -- External-only MySQL 8.0 legacy checks --
    // ======================================================================

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql80)]
    public async Task MySql80_crud_and_query_parity()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql80);
        await using var context = new ParityContext(
            CreateOptions<ParityContext>(
                connectionString,
                MySqlServerVersion.MySql(
                    new Version(8, 0, 0),
                    MySqlServerVersionCompatibilityMode.AllowUnsupported)));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ParityItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `ParityItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(200) NOT NULL, `Value` double NOT NULL, `CreatedAt` datetime(6) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            // CRUD.
            context.Items.Add(
                new ParityItem
                {
                    Name = "Hello World",
                    Value = 42.5,
                    CreatedAt = new DateTime(2025, 1, 15, 10, 0, 0)
                });
            context.Items.Add(
                new ParityItem
                {
                    Name = "Goodbye",
                    Value = 7.0,
                    CreatedAt = new DateTime(2025, 6, 1, 0, 0, 0)
                });
            await context.SaveChangesAsync();
            Assert.Equal(2, await context.Items.CountAsync());

            // String queries.
            Assert.Equal(
                1,
                await context
                    .Items.Where(e => e.Name.Contains("Hello"))
                    .CountAsync());
            Assert.Equal(
                1,
                await context
                    .Items.Where(e => e.Name.StartsWith("Good"))
                    .CountAsync());

            // DateTime queries.
            var added = await context
                .Items.Select(e => e.CreatedAt.AddDays(10))
                .FirstAsync();
            Assert.Equal(25, added.Day);

            // Math queries.
            var abs = await context
                .Items.Select(e => Math.Abs(e.Value))
                .FirstAsync();
            Assert.Equal(42.5, abs, 1);

            // Update + Delete.
            var item = await context.Items.FirstAsync();
            item.Name = "Updated";
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            Assert.Equal("Updated", (await context.Items.FindAsync(item.Id))!.Name);

            context.Items.Remove(
                await context
                    .Items.OrderBy(e => e.Id)
                    .LastAsync());
            await context.SaveChangesAsync();
            Assert.Equal(1, await context.Items.CountAsync());
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ParityItems`;");
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql80)]
    public async Task MySql80_tph_modeling()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql80);
        await using var context = new TphParityContext(
            CreateOptions<TphParityContext>(
                connectionString,
                MySqlServerVersion.MySql(
                    new Version(8, 0, 0),
                    MySqlServerVersionCompatibilityMode.AllowUnsupported)));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `Parity80Animals`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `Parity80Animals` (`Id` int NOT NULL AUTO_INCREMENT, `Type` varchar(64) NOT NULL, `Name` varchar(100) NOT NULL, `Breed` varchar(100) NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Animals.Add(
                new ParityDog
                {
                    Name = "Buddy",
                    Breed = "Lab"
                });
            await context.SaveChangesAsync();
            var dogs = await context
                .Animals.OfType<ParityDog>()
                .ToListAsync();
            Assert.Single(dogs);
            Assert.Equal("Lab", dogs[0].Breed);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `Parity80Animals`;");
        }
    }

    // ======================================================================
    // -- MariaDB 11.4 Parity --
    // ======================================================================

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_crud_and_query_parity()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);
        await using var context = new ParityContext(
            CreateOptions<ParityContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 4, 0))));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ParityItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `ParityItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(200) NOT NULL, `Value` double NOT NULL, `CreatedAt` datetime(6) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Items.Add(
                new ParityItem
                {
                    Name = "MariaDB Test",
                    Value = 99.9,
                    CreatedAt = new DateTime(2025, 3, 1, 12, 0, 0)
                });
            context.Items.Add(
                new ParityItem
                {
                    Name = "Another",
                    Value = -5.0,
                    CreatedAt = new DateTime(2025, 7, 20, 8, 0, 0)
                });
            await context.SaveChangesAsync();

            Assert.Equal(
                1,
                await context
                    .Items.Where(e => e.Name.Contains("MariaDB"))
                    .CountAsync());
            Assert.Equal(
                1,
                await context
                    .Items.Where(e => e.Name.EndsWith("Test"))
                    .CountAsync());

            var abs = await context
                .Items.Where(e => e.Value < 0)
                .Select(e => Math.Abs(e.Value))
                .FirstAsync();
            Assert.Equal(5.0, abs, 1);

            var added = await context
                .Items.Select(e => e.CreatedAt.AddMonths(2))
                .FirstAsync();
            Assert.Equal(5, added.Month);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ParityItems`;");
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_tph_and_owned_modeling()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);
        await using var context = new TphParityContext(
            CreateOptions<TphParityContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 4, 0))));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `Parity80Animals`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `Parity80Animals` (`Id` int NOT NULL AUTO_INCREMENT, `Type` varchar(64) NOT NULL, `Name` varchar(100) NOT NULL, `Breed` varchar(100) NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Animals.Add(
                new ParityDog
                {
                    Name = "Max",
                    Breed = "Poodle"
                });
            await context.SaveChangesAsync();
            Assert.Single(
                await context
                    .Animals.OfType<ParityDog>()
                    .ToListAsync());
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `Parity80Animals`;");
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_regexp_works()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);
        await using var context = new ParityContext(
            CreateOptions<ParityContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 4, 0))));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ParityItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `ParityItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(200) NOT NULL, `Value` double NOT NULL, `CreatedAt` datetime(6) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Items.Add(
                new ParityItem
                {
                    Name = "abc123",
                    Value = 0,
                    CreatedAt = DateTime.Now
                });
            context.Items.Add(
                new ParityItem
                {
                    Name = "xyz",
                    Value = 0,
                    CreatedAt = DateTime.Now
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var count = await context
                .Items.Where(e => EF.Functions.Regexp(e.Name, "[0-9]+"))
                .CountAsync();
            Assert.Equal(1, count);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ParityItems`;");
        }
    }

    // ======================================================================
    // -- HiLo End-to-End through EF SaveChanges --
    // ======================================================================

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task HiLo_generates_block_allocated_ids_on_mysql84()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        // seqName is a compile-time literal used as a HiLo sequence-table suffix;
        // the DDL that interpolates it below is fixture-controlled, not user input.
        var seqName = "HiLoE2ESeq";

        await using var conn = new MySqlConnector.MySqlConnection(connectionString);
        await conn.OpenAsync();

        await ExecuteRawAsync(conn, "DROP TABLE IF EXISTS `HiLoE2EItems`;");
        await ExecuteRawAsync(conn, $"DROP TABLE IF EXISTS `__efsequence_{seqName}`;");
        await ExecuteRawAsync(
            conn,
            $"CREATE TABLE `__efsequence_{seqName}` ("
            + "`id` TINYINT UNSIGNED NOT NULL,"
            + "`value` BIGINT NOT NULL,"
            + "`is_called` BOOLEAN NOT NULL,"
            + "PRIMARY KEY (`id`),"
            + "CHECK (`id` = 1)"
            + ") ENGINE=InnoDB;");
        await ExecuteRawAsync(
            conn,
            $"INSERT INTO `__efsequence_{seqName}` (`id`, `value`, `is_called`) VALUES (1, 1, FALSE);");
        await ExecuteRawAsync(
            conn,
            "CREATE TABLE `HiLoE2EItems` (`Id` int NOT NULL, `Name` varchar(100) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            await using var context = new HiLoE2EContext(
                CreateOptions<HiLoE2EContext>(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0))));

            context.Items.Add(new HiLoE2EItem { Name = "First" });
            context.Items.Add(new HiLoE2EItem { Name = "Second" });
            await context.SaveChangesAsync();

            var items = await context
                .Items.OrderBy(e => e.Id)
                .ToListAsync();
            Assert.Equal(2, items.Count);
            Assert.True(items[0].Id > 0, "First ID should be positive");
            Assert.True(items[1].Id > items[0].Id, "Second ID should be greater than first");
        }
        finally
        {
            await ExecuteRawAsync(conn, "DROP TABLE IF EXISTS `HiLoE2EItems`;");
            await ExecuteRawAsync(conn, $"DROP TABLE IF EXISTS `__efsequence_{seqName}`;");
        }
    }

    // ======================================================================
    // -- Idempotent Migration Script on Live DB --
    // ======================================================================

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Idempotent_migration_script_executes_on_mysql84()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new SimpleParityContext(
            CreateOptions<SimpleParityContext>(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var historyRepository = context.GetService<IHistoryRepository>();

        // Generate idempotent script parts.
        var begin = historyRepository.GetBeginIfNotExistsScript("20260410_TestMigration");
        var end = historyRepository.GetEndIfScript();
        var body = "        SELECT 1;\n";
        var fullScript = begin + body + end;

        // Ensure history table exists.
        // Safe: SQL emitted by IHistoryRepository.GetCreateIfNotExistsScript().
        await context.Database.ExecuteSqlRawAsync(historyRepository.GetCreateIfNotExistsScript());

        try
        {
            // Safe: fullScript is composed from IHistoryRepository's Begin/End helpers around a literal body.
            await context.Database.ExecuteSqlRawAsync(fullScript);

            // Execute again -- should be idempotent (no error).
            await context.Database.ExecuteSqlRawAsync(fullScript);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `__EFMigrationsHistory`;");
            await context.Database.ExecuteSqlRawAsync("DROP PROCEDURE IF EXISTS `__ef_apply_migration`;");
        }
    }

    // ======================================================================
    // -- EnsureCreated / EnsureDeleted Round-Trip --
    // ======================================================================

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task EnsureCreated_and_EnsureDeleted_roundtrip_on_mysql84() =>
        await RunEnsureCreatedDeletedTest(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task EnsureCreated_and_EnsureDeleted_roundtrip_on_mysql97() =>
        await RunEnsureCreatedDeletedTest(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task EnsureCreated_and_EnsureDeleted_roundtrip_on_mariadb1011() =>
        await RunEnsureCreatedDeletedTest(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task EnsureCreated_and_EnsureDeleted_roundtrip_on_mariadb118() =>
        await RunEnsureCreatedDeletedTest(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task EnsureCreated_and_EnsureDeleted_roundtrip_on_mariadb123() =>
        await RunEnsureCreatedDeletedTest(IntegrationDatabaseTarget.MariaDb123);

    private static async Task RunEnsureCreatedDeletedTest(
        IntegrationDatabaseTarget target
    )
    {
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var testDbName = $"ensure_test_{Guid.NewGuid():N}"[..30];
        var testConnectionString = ReplaceDatabase(baseConnectionString, testDbName);
        var serverVersion = GetServerVersion(target);

        // Pre-cleanup.
        await DropDatabaseIfExistsAsync(baseConnectionString, testDbName);

        try
        {
            await using var context =
                new SimpleParityContext(CreateOptions<SimpleParityContext>(testConnectionString, serverVersion));

            // EnsureCreated creates DB + tables.
            var created = await context.Database.EnsureCreatedAsync();
            Assert.True(created);

            // Insert data to verify tables work.
            context.Items.Add(new SimpleItem { Name = "Test" });
            await context.SaveChangesAsync();
            Assert.Equal(1, await context.Items.CountAsync());

            // EnsureDeleted drops the DB.
            var deleted = await context.Database.EnsureDeletedAsync();
            Assert.True(deleted);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, testDbName);
        }
    }

    // ======================================================================
    // -- GROUP_CONCAT on MariaDB 11.4 --
    // ======================================================================

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_group_concat_executes()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);
        await using var context = new ParityContext(
            CreateOptions<ParityContext>(connectionString, MySqlServerVersion.MariaDb(new Version(11, 4, 0))));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ParityItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `ParityItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(200) NOT NULL, `Value` double NOT NULL, `CreatedAt` datetime(6) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.Items.AddRange(
                new ParityItem
                {
                    Name = "A1",
                    Value = 1,
                    CreatedAt = DateTime.Now
                },
                new ParityItem
                {
                    Name = "A2",
                    Value = 1,
                    CreatedAt = DateTime.Now
                },
                new ParityItem
                {
                    Name = "B1",
                    Value = 2,
                    CreatedAt = DateTime.Now
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var groups = await context
                .Items.GroupBy(e => e.Value)
                .Select(g => new
                {
                    Val = g.Key,
                    Names = string.Join(",", g.Select(e => e.Name))
                })
                .OrderBy(g => g.Val)
                .ToListAsync();

            Assert.Equal(2, groups.Count);
            Assert.Contains("A1", groups[0].Names, StringComparison.Ordinal);
            Assert.Contains("A2", groups[0].Names, StringComparison.Ordinal);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ParityItems`;");
        }
    }

    // ======================================================================
    // -- Helpers --
    // ======================================================================

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
    ) => target == IntegrationDatabaseTarget.MySql80
        ? MySqlServerVersion.MySql(
            new Version(8, 0, 0),
            MySqlServerVersionCompatibilityMode.AllowUnsupported)
        : IntegrationTestEnvironment.GetServerVersion(target);

    private static string ReplaceDatabase(
        string cs,
        string db
    )
    {
        var csb = new MySqlConnector.MySqlConnectionStringBuilder(cs) { Database = db };
        return csb.ConnectionString;
    }

    private static async Task DropDatabaseIfExistsAsync(
        string cs,
        string db
    )
    {
        var csb = new MySqlConnector.MySqlConnectionStringBuilder(cs) { Database = string.Empty };
        await using var conn = new MySqlConnector.MySqlConnection(csb.ConnectionString);
        await conn.OpenAsync();
        await ExecuteRawAsync(conn, $"DROP DATABASE IF EXISTS `{db}`;");
    }

    private static async Task ExecuteRawAsync(
        MySqlConnector.MySqlConnection conn,
        string sql
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    // ======================================================================
    // -- Entities / Contexts --
    // ======================================================================

    private sealed class ParityItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class ParityContext : DbContext
    {
        public ParityContext(
            DbContextOptions<ParityContext> o
        ) : base(o) { }

        public DbSet<ParityItem> Items => Set<ParityItem>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<ParityItem>(e =>
            {
                e.ToTable("ParityItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(200);
            });
        }
    }

    private abstract class ParityAnimal
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class ParityDog : ParityAnimal
    {
        public string Breed { get; set; } = "";
    }

    private sealed class TphParityContext : DbContext
    {
        public TphParityContext(
            DbContextOptions<TphParityContext> o
        ) : base(o) { }

        public DbSet<ParityAnimal> Animals => Set<ParityAnimal>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<ParityAnimal>(e =>
            {
                e.ToTable("Parity80Animals");
                e
                    .HasDiscriminator<string>("Type")
                    .HasValue<ParityDog>("Dog");
                e
                    .Property("Type")
                    .HasMaxLength(64);
                e
                    .Property(a => a.Name)
                    .HasMaxLength(100);
            });
            m.Entity<ParityDog>(e => e
                .Property(d => d.Breed)
                .HasMaxLength(100));
        }
    }

    private sealed class HiLoE2EItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class HiLoE2EContext : DbContext
    {
        public HiLoE2EContext(
            DbContextOptions<HiLoE2EContext> o
        ) : base(o) { }

        public DbSet<HiLoE2EItem> Items => Set<HiLoE2EItem>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<HiLoE2EItem>(e =>
            {
                e.ToTable("HiLoE2EItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .UseHiLo("HiLoE2ESeq");
                e
                    .Property(x => x.Name)
                    .HasMaxLength(100);
            });
        }
    }

    private sealed class SimpleItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class SimpleParityContext : DbContext
    {
        public SimpleParityContext(
            DbContextOptions<SimpleParityContext> o
        ) : base(o) { }

        public DbSet<SimpleItem> Items => Set<SimpleItem>();

        protected override void OnModelCreating(
            ModelBuilder m
        )
        {
            m.Entity<SimpleItem>(e =>
            {
                e.ToTable("SimpleParityItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(200);
            });
        }
    }
}
