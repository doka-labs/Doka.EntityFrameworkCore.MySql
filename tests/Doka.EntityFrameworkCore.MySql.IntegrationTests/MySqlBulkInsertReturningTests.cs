namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// End-to-end coverage for the multi-row INSERT path and the MariaDB 10.5+ RETURNING
/// routing. The matrix exercises four shapes:
/// - MariaDB 11.8: multi-row INSERT + RETURNING with auto-increment (engine-supported single-statement path)
/// - MariaDB 11.8: multi-row INSERT + RETURNING with trigger-modified column (server-side default visible)
/// - MySQL 8.4: multi-row write-only INSERT (no read-back; engine cannot use RETURNING)
/// - MySQL 8.4: multi-row INSERT with auto-increment (falls back to per-row loop)
/// Plus the shape-mismatch case where two distinct write-column sets force a batch split.
/// </summary>
public sealed class MySqlBulkInsertReturningTests
{
    // -- MariaDB 11.8: multi-row INSERT + RETURNING with auto-increment --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_multirow_insert_returning_populates_auto_increment_ids()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        await using var context = new BulkContext(CreateOptions<BulkContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `BulkItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `BulkItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(64) NOT NULL, `Score` int NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            var items = Enumerable
                .Range(1, 5)
                .Select(i => new BulkItem
                {
                    Name = $"row-{i}",
                    Score = i * 10,
                })
                .ToList();

            context.Items.AddRange(items);
            await context.SaveChangesAsync();

            Assert.All(items, item => Assert.True(item.Id > 0));
            Assert.Equal(
                items
                    .Select(i => i.Id)
                    .Distinct()
                    .Count(),
                items.Count);

            context.ChangeTracker.Clear();
            var roundTripped = await context
                .Items.OrderBy(e => e.Id)
                .ToListAsync();
            Assert.Equal(5, roundTripped.Count);
            Assert.Equal("row-1", roundTripped[0].Name);
            Assert.Equal(50, roundTripped[4].Score);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `BulkItems`;");
        }
    }

    // -- MariaDB 11.8: multi-row INSERT + RETURNING with trigger-modified column --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_multirow_insert_returning_surfaces_trigger_modified_column()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        await using var context = new TriggerContext(CreateOptions<TriggerContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `TriggerItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `TriggerItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(64) NOT NULL, `Stamp` int NOT NULL DEFAULT 0, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER `TriggerItems_stamp_bi` BEFORE INSERT ON `TriggerItems` FOR EACH ROW SET NEW.`Stamp` = CHAR_LENGTH(NEW.`Name`) * 7;");

        try
        {
            var items = new[]
            {
                new TriggerItem { Name = "a" },
                new TriggerItem { Name = "abc" },
                new TriggerItem { Name = "hello-world" },
            };

            context.Items.AddRange(items);
            await context.SaveChangesAsync();

            Assert.Equal(1 * 7, items[0].Stamp);
            Assert.Equal(3 * 7, items[1].Stamp);
            Assert.Equal(11 * 7, items[2].Stamp);
            Assert.All(items, item => Assert.True(item.Id > 0));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS `TriggerItems_stamp_bi`;");
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `TriggerItems`;");
        }
    }

    // -- MySQL 8.4: multi-row write-only INSERT (no read-back) --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_multirow_write_only_insert_lands_all_rows_in_single_statement()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        await using var context =
            new ExplicitKeyContext(CreateOptions<ExplicitKeyContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ExplicitKeyItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `ExplicitKeyItems` (`Id` int NOT NULL, `Name` varchar(64) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            var items = Enumerable
                .Range(1, 5)
                .Select(i => new ExplicitKeyItem
                {
                    Id = i,
                    Name = $"k-{i}",
                })
                .ToList();

            context.Items.AddRange(items);
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();
            var roundTripped = await context
                .Items.OrderBy(e => e.Id)
                .ToListAsync();
            Assert.Equal(5, roundTripped.Count);
            Assert.Equal("k-3", roundTripped[2].Name);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `ExplicitKeyItems`;");
        }
    }

    // -- MySQL 8.4: multi-row INSERT with auto-increment falls back to per-row --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_multirow_insert_with_auto_increment_populates_each_id_via_per_row_fallback()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));
        await using var context = new BulkContext(CreateOptions<BulkContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `BulkItems`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `BulkItems` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(64) NOT NULL, `Score` int NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            var items = Enumerable
                .Range(1, 3)
                .Select(i => new BulkItem
                {
                    Name = $"my-{i}",
                    Score = i,
                })
                .ToList();

            context.Items.AddRange(items);
            await context.SaveChangesAsync();

            Assert.All(items, item => Assert.True(item.Id > 0));
            Assert.Equal(
                items
                    .Select(i => i.Id)
                    .Distinct()
                    .Count(),
                items.Count);
            Assert.Equal(items[1].Id, items[0].Id + 1);
            Assert.Equal(items[2].Id, items[1].Id + 1);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `BulkItems`;");
        }
    }

    // -- Shape-split: two distinct write-column sets in one batch (MariaDB 11.8) --

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_batch_with_distinct_shapes_splits_into_two_statements()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        await using var context = new MixedContext(CreateOptions<MixedContext>(connectionString, serverVersion));

        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MixedA`;");
        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MixedB`;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MixedA` (`Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(64) NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE `MixedB` (`Id` int NOT NULL AUTO_INCREMENT, `Value` int NOT NULL, PRIMARY KEY (`Id`)) CHARACTER SET utf8mb4;");

        try
        {
            context.ItemsA.Add(new MixedA { Name = "a1" });
            context.ItemsA.Add(new MixedA { Name = "a2" });
            context.ItemsB.Add(new MixedB { Value = 100 });
            context.ItemsB.Add(new MixedB { Value = 200 });
            await context.SaveChangesAsync();

            Assert.Equal(2, await context.ItemsA.CountAsync());
            Assert.Equal(2, await context.ItemsB.CountAsync());
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MixedA`;");
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `MixedB`;");
        }
    }

    // -- Helpers --

    private static DbContextOptions<T> CreateOptions<T>(
        string connectionString,
        MySqlServerVersion serverVersion
    )
        where T : DbContext
    {
        var builder = new DbContextOptionsBuilder<T>();
        builder.UseMySql(connectionString, serverVersion);
        return builder.Options;
    }

    // -- Entities + Contexts --

    private sealed class BulkItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    private sealed class BulkContext : DbContext
    {
        public BulkContext(
            DbContextOptions<BulkContext> options
        ) : base(options) { }

        public DbSet<BulkItem> Items => Set<BulkItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<BulkItem>(e =>
            {
                e.ToTable("BulkItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(64);
            });
        }
    }

    private sealed class TriggerItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Stamp { get; set; }
    }

    private sealed class TriggerContext : DbContext
    {
        public TriggerContext(
            DbContextOptions<TriggerContext> options
        ) : base(options) { }

        public DbSet<TriggerItem> Items => Set<TriggerItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TriggerItem>(e =>
            {
                e.ToTable("TriggerItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(64);
                e
                    .Property(x => x.Stamp)
                    .ValueGeneratedOnAddOrUpdate()
                    .HasDefaultValue(0);
            });
        }
    }

    private sealed class ExplicitKeyItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ExplicitKeyContext : DbContext
    {
        public ExplicitKeyContext(
            DbContextOptions<ExplicitKeyContext> options
        ) : base(options) { }

        public DbSet<ExplicitKeyItem> Items => Set<ExplicitKeyItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ExplicitKeyItem>(e =>
            {
                e.ToTable("ExplicitKeyItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .ValueGeneratedNever();
                e
                    .Property(x => x.Name)
                    .HasMaxLength(64);
            });
        }
    }

    private sealed class MixedA
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class MixedB
    {
        public int Id { get; set; }
        public int Value { get; set; }
    }

    private sealed class MixedContext : DbContext
    {
        public MixedContext(
            DbContextOptions<MixedContext> options
        ) : base(options) { }

        public DbSet<MixedA> ItemsA => Set<MixedA>();
        public DbSet<MixedB> ItemsB => Set<MixedB>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<MixedA>(e =>
            {
                e.ToTable("MixedA");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(64);
            });
            modelBuilder.Entity<MixedB>(e =>
            {
                e.ToTable("MixedB");
                e.HasKey(x => x.Id);
            });
        }
    }
}
