namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the common-table-expression contract against every supported live engine.
/// </summary>
[IntegrationDatabaseTest]
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
    public Task Cte_contract_executes_on_mysql84()
        => RunQueryContractAsync(IntegrationDatabaseTarget.MySql84, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Cte_contract_executes_on_mysql97()
        => RunQueryContractAsync(IntegrationDatabaseTarget.MySql97, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Cte_contract_executes_on_mariadb1011()
        => RunQueryContractAsync(IntegrationDatabaseTarget.MariaDb1011, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Cte_contract_executes_on_mariadb114()
        => RunQueryContractAsync(IntegrationDatabaseTarget.MariaDb114, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Cte_contract_executes_on_mariadb118()
        => RunQueryContractAsync(IntegrationDatabaseTarget.MariaDb118, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Cte_contract_executes_on_mariadb123()
        => RunQueryContractAsync(IntegrationDatabaseTarget.MariaDb123, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Cte_update_executes_on_mysql84()
        => RunUpdateContractAsync(IntegrationDatabaseTarget.MySql84, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Cte_update_executes_on_mysql97()
        => RunUpdateContractAsync(IntegrationDatabaseTarget.MySql97, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Cte_update_executes_on_mariadb123()
        => RunUpdateContractAsync(IntegrationDatabaseTarget.MariaDb123, CancellationToken.None);

    /// <summary>Runs the update contract integration scenario.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task RunUpdateContractAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateContext(target);
        await SetupAsync(context, cancellationToken);

        try
        {
            await SeedAsync(context, cancellationToken);

            var affectedRows = await context.Database.ExecuteSqlRawAsync(
                UpdateCteSql,
                new object[] { new MySqlParameter("@minimumScore", 20), new MySqlParameter("@category", "updated") },
                cancellationToken);

            Assert.Equal(2, affectedRows);

            var updatedNames = await context
                .Items
                .Where(item => item.Category == "updated")
                .OrderBy(item => item.Id)
                .Select(item => item.Name)
                .ToListAsync(cancellationToken: cancellationToken);

            Assert.Equal(["bravo", "charlie"], updatedNames);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    /// <summary>Runs the query contract integration scenario.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task RunQueryContractAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateContext(target);
        await SetupAsync(context, cancellationToken);

        try
        {
            await SeedAsync(context, cancellationToken);
            context.ChangeTracker.Clear();

            // The first CTE proves recursion while the second proves that a CTE can
            // consume another CTE. The outer LINQ predicate verifies composition.
            var trackedItems = await CreateComposedQuery(context, 2)
                .Where(item => item.Category == "selected")
                .OrderBy(item => item.Id)
                .ToListAsync(cancellationToken: cancellationToken);

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

            Assert.Equal(
                3,
                await CreateComposedQuery(context, 3)
                    .CountAsync(cancellationToken: cancellationToken));

            Assert.Equal(
                60,
                await CreateComposedQuery(context, 3)
                    .SumAsync(
                        item => item.Score,
                        cancellationToken: cancellationToken));

            await AssertCancellationContractAsync(target, cancellationToken);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    /// <summary>Verifies the cancellation contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertCancellationContractAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var interceptor = new CommandCancellationProbeInterceptor();

        await using var context = CreateContext(target, interceptor);

        // WHY: Keep the deliberate probe token distinct while honoring cancellation from its caller.
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var probeCancellationToken = cancellationSource.Token;

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateComposedQuery(context, 3)
            .CountAsync(probeCancellationToken));

        // Assert
        Assert.Equal(probeCancellationToken, exception.CancellationToken);
        Assert.Equal(probeCancellationToken, interceptor.ReceivedCancellationToken);
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
        var optionsBuilder = IntegrationTestDbContextOptions
            .Create<CteContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target));

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new CteContext(optionsBuilder.Options);
    }

    /// <summary>Creates the integration tables with cancellable database work.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task SetupAsync(
        CteContext context,
        CancellationToken cancellationToken
    )
    {
        await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{TableName}`;",
            cancellationToken: cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            $"""
            CREATE TABLE `{TableName}` (
                `Id` int NOT NULL AUTO_INCREMENT,
                `Name` varchar(64) NOT NULL,
                `Score` int NOT NULL,
                `Category` varchar(32) NOT NULL,
                CONSTRAINT `PK_{TableName}` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """,
            cancellationToken: cancellationToken);
    }

    /// <summary>Seeds the integration tables with cancellable database work.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task SeedAsync(
        CteContext context,
        CancellationToken cancellationToken
    )
    {
        context.Items.AddRange(
            new CteItem
            {
                Name = "alpha",
                Score = 10,
                Category = "selected",
            },
            new CteItem
            {
                Name = "bravo",
                Score = 20,
                Category = "selected",
            },
            new CteItem
            {
                Name = "charlie",
                Score = 30,
                Category = "other",
            });

        await context.SaveChangesAsync(cancellationToken: cancellationToken);
    }

    // WHY: Failure cleanup must complete after cancellation so later tests do not inherit leftover tables.
    private static Task<int> CleanupAsync(
        CteContext context
    ) => context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{TableName}`;",
        CancellationToken.None);

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
