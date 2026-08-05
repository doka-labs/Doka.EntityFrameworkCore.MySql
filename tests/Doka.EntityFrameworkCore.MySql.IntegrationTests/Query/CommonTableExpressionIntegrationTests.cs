namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the common-table-expression contract against every supported live engine.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class CommonTableExpressionIntegrationTests
{
    private const string TableName = "IntCteItems";
    private const string ComposedCteSql =
        """
        WITH RECURSIVE `numbers` (`Value`) AS (
            SELECT 1
            UNION ALL
            SELECT `Value` + 1
            FROM `numbers`
            WHERE `Value` < @upperBound
        ),
        `selected` AS (
            SELECT `items`.*
            FROM `IntCteItems` AS `items`
            INNER JOIN `numbers` ON `numbers`.`Value` = `items`.`Id`
        )
        SELECT *
        FROM `selected`
        """;
    private const string UpdateCteSql =
        """
        WITH `selected` AS (
            SELECT `Id`
            FROM `IntCteItems`
            WHERE `Score` >= @minimumScore
        )
        UPDATE `IntCteItems` AS `items`
        INNER JOIN `selected` ON `selected`.`Id` = `items`.`Id`
        SET `items`.`Category` = @category;
        """;

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Cte_contract_executes_on_mysql84() => RunQueryContractAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Cte_contract_executes_on_mariadb114() => RunQueryContractAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Cte_contract_executes_on_mariadb118() => RunQueryContractAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Cte_update_executes_on_mysql84()
    {
        await using var context = CreateContext(IntegrationDatabaseTarget.MySql84);
        await SetupAsync(context);

        try
        {
            await SeedAsync(context);

            var affectedRows = await context.Database.ExecuteSqlRawAsync(
                UpdateCteSql,
                new MySqlParameter("@minimumScore", 20),
                new MySqlParameter("@category", "updated"));

            Assert.Equal(2, affectedRows);

            var updatedNames = await context
                .Items.Where(item => item.Category == "updated")
                .OrderBy(item => item.Id)
                .Select(item => item.Name)
                .ToListAsync();

            Assert.Equal(["bravo", "charlie"], updatedNames);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    private static async Task RunQueryContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var context = CreateContext(target);
        await SetupAsync(context);

        try
        {
            await SeedAsync(context);
            context.ChangeTracker.Clear();

            // The first CTE proves recursion while the second proves that a CTE can
            // consume another CTE. The outer LINQ predicate verifies composition.
            var trackedItems = await CreateComposedQuery(context, 2)
                .Where(item => item.Category == "selected")
                .OrderBy(item => item.Id)
                .ToListAsync();

            Assert.Equal(["alpha", "bravo"], trackedItems.Select(item => item.Name));
            Assert.All(
                trackedItems,
                item => Assert.Equal(
                    EntityState.Unchanged,
                    context.Entry(item)
                        .State));

            context.ChangeTracker.Clear();

            var untrackedItems = CreateComposedQuery(context, 3)
                .AsNoTracking()
                .OrderByDescending(item => item.Id)
                .ToList();

            Assert.Equal(["charlie", "bravo", "alpha"], untrackedItems.Select(item => item.Name));
            Assert.Empty(context.ChangeTracker.Entries());

            Assert.Equal(3, await CreateComposedQuery(context, 3).CountAsync());
            Assert.Equal(60, await CreateComposedQuery(context, 3).SumAsync(item => item.Score));

            await AssertCancellationContractAsync(target);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    private static async Task AssertCancellationContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var interceptor = new CommandCancellationProbeInterceptor();

        await using var context = CreateContext(target, interceptor);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateComposedQuery(context, 3)
            .CountAsync(cancellationToken));

        Assert.Equal(cancellationToken, exception.CancellationToken);
        Assert.Equal(cancellationToken, interceptor.ReceivedCancellationToken);
        Assert.Equal(1, interceptor.InvocationCount);
    }

    private static IQueryable<CteItem> CreateComposedQuery(
        CteContext context,
        int upperBound
    ) =>
        // SQL identifiers are deliberately fixed in the trusted command text.
        // Only runtime data is parameterized; relational providers cannot bind
        // table or column names through database parameters.
        context.Items.FromSqlRaw(ComposedCteSql, new MySqlParameter("@upperBound", upperBound));

    private static CteContext CreateContext(
        IntegrationDatabaseTarget target,
        params IInterceptor[] interceptors
    )
    {
        var optionsBuilder = new DbContextOptionsBuilder<CteContext>().UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target));

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new CteContext(optionsBuilder.Options);
    }

    private static async Task SetupAsync(
        CteContext context
    )
    {
        await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;");
        await context.Database.ExecuteSqlRawAsync(
            $"""
             CREATE TABLE `{TableName}` (
                 `Id` int NOT NULL AUTO_INCREMENT,
                 `Name` varchar(64) NOT NULL,
                 `Score` int NOT NULL,
                 `Category` varchar(32) NOT NULL,
                 CONSTRAINT `PK_{TableName}` PRIMARY KEY (`Id`)
             ) CHARACTER SET utf8mb4;
             """);
    }

    private static async Task SeedAsync(
        CteContext context
    )
    {
        context.Items.AddRange(
            new CteItem
            {
                Name = "alpha",
                Score = 10,
                Category = "selected"
            },
            new CteItem
            {
                Name = "bravo",
                Score = 20,
                Category = "selected"
            },
            new CteItem
            {
                Name = "charlie",
                Score = 30,
                Category = "other"
            });

        await context.SaveChangesAsync();
    }

    private static Task<int> CleanupAsync(
        CteContext context
    ) => context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;");

    private sealed class CteItem
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public int Score { get; set; }

        public string Category { get; set; } = "";
    }

    private sealed class CteContext : DbContext
    {
        public CteContext(
            DbContextOptions<CteContext> options
        ) : base(options) { }

        public DbSet<CteItem> Items => Set<CteItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CteItem>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Name)
                    .HasMaxLength(64);
                entity
                    .Property(item => item.Category)
                    .HasMaxLength(32);
            });
        }
    }
}
