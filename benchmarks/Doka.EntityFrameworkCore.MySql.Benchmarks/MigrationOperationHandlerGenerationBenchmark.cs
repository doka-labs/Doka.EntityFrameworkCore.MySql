namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the complete migration-operation handler generation path,
/// including provider baseline rendering and structured command preservation.
/// </summary>
/// <remarks>
/// Input operations and provider service graphs are prepared outside the
/// measured path. One invocation covers MySQL and MariaDB with 1, 100, and
/// 1,000 operations so allocation growth remains visible in one stable control.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class MigrationOperationHandlerGenerationBenchmark
{
    private const string ConnectionString =
        "Server=localhost;Database=benchmark_handlers;User ID=root;Password=benchmark;";

    private static readonly int[] s_operationCounts = [1, 100, 1000];

    private ServiceProvider _mariaDbServices = null!;
    private ServiceProvider _mySqlServices = null!;
    private DbContextOptions<HandlerGenerationContext> _mariaDbOptions = null!;
    private DbContextOptions<HandlerGenerationContext> _mySqlOptions = null!;
    private IReadOnlyList<MigrationOperation>[] _operationPopulations = null!;

    /// <summary>
    /// Builds immutable operation populations and isolated engine service
    /// graphs outside the measured path.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _operationPopulations = s_operationCounts
            .Select(CreateOperations)
            .ToArray();

        _mySqlServices = CreateServiceProvider();
        _mariaDbServices = CreateServiceProvider();
        _mySqlOptions = CreateOptions(_mySqlServices, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        _mariaDbOptions = CreateOptions(_mariaDbServices, MySqlServerVersion.MariaDb(new Version(11, 4, 0)));
    }

    /// <summary>
    /// Releases the isolated provider service graphs.
    /// </summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _mySqlServices.Dispose();
        _mariaDbServices.Dispose();
    }

    /// <summary>
    /// Generates opaque, ordinary baseline, and scoped baseline commands for
    /// the complete operation-count and engine-family matrix.
    /// </summary>
    /// <returns>
    /// A checksum of command counts and SQL lengths that prevents dead-code
    /// elimination.
    /// </returns>
    [Benchmark]
    public long GenerateHandlerOperationMatrix()
    {
        using var mySqlContext = new HandlerGenerationContext(_mySqlOptions);
        using var mariaDbContext = new HandlerGenerationContext(_mariaDbOptions);
        long checksum = 0;

        foreach (var operations in _operationPopulations)
        {
            checksum += Generate(mySqlContext, operations);
            checksum += Generate(mariaDbContext, operations);
        }

        return checksum;
    }

    internal long GenerateOperationPopulation(
        int populationIndex
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(populationIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(populationIndex, _operationPopulations.Length);

        using var mySqlContext = new HandlerGenerationContext(_mySqlOptions);
        using var mariaDbContext = new HandlerGenerationContext(_mariaDbOptions);
        var operations = _operationPopulations[populationIndex];

        return Generate(mySqlContext, operations) + Generate(mariaDbContext, operations);
    }

    private static long Generate(
        HandlerGenerationContext context,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, context.Model);

        long checksum = commands.Count;

        foreach (var command in commands)
        {
            checksum += command.CommandText.Length;
        }

        return checksum;
    }

    private static MigrationOperation[] CreateOperations(
        int operationCount
    )
    {
        var operations = new MigrationOperation[operationCount];

        for (var index = 0; index < operationCount; index++)
        {
            operations[index] = new HandlerGenerationOperation(index);
        }

        return operations;
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkDokaMySql();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IMySqlMigrationOperationHandler, HandlerGenerationOperationHandler>());

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static DbContextOptions<HandlerGenerationContext> CreateOptions(
        IServiceProvider serviceProvider,
        MySqlServerVersion serverVersion
    ) => new DbContextOptionsBuilder<HandlerGenerationContext>()
        .UseInternalServiceProvider(serviceProvider)
        .UseMySql(ConnectionString, serverVersion)
        .Options;

    private sealed class HandlerGenerationContext : DbContext
    {
        public HandlerGenerationContext(
            DbContextOptions<HandlerGenerationContext> options
        ) : base(options) { }
    }

    private sealed class HandlerGenerationOperation : MigrationOperation
    {
        public HandlerGenerationOperation(
            int ordinal
        )
        {
            Ordinal = ordinal;
        }

        public int Ordinal { get; }
    }

    private sealed class HandlerGenerationOperationHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "benchmarks.full_generation";

        public Type OperationType => typeof(HandlerGenerationOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var operation = (HandlerGenerationOperation)context.Operation;
            var ordinary = context.RenderStandardOperation(
                new SqlOperation
                {
                    Sql = $"SELECT {operation.Ordinal};",
                    SuppressTransaction = true,
                });

            var scoped = context.RenderStandardOperation(
                new AddColumnOperation
                {
                    Table = "HandlerBenchmarkEntries",
                    Name = $"Value{operation.Ordinal}",
                    ClrType = typeof(string),
                    ColumnType = "varchar(64)",
                    IsNullable = true,
                    Comment = "path\\segment",
                });

            return MySqlMigrationOperationResult.Generated(
                [
                    ordinary.Single(),
                    scoped.Single(),
                    MySqlMigrationCommandSpec.Create("SELECT 1;"),
                ],
                "generated");
        }
    }
}
