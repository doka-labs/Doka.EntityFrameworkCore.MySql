namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies query translations against live MySQL 8.4 and MariaDB 11.8 with real data.
/// Covers string methods, DateTime/DateOnly/TimeOnly arithmetic, Math, GROUP_CONCAT,
/// REGEXP, JSON functions.
/// </summary>
public sealed class MySqlQueryTranslationIntegrationTests
{
    private const string TableName = "IntQueryItems";

    // ── String Methods ──

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task String_methods_execute_correctly_on_mysql84()
    {
        await RunStringMethodTests(IntegrationDatabaseTarget.MySql84);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task String_methods_execute_correctly_on_mariadb118()
    {
        await RunStringMethodTests(IntegrationDatabaseTarget.MariaDb118);
    }

    private static async Task RunStringMethodTests(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));
        await SetupAsync(context);

        try
        {
            context.Items.Add(
                new QueryItem
                {
                    Name = "  Hello World  ",
                    Value = 42.5,
                    Category = "A",
                    CreatedAt = new DateTime(2025, 6, 15, 10, 30, 0)
                });
            context.Items.Add(
                new QueryItem
                {
                    Name = "Goodbye",
                    Value = 7.0,
                    Category = "A",
                    CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0)
                });
            context.Items.Add(
                new QueryItem
                {
                    Name = "Hello Again",
                    Value = -3.0,
                    Category = "B",
                    CreatedAt = new DateTime(2024, 12, 25, 18, 0, 0)
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Contains
            var containsResult = await context
                .Items.Where(e => e.Name.Contains("Hello"))
                .CountAsync();
            Assert.Equal(2, containsResult);

            // StartsWith
            var startsResult = await context
                .Items.Where(e => e.Name.StartsWith("Good"))
                .CountAsync();
            Assert.Equal(1, startsResult);

            // EndsWith
            var endsResult = await context
                .Items.Where(e => e.Name.EndsWith("Again"))
                .CountAsync();
            Assert.Equal(1, endsResult);

            // ToUpper/ToLower run inside an IQueryable expression tree — EF translates
            // them to UPPER()/LOWER() SQL, the CLR culture is never consulted.
#pragma warning disable CA1304, CA1311
            var upperName = await context
                .Items.Where(e => e.Id == 1)
                .Select(e => e.Name.ToUpper())
                .FirstAsync();
            Assert.Contains("HELLO", upperName, StringComparison.Ordinal);

            var lowerName = await context
                .Items.Where(e => e.Id == 2)
                .Select(e => e.Name.ToLower())
                .FirstAsync();
#pragma warning restore CA1304, CA1311
            Assert.Equal("goodbye", lowerName);

            // Trim
            var trimmed = await context
                .Items.Where(e => e.Id == 1)
                .Select(e => e.Name.Trim())
                .FirstAsync();
            Assert.Equal("Hello World", trimmed);

            // Substring
            var sub = await context
                .Items.Where(e => e.Id == 2)
                .Select(e => e.Name.Substring(4))
                .FirstAsync();
            Assert.Equal("bye", sub);

            // Replace
            var replaced = await context
                .Items.Where(e => e.Id == 2)
                .Select(e => e.Name.Replace("Good", "See you "))
                .FirstAsync();
            Assert.Equal("See you bye", replaced);

            // IndexOf
            var idx = await context
                .Items.Where(e => e.Id == 1)
                .Select(e => e.Name.IndexOf("World", StringComparison.Ordinal))
                .FirstAsync();
            Assert.True(idx > 0);

            // string.Length
            var len = await context
                .Items.Where(e => e.Id == 2)
                .Select(e => e.Name.Length)
                .FirstAsync();
            Assert.Equal(7, len);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    // ── DateTime Arithmetic ──

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task DateTime_arithmetic_executes_correctly_on_mysql84()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new QueryContext(CreateOptions(connectionString, IntegrationDatabaseTarget.MySql84));
        await SetupAsync(context);

        try
        {
            context.Items.Add(
                new QueryItem
                {
                    Name = "dt",
                    Value = 0,
                    Category = "X",
                    CreatedAt = new DateTime(2025, 1, 1, 12, 0, 0)
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // AddDays
            var result = await context
                .Items.Select(e => e.CreatedAt.AddDays(10))
                .FirstAsync();
            Assert.Equal(11, result.Day);

            // AddMonths
            result = await context
                .Items.Select(e => e.CreatedAt.AddMonths(2))
                .FirstAsync();
            Assert.Equal(3, result.Month);

            // AddYears
            result = await context
                .Items.Select(e => e.CreatedAt.AddYears(1))
                .FirstAsync();
            Assert.Equal(2026, result.Year);

            // AddHours
            result = await context
                .Items.Select(e => e.CreatedAt.AddHours(5))
                .FirstAsync();
            Assert.Equal(17, result.Hour);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    // ── Math Functions ──

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Math_functions_execute_correctly_on_mysql84()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new QueryContext(CreateOptions(connectionString, IntegrationDatabaseTarget.MySql84));
        await SetupAsync(context);

        try
        {
            context.Items.Add(
                new QueryItem
                {
                    Name = "m",
                    Value = -16.7,
                    Category = "X",
                    CreatedAt = DateTime.Now
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var abs = await context
                .Items.Select(e => Math.Abs(e.Value))
                .FirstAsync();
            Assert.Equal(16.7, abs, 1);

            var ceil = await context
                .Items.Select(e => Math.Ceiling(e.Value))
                .FirstAsync();
            Assert.Equal(-16.0, ceil, 1);

            var floor = await context
                .Items.Select(e => Math.Floor(e.Value))
                .FirstAsync();
            Assert.Equal(-17.0, floor, 1);

            var sqrt = await context
                .Items.Select(e => Math.Sqrt(Math.Abs(e.Value)))
                .FirstAsync();
            Assert.True(sqrt > 4.0);

            var round = await context
                .Items.Select(e => Math.Round(e.Value, 0))
                .FirstAsync();
            Assert.Equal(-17.0, round, 1);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    // ── GROUP_CONCAT ──

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Group_concat_executes_on_mysql84()
    {
        await RunGroupConcatTest(IntegrationDatabaseTarget.MySql84);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Group_concat_executes_on_mariadb118()
    {
        await RunGroupConcatTest(IntegrationDatabaseTarget.MariaDb118);
    }

    private static async Task RunGroupConcatTest(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));
        await SetupAsync(context);

        try
        {
            context.Items.Add(
                new QueryItem
                {
                    Name = "Alpha",
                    Value = 1,
                    Category = "G1",
                    CreatedAt = DateTime.Now
                });
            context.Items.Add(
                new QueryItem
                {
                    Name = "Beta",
                    Value = 2,
                    Category = "G1",
                    CreatedAt = DateTime.Now
                });
            context.Items.Add(
                new QueryItem
                {
                    Name = "Gamma",
                    Value = 3,
                    Category = "G2",
                    CreatedAt = DateTime.Now
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var groups = await context
                .Items
                .GroupBy(e => e.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Names = string.Join(",", g.Select(e => e.Name))
                })
                .OrderBy(g => g.Category)
                .ToListAsync();

            Assert.Equal(2, groups.Count);
            Assert.Contains("Alpha", groups[0].Names, StringComparison.Ordinal);
            Assert.Contains("Beta", groups[0].Names, StringComparison.Ordinal);
            Assert.Contains("Gamma", groups[1].Names, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    // ── REGEXP ──

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Regexp_executes_on_mysql84()
    {
        await RunRegexpTest(IntegrationDatabaseTarget.MySql84);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Regexp_executes_on_mariadb118()
    {
        await RunRegexpTest(IntegrationDatabaseTarget.MariaDb118);
    }

    private static async Task RunRegexpTest(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));
        await SetupAsync(context);

        try
        {
            context.Items.Add(
                new QueryItem
                {
                    Name = "abc123",
                    Value = 0,
                    Category = "R",
                    CreatedAt = DateTime.Now
                });
            context.Items.Add(
                new QueryItem
                {
                    Name = "xyz",
                    Value = 0,
                    Category = "R",
                    CreatedAt = DateTime.Now
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var matches = await context
                .Items
                .Where(e => EF.Functions.Regexp(e.Name, "[0-9]+"))
                .CountAsync();

            Assert.Equal(1, matches);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    // ── JSON Functions ──

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Json_depth_and_length_execute_on_mysql84()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        await using var context = new JsonQueryContext(CreateJsonOptions(connectionString));
        await CleanupJsonAsync(context);

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS `IntJsonItems` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Data` json NOT NULL,
                    CONSTRAINT `PK_IntJsonItems` PRIMARY KEY (`Id`)
                ) CHARACTER SET utf8mb4;
                """);

            var jsonValue = """{"a":1,"b":{"c":2}}""";
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO `IntJsonItems` (`Data`) VALUES ({0});",
                jsonValue);

            var depth = await context
                .Items
                .Select(e => EF.Functions.JsonDepth(e.Data))
                .FirstAsync();
            Assert.Equal(3, depth);

            var length = await context
                .Items
                .Select(e => EF.Functions.JsonLength(e.Data))
                .FirstAsync();
            Assert.Equal(2, length);
        }
        finally
        {
            await CleanupJsonAsync(context);
        }
    }

    // ── Helpers ──

    private static DbContextOptions<QueryContext> CreateOptions(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        var builder = new DbContextOptionsBuilder<QueryContext>();
        var serverVersion = target is IntegrationDatabaseTarget.MariaDb114 or IntegrationDatabaseTarget.MariaDb118
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));
        builder.UseMySql(connectionString, serverVersion);
        return builder.Options;
    }

    private static DbContextOptions<JsonQueryContext> CreateJsonOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<JsonQueryContext>();
        builder.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    private static async Task SetupAsync(
        QueryContext context
    )
    {
        await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;");
        await context.Database.ExecuteSqlRawAsync(
            $"""
             CREATE TABLE `{TableName}` (
                 `Id` int NOT NULL AUTO_INCREMENT,
                 `Name` varchar(200) NOT NULL,
                 `Value` double NOT NULL,
                 `Category` varchar(50) NOT NULL,
                 `CreatedAt` datetime(6) NOT NULL,
                 CONSTRAINT `PK_{TableName}` PRIMARY KEY (`Id`)
             ) CHARACTER SET utf8mb4;
             """);
    }

    private static async Task CleanupAsync(
        QueryContext context
    )
    {
        await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;");
    }

    private static async Task CleanupJsonAsync(
        JsonQueryContext context
    )
    {
        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `IntJsonItems`;");
    }

    private sealed class QueryItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public string Category { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private sealed class QueryContext : DbContext
    {
        public QueryContext(
            DbContextOptions<QueryContext> options
        ) : base(options) { }

        public DbSet<QueryItem> Items => Set<QueryItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<QueryItem>(e =>
            {
                e.ToTable(TableName);
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Name)
                    .HasMaxLength(200);
                e
                    .Property(x => x.Category)
                    .HasMaxLength(50);
            });
        }
    }

    private sealed class JsonItem
    {
        public int Id { get; set; }
        public string Data { get; set; } = "{}";
    }

    private sealed class JsonQueryContext : DbContext
    {
        public JsonQueryContext(
            DbContextOptions<JsonQueryContext> options
        ) : base(options) { }

        public DbSet<JsonItem> Items => Set<JsonItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonItem>(e =>
            {
                e.ToTable("IntJsonItems");
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }
}
