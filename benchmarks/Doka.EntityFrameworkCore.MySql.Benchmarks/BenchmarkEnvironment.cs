namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class BenchmarkEnvironment
{
    // Identity values are not benchmark outputs. Keeping cleanup DML-only
    // avoids carrying AUTO_INCREMENT DDL and storage-engine flush work into
    // the next measured sample.
    private const string ClearSaveChangesTableSql = "DELETE FROM `SaveChangeEntities`;";

    private static readonly Lock s_initializationGate = new();
    private static readonly BenchmarkDatabaseTarget s_target = BenchmarkDatabaseTarget.Current;
    private const string DatabaseName = "benchmark_suite";
    private static bool s_initialized;

    public static string DatabaseNameValue => DatabaseName;

    public static string TargetIdValue => s_target.TargetId;

    public static string DisplayNameValue => s_target.DisplayName;

    public static string EngineFamilyValue => s_target.EngineFamily;

    public static string ServerVersionText => s_target.ServerVersion.ToString(3);

    public static MySqlServerVersion ServerVersionValue => s_target.CreateServerVersion();

    public static bool SupportsNativeSequencesValue
    {
        get
        {
            var family = s_target.IsMariaDb
                ? EngineFamily.MariaDb
                : EngineFamily.MySql;

            var providerProfile = new ProviderProfile(
                EngineProfileTable.Resolve(family, s_target.ServerVersion));

            return providerProfile.GetSupport(ProviderCapability.Sequences) == ProviderSupportStatus.Native;
        }
    }

    public static DbContextOptions<TContext> CreateOptions<TContext>(
        bool connectionPooling = true,
        bool retryOnFailure = false,
        bool serviceProviderCaching = true,
        int? maxBatchSize = null
    )
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();

        builder.UseMySql(
            CreateConnectionString(DatabaseName, connectionPooling),
            ServerVersionValue,
            mySqlOptions =>
            {
                mySqlOptions.UseNetTopologySuite();

                if (retryOnFailure)
                {
                    mySqlOptions.EnableRetryOnFailure();
                }

                if (maxBatchSize is not null)
                {
                    mySqlOptions.MaxBatchSize(maxBatchSize.Value);
                }
            });
        builder.EnableServiceProviderCaching(serviceProviderCaching);

        return builder.Options;
    }

    public static BenchmarkContext CreateContext() => new(CreateOptions<BenchmarkContext>());

    public static void EnsureInitialized()
    {
        lock (s_initializationGate)
        {
            if (s_initialized)
            {
                return;
            }

            ResetDatabase(DatabaseName);

            using var context = CreateContext();
            context.Database.EnsureCreated();

            if (!context.BasicEntities.Any())
            {
                context.BasicEntities.AddRange(CreateBasicEntities());
                context.SpatialEntities.AddRange(CreateSpatialEntities());
                context.SaveChanges();
            }

            s_initialized = true;
        }
    }

    public static void ResetSaveChangesTable()
    {
        using var context = CreateContext();
        context.Database.ExecuteSqlRaw(ClearSaveChangesTableSql);
    }

    public static async Task ResetSaveChangesTableAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var context = CreateContext();
        await context
            .Database.ExecuteSqlRawAsync(ClearSaveChangesTableSql, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<string> ReadServerVersionAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = new MySqlConnection(CreateConnectionString(DatabaseName));
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION();";
        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return result as string
            ?? throw new InvalidOperationException("The benchmark server did not report its version.");
    }

    public static string CreateConnectionString(
        string databaseName,
        bool pooling = true
    )
    {
        var builder = new MySqlConnectionStringBuilder(s_target.CreateConnectionString(databaseName))
        {
            Pooling = pooling,
        };

        return builder.ConnectionString;
    }

    private static void ResetDatabase(
        string databaseName
    )
    {
        var builder = new MySqlConnectionStringBuilder(CreateConnectionString(databaseName))
        {
            Database = string.Empty,
        };

        builder.Remove("Database");
        builder.Remove("Initial Catalog");

        using var connection = new MySqlConnection(builder.ConnectionString);
        connection.Open();

        ExecuteServerCommand(connection, $"DROP DATABASE IF EXISTS `{databaseName}`;");
        ExecuteServerCommand(connection, $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4;");
    }

    private static void ExecuteServerCommand(
        MySqlConnection connection,
        string commandText
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static IEnumerable<BasicBenchmarkEntity> CreateBasicEntities()
    {
        var baseDate = new DateTime(
            2024,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);

        return Enumerable
            .Range(1, 1000)
            .Select(index => new BasicBenchmarkEntity
            {
                Name = $"benchmark-{index}",
                CreatedAt = baseDate.AddDays(index % 365),
                Payload = $$"""{"kind":"benchmark","index":{{index}},"active":true}""",
            });
    }

    private static IEnumerable<SpatialBenchmarkEntity> CreateSpatialEntities() => Enumerable
        .Range(1, 1000)
        .Select(index => new SpatialBenchmarkEntity
        {
            Location = new Point(13.4050 + ((index % 20) * 0.01d), 52.5200 + ((index % 20) * 0.01d))
            {
                SRID = 4326,
            },
        });
}
