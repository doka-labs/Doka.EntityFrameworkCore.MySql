namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlQueryTranslationIntegrationTests
{
    private static DbContextOptions<QueryContext> CreateOptions(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<QueryContext>();
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);
        builder.UseMySql(connectionString, serverVersion);

        return builder.Options;
    }

    private static DbContextOptions<JsonQueryContext> CreateJsonOptions(
        string connectionString,
        IntegrationDatabaseTarget target,
        IInterceptor? interceptor = null
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<JsonQueryContext>();
        builder.UseMySql(connectionString, IntegrationTestEnvironment.GetServerVersion(target));

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

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
                 `SignedValue` int NOT NULL,
                 `SignedLongValue` bigint NOT NULL,
                 `UnsignedValue` int unsigned NOT NULL,
                 `UnsignedLongValue` bigint unsigned NOT NULL,
                 `ShiftCount` int NOT NULL,
                 `Token` binary(16) NOT NULL,
                 CONSTRAINT `PK_{TableName}` PRIMARY KEY (`Id`)
             ) CHARACTER SET utf8mb4;
             """);
    }

    private static async Task CleanupAsync(
        QueryContext context
    ) => await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;");

    private static async Task CleanupJsonAsync(
        JsonQueryContext context
    ) => await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `IntJsonItems`;");

    private sealed class QueryItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public string Category { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int SignedValue { get; set; }
        public long SignedLongValue { get; set; }
        public uint UnsignedValue { get; set; }
        public ulong UnsignedLongValue { get; set; }
        public int ShiftCount { get; set; }
        public Guid Token { get; set; }
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

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            CommandTexts.Add(command.CommandText);

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
