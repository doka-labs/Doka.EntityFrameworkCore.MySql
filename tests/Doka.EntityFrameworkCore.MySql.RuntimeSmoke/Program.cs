namespace Doka.EntityFrameworkCore.MySql.RuntimeSmoke;

public static class Program
{
    private static readonly MySqlServerVersion s_mySql84 = MySqlServerVersion.MySql(new Version(8, 4, 0));
    private const string RuntimeSmokeHost = "127.0.0.1";
    private const int RuntimeSmokePort = 33068;
    private const string RuntimeSmokeUser = "root";
    private const string RuntimeSmokePassword = "root_password";
    private const string BasicDatabaseName = "runtime_smoke_basic";
    private const string SpatialDatabaseName = "runtime_smoke_spatial";

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification =
            "The smoke app intentionally exercises the documented EF Core trimmed runtime path with compiled models.")]
    [UnconditionalSuppressMessage(
        "Aot",
        "IL3050",
        Justification =
            "The smoke app intentionally exercises the documented EF Core NativeAOT runtime path with compiled models.")]
    public static async Task<int> Main()
    {
        var supportsQueryExecution = RuntimeFeature.IsDynamicCodeSupported;

        await RunBasicCompiledModelSmokeAsync(supportsQueryExecution);

        if (supportsQueryExecution)
        {
            await RunSpatialCompiledModelSmokeAsync(true);
        }

        Console.WriteLine("Runtime smoke OK.");

        return 0;
    }

    [RequiresUnreferencedCode(
        "This smoke verifies the documented trimmed runtime path for the compiled-model baseline.")]
    [RequiresDynamicCode("This smoke verifies the documented NativeAOT runtime path for the compiled-model baseline.")]
    private static async Task RunBasicCompiledModelSmokeAsync(
        bool supportsQueryExecution
    )
    {
        await ResetDatabaseAsync(BasicDatabaseName)
            .ConfigureAwait(false);

        var connectionString = CreateConnectionString(BasicDatabaseName);
        var options = new DbContextOptionsBuilder<BasicSmokeContext>()
            .UseMySql(connectionString, s_mySql84)
            .UseModel(CompiledModelAccessor.GetBasicModel())
            .Options;

        using var context = new BasicSmokeContext(options);
        if (supportsQueryExecution)
        {
            await context
                .Database.EnsureCreatedAsync()
                .ConfigureAwait(false);
        }
        else
        {
            await CreateBasicNativeAotSchemaAsync(context)
                .ConfigureAwait(false);
        }

        context.BasicEntities.Add(
            new BasicSmokeEntity
            {
                Name = "runtime-smoke",
            });
        await context
            .SaveChangesAsync()
            .ConfigureAwait(false);

        if (!supportsQueryExecution)
        {
            if (context
                    .ChangeTracker.Entries<BasicSmokeEntity>()
                    .Single()
                    .Entity.Id
                <= 0)
            {
                throw new InvalidOperationException(
                    "The NativeAOT basic smoke path did not complete the expected generated-value insert baseline.");
            }

            return;
        }

        if (!await HasRuntimeSmokeBasicEntityAsync(context))
        {
            throw new InvalidOperationException(
                "The compiled-model basic smoke query did not execute through the MySQL provider path.");
        }
    }

    [RequiresUnreferencedCode(
        "This smoke verifies the documented trimmed runtime path for the spatial compiled-model baseline.")]
    [RequiresDynamicCode(
        "This smoke verifies the documented NativeAOT runtime path for the spatial compiled-model baseline.")]
    private static async Task RunSpatialCompiledModelSmokeAsync(
        bool supportsQueryExecution
    )
    {
        await ResetDatabaseAsync(SpatialDatabaseName)
            .ConfigureAwait(false);

        var connectionString = CreateConnectionString(SpatialDatabaseName);
        var options = new DbContextOptionsBuilder<SpatialSmokeContext>()
            .UseMySql(connectionString, s_mySql84, mySqlOptions => mySqlOptions.UseNetTopologySuite())
            .UseModel(CompiledModelAccessor.GetSpatialModel())
            .Options;

        await using var context = new SpatialSmokeContext(options);
        if (supportsQueryExecution)
        {
            await context
                .Database.EnsureCreatedAsync()
                .ConfigureAwait(false);
        }
        else
        {
            await CreateSpatialNativeAotSchemaAsync(context)
                .ConfigureAwait(false);
        }

        context.SpatialEntities.Add(
            new SpatialSmokeEntity
            {
                Location = new Point(13.4050, 52.5200)
                {
                    SRID = 4326,
                },
            });
        await context
            .SaveChangesAsync()
            .ConfigureAwait(false);

        if (!supportsQueryExecution)
        {
            if (context
                    .ChangeTracker.Entries<SpatialSmokeEntity>()
                    .Single()
                    .Entity.Id
                <= 0)
            {
                throw new InvalidOperationException(
                    "The NativeAOT spatial smoke path did not complete the expected generated-value insert baseline.");
            }

            return;
        }

        if (!await HasRuntimeSmokeSpatialEntityAsync(context))
        {
            throw new InvalidOperationException(
                "The compiled-model spatial smoke query did not execute through the approved spatial translation path.");
        }
    }

    private static string CreateConnectionString(
        string databaseName
    )
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable(
            "DOKA_RUNTIME_SMOKE_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            // Release candidates own an isolated container on a dynamic port.
            // Replacing only the database keeps credentials and transport
            // options in the runner-owned connection contract.
            var configuredBuilder = new MySqlConnectionStringBuilder(configuredConnectionString)
            {
                Database = databaseName,
            };

            return configuredBuilder.ConnectionString;
        }

        return $"Server={RuntimeSmokeHost};Port={RuntimeSmokePort};"
            + $"Database={databaseName};User ID={RuntimeSmokeUser};"
            + $"Password={RuntimeSmokePassword};";
    }

    private static async Task ResetDatabaseAsync(
        string databaseName
    )
    {
        var builder = new MySqlConnectionStringBuilder(CreateConnectionString(databaseName))
        {
            Database = string.Empty,
        };

        builder.Remove("Database");
        builder.Remove("Initial Catalog");

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await ExecuteServerCommandAsync(connection, $"DROP DATABASE IF EXISTS `{databaseName}`;")
            .ConfigureAwait(false);
        await ExecuteServerCommandAsync(connection, $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4;")
            .ConfigureAwait(false);
    }

    private static async Task ExecuteServerCommandAsync(
        MySqlConnection connection,
        string commandText
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task CreateBasicNativeAotSchemaAsync(
        BasicSmokeContext context
    )
    {
        var entityType = context.Model.FindEntityType(typeof(BasicSmokeEntity))
            ?? throw new InvalidOperationException(
                "The NativeAOT basic smoke path could not resolve the compiled entity type.");
        var storeObject = StoreObjectIdentifier.Table(
            entityType.GetTableName()
            ?? throw new InvalidOperationException(
                "The NativeAOT basic smoke path could not resolve the compiled table name."),
            entityType.GetSchema());
        var idColumnName = entityType
                .FindProperty(nameof(BasicSmokeEntity.Id))
                ?.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                "The NativeAOT basic smoke path could not resolve the compiled Id column name.");
        var nameColumnName = entityType
                .FindProperty(nameof(BasicSmokeEntity.Name))
                ?.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                "The NativeAOT basic smoke path could not resolve the compiled Name column name.");

        var createTableSql = $"""
                              CREATE TABLE {DelimitIdentifier(storeObject.Name)} (
                                  {DelimitIdentifier(idColumnName)} int NOT NULL AUTO_INCREMENT,
                                  {DelimitIdentifier(nameColumnName)} longtext NOT NULL,
                                  PRIMARY KEY ({DelimitIdentifier(idColumnName)})
                              ) CHARACTER SET utf8mb4;
                              """;

        await context
            .Database.ExecuteSqlRawAsync(createTableSql)
            .ConfigureAwait(false);
    }

    private static async Task CreateSpatialNativeAotSchemaAsync(
        SpatialSmokeContext context
    )
    {
        var entityType = context.Model.FindEntityType(typeof(SpatialSmokeEntity))
            ?? throw new InvalidOperationException(
                "The NativeAOT spatial smoke path could not resolve the compiled entity type.");
        var storeObject = StoreObjectIdentifier.Table(
            entityType.GetTableName()
            ?? throw new InvalidOperationException(
                "The NativeAOT spatial smoke path could not resolve the compiled table name."),
            entityType.GetSchema());
        var idColumnName = entityType
                .FindProperty(nameof(SpatialSmokeEntity.Id))
                ?.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                "The NativeAOT spatial smoke path could not resolve the compiled Id column name.");
        var locationColumnName = entityType
                .FindProperty(nameof(SpatialSmokeEntity.Location))
                ?.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                "The NativeAOT spatial smoke path could not resolve the compiled Location column name.");

        var createTableSql = $"""
                              CREATE TABLE {DelimitIdentifier(storeObject.Name)} (
                                  {DelimitIdentifier(idColumnName)} int NOT NULL AUTO_INCREMENT,
                                  {DelimitIdentifier(locationColumnName)} point NOT NULL /*!80003 SRID 4326 */,
                                  PRIMARY KEY ({DelimitIdentifier(idColumnName)})
                              ) CHARACTER SET utf8mb4;
                              """;

        await context
            .Database.ExecuteSqlRawAsync(createTableSql)
            .ConfigureAwait(false);
    }

    private static string DelimitIdentifier(
        string identifier
    ) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    private static async Task<bool> HasRuntimeSmokeBasicEntityAsync(
        BasicSmokeContext context
    )
    {
        await using var enumerator = GetRuntimeSmokeBasicEntities(context)
            .GetAsyncEnumerator();

        return await enumerator.MoveNextAsync();
    }

    private static IAsyncEnumerable<BasicSmokeEntity> GetRuntimeSmokeBasicEntities(
        BasicSmokeContext context
    )
    {
        return context
            .BasicEntities.Where(entity => entity.Name == "runtime-smoke")
            .AsAsyncEnumerable();
    }

    private static async Task<bool> HasRuntimeSmokeSpatialEntityAsync(
        SpatialSmokeContext context
    )
    {
        await using var enumerator = GetRuntimeSmokeSpatialEntities(context)
            .GetAsyncEnumerator();

        return await enumerator.MoveNextAsync();
    }

    private static IAsyncEnumerable<SpatialSmokeEntity> GetRuntimeSmokeSpatialEntities(
        SpatialSmokeContext context
    )
    {
        return context
            .SpatialEntities.Where(entity => EF.Functions.DistanceSphere(entity.Location, entity.Location) < 1d)
            .AsAsyncEnumerable();
    }

    public sealed class BasicSmokeContext : DbContext
    {
        [RequiresUnreferencedCode(
            "The compiled-model runtime smoke intentionally exercises the trimmed EF Core runtime path.")]
        [RequiresDynamicCode(
            "The compiled-model runtime smoke intentionally exercises the NativeAOT EF Core runtime path.")]
        public BasicSmokeContext() { }

        [RequiresUnreferencedCode(
            "The compiled-model runtime smoke intentionally exercises the trimmed EF Core runtime path.")]
        [RequiresDynamicCode(
            "The compiled-model runtime smoke intentionally exercises the NativeAOT EF Core runtime path.")]
        public BasicSmokeContext(
            DbContextOptions<BasicSmokeContext> options
        ) : base(options) { }

        public DbSet<BasicSmokeEntity> BasicEntities => Set<BasicSmokeEntity>();

        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseMySql(CreateConnectionString(BasicDatabaseName), s_mySql84);
            }
        }
    }

    public sealed class SpatialSmokeContext : DbContext
    {
        [RequiresUnreferencedCode(
            "The spatial compiled-model runtime smoke intentionally exercises the trimmed EF Core runtime path.")]
        [RequiresDynamicCode(
            "The spatial compiled-model runtime smoke intentionally exercises the NativeAOT EF Core runtime path.")]
        public SpatialSmokeContext() { }

        [RequiresUnreferencedCode(
            "The spatial compiled-model runtime smoke intentionally exercises the trimmed EF Core runtime path.")]
        [RequiresDynamicCode(
            "The spatial compiled-model runtime smoke intentionally exercises the NativeAOT EF Core runtime path.")]
        public SpatialSmokeContext(
            DbContextOptions<SpatialSmokeContext> options
        ) : base(options) { }

        public DbSet<SpatialSmokeEntity> SpatialEntities => Set<SpatialSmokeEntity>();

        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseMySql(
                    CreateConnectionString(SpatialDatabaseName),
                    s_mySql84,
                    mySqlOptions => mySqlOptions.UseNetTopologySuite());
            }
        }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialSmokeEntity>(entity =>
            {
                entity
                    .Property(item => item.Location)
                    .HasSrid(4326);
                entity
                    .HasIndex(item => item.Location)
                    .IsSpatial();
            });
        }
    }

    public sealed class BasicSmokeEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public sealed class SpatialSmokeEntity
    {
        public int Id { get; set; }

        public Point Location { get; set; } = null!;
    }
}
