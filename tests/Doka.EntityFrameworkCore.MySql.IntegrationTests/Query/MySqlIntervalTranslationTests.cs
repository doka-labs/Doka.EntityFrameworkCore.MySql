namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Live integration coverage for the parametrized INTERVAL translation. The previous
/// implementation only supported constant intervals (.AddDays(7)) because it emitted
/// the value through a raw SqlFragmentExpression; parametrized intervals
/// (.AddDays(parameter)) silently fell back to client evaluation. The sentinel function
/// pattern routes both shapes through DATE_ADD(dt, INTERVAL ? UNIT) so the database
/// performs the arithmetic regardless of whether the interval is a constant or a
/// parameter. The tests assert the result is filtered server-side via ToQueryString +
/// EXPLAIN-style behavior: when the query enumerates only the matching row, the WHERE
/// clause must have evaluated on the server.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlIntervalTranslationTests
{
    private const string TableName = "IntervalQueryItems";

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Parametrized_AddDays_filters_server_side_on_mysql84() =>
        await RunParametrizedAddDaysAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Parametrized_AddDays_filters_server_side_on_mariadb118() =>
        await RunParametrizedAddDaysAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Parametrized_AddHours_filters_server_side_on_mysql84() =>
        await RunParametrizedAddHoursAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Constant_AddDays_still_filters_server_side_on_mysql84() =>
        await RunConstantAddDaysAsync(IntegrationDatabaseTarget.MySql84);

    private static async Task RunParametrizedAddDaysAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await using var context = new IntervalContext(CreateOptions(connectionString, target));
        await SetupAsync(context);

        try
        {
            var anchor = new DateTime(
                2026,
                1,
                1,
                12,
                0,
                0,
                DateTimeKind.Utc);
            context.Items.Add(new IntervalItem { CreatedAt = anchor.AddDays(-5) });
            context.Items.Add(new IntervalItem { CreatedAt = anchor.AddDays(5) });
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var offsetDays = 1;
            var sql = context
                .Items.Where(i => i.CreatedAt.AddDays(offsetDays) > anchor)
                .ToQueryString();

            Assert.Contains("DATE_ADD(", sql, StringComparison.Ordinal);
            Assert.Contains("INTERVAL ", sql, StringComparison.Ordinal);
            Assert.Contains(" Day)", sql, StringComparison.OrdinalIgnoreCase);

            var matched = await context
                .Items.Where(i => i.CreatedAt.AddDays(offsetDays) > anchor)
                .CountAsync()
                .ConfigureAwait(false);

            Assert.Equal(1, matched);
        }
        finally
        {
            await TearDownAsync(context)
                .ConfigureAwait(false);
        }
    }

    private static async Task RunParametrizedAddHoursAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await using var context = new IntervalContext(CreateOptions(connectionString, target));
        await SetupAsync(context);

        try
        {
            var anchor = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            context.Items.Add(new IntervalItem { CreatedAt = anchor.AddHours(-2) });
            context.Items.Add(new IntervalItem { CreatedAt = anchor.AddHours(2) });
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var offsetHours = 1;
            var sql = context
                .Items.Where(i => i.CreatedAt.AddHours(offsetHours) > anchor)
                .ToQueryString();

            Assert.Contains("DATE_ADD(", sql, StringComparison.Ordinal);
            Assert.Contains("INTERVAL ", sql, StringComparison.Ordinal);
            Assert.Contains(" Hour)", sql, StringComparison.OrdinalIgnoreCase);

            var matched = await context
                .Items.Where(i => i.CreatedAt.AddHours(offsetHours) > anchor)
                .CountAsync()
                .ConfigureAwait(false);

            Assert.Equal(1, matched);
        }
        finally
        {
            await TearDownAsync(context)
                .ConfigureAwait(false);
        }
    }

    private static async Task RunConstantAddDaysAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await using var context = new IntervalContext(CreateOptions(connectionString, target));
        await SetupAsync(context);

        try
        {
            var anchor = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            context.Items.Add(new IntervalItem { CreatedAt = anchor.AddDays(-5) });
            context.Items.Add(new IntervalItem { CreatedAt = anchor.AddDays(5) });
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var sql = context
                .Items.Where(i => i.CreatedAt.AddDays(1) > anchor)
                .ToQueryString();

            Assert.Contains("DATE_ADD(", sql, StringComparison.Ordinal);
            Assert.Contains("INTERVAL ", sql, StringComparison.Ordinal);
            Assert.Contains(" Day)", sql, StringComparison.OrdinalIgnoreCase);

            var matched = await context
                .Items.Where(i => i.CreatedAt.AddDays(1) > anchor)
                .CountAsync()
                .ConfigureAwait(false);

            Assert.Equal(1, matched);
        }
        finally
        {
            await TearDownAsync(context)
                .ConfigureAwait(false);
        }
    }

    private static DbContextOptions<IntervalContext> CreateOptions(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        var serverVersion = target == IntegrationDatabaseTarget.MariaDb118
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        var builder = new DbContextOptionsBuilder<IntervalContext>();
        builder.UseMySql(connectionString, serverVersion);
        return builder.Options;
    }

    private static async Task SetupAsync(
        IntervalContext context
    )
    {
        await using var connection = new MySqlConnection(context.Database.GetConnectionString());
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;"
            + $"CREATE TABLE `{TableName}` ("
            + "  `Id` INT NOT NULL AUTO_INCREMENT,"
            + "  `CreatedAt` DATETIME(6) NOT NULL,"
            + "  PRIMARY KEY (`Id`)"
            + ") CHARACTER SET utf8mb4;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task TearDownAsync(
        IntervalContext context
    )
    {
        await using var connection = new MySqlConnection(context.Database.GetConnectionString());
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private sealed class IntervalItem
    {
        public int Id { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    private sealed class IntervalContext : DbContext
    {
        public IntervalContext(
            DbContextOptions<IntervalContext> options
        ) : base(options) { }

        public DbSet<IntervalItem> Items => Set<IntervalItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<IntervalItem>(builder =>
            {
                builder.ToTable(TableName);
                builder.HasKey(item => item.Id);
                builder
                    .Property(item => item.CreatedAt)
                    .HasColumnType("datetime(6)");
            });
        }
    }
}
