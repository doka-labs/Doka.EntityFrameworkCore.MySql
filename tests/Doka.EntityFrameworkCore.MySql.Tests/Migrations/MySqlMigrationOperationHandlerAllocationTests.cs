using Doka.EntityFrameworkCore.MySql.Benchmarks;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Enforces allocation and scaling budgets for the complete migration-operation
/// handler generation path.
/// </summary>
public sealed class MySqlMigrationOperationHandlerAllocationTests
{
    private const string ConnectionString =
        "Server=localhost;Database=scoped_allocation_tests;User ID=root;Password=tests;";
    private const long MaximumMatrixAllocationBytes = 48L * 1024L * 1024L;
    private const double MaximumNormalizedGrowthRatio = 1.10D;
    private const double MaximumHandlerScopedAllocationRatio = 0.75D;
    private const double MaximumProviderScopedAllocationRatio = 1.75D;
    private const long ScopedAllocationFixedAllowanceBytes = 24L * 1024L;
    private const long MaximumProviderIncrementalBytesPerScope = 2304L;

    private static readonly string[] s_cleanupCommands =
    [
        "SET @doka_scope_a = NULL;",
        "SET @doka_scope_b = NULL;",
    ];

    private static readonly string[] s_setupCommands =
    [
        "SET @doka_scope_a = 1;",
        "SET @doka_scope_b = 1;",
    ];

    [Fact]
    public void Complete_handler_generation_stays_bounded_and_scales_linearly()
    {
        var benchmark = new MigrationOperationHandlerGenerationBenchmark();
        benchmark.GlobalSetup();

        try
        {
            _ = benchmark.GenerateHandlerOperationMatrix();
            var matrixAllocation = MeasureAllocation(benchmark.GenerateHandlerOperationMatrix);
            var hundredAllocation = MeasureAllocation(() => benchmark.GenerateOperationPopulation(1));
            var thousandAllocation = MeasureAllocation(() => benchmark.GenerateOperationPopulation(2));
            var normalizedGrowth = (thousandAllocation / 1000D) / (hundredAllocation / 100D);

            Assert.InRange(matrixAllocation, 1L, MaximumMatrixAllocationBytes);
            Assert.InRange(normalizedGrowth, 0D, MaximumNormalizedGrowthRatio);
        }
        finally
        {
            benchmark.GlobalCleanup();
        }
    }

    [Fact]
    public async Task Benchmark_driver_reports_invalid_invocation_as_invalid_evidence()
    {
        var exitCode = await Doka.EntityFrameworkCore.MySql.Benchmarks.Program.Main(["--workloads"]);

        Assert.Equal(78, exitCode);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(4_288, 1)]
    [InlineData(1_048_400, 1)]
    [InlineData(4_288, 4)]
    public void Handler_scoped_generation_does_not_amplify_allocations_by_sql_size(
        int bodyLength,
        int scopeCount
    )
    {
        using var fixture = new ScopedAllocationFixture();
        var bodyCommand = CreateBodyCommand(bodyLength);
        var opaqueCommand = string.Concat(s_setupCommands)
            + bodyCommand
            + string.Concat(s_cleanupCommands.Reverse());
        var opaqueOperations = CreateProbeOperations(
            s_setupCommands,
            bodyCommand,
            s_cleanupCommands,
            opaqueCommand,
            scopeCount,
            scoped: false);
        var scopedOperations = CreateProbeOperations(
            s_setupCommands,
            bodyCommand,
            s_cleanupCommands,
            opaqueCommand,
            scopeCount,
            scoped: true);

        _ = fixture.Generate(opaqueOperations);
        _ = fixture.Generate(scopedOperations);

        var opaqueAllocation = MeasureMedianAllocation(() => fixture.Generate(opaqueOperations));
        var scopedAllocation = MeasureMedianAllocation(() => fixture.Generate(scopedOperations));

        AssertScopedAllocation(
            "handler",
            bodyLength,
            scopeCount,
            opaqueAllocation,
            scopedAllocation,
            MaximumHandlerScopedAllocationRatio);
    }

    [Fact]
    public void Handler_scoped_generation_does_not_materialize_fragment_group_intermediates()
    {
        const int commandCountPerRole = 32;
        const int commandLength = 16_000;
        using var fixture = new ScopedAllocationFixture();
        var setupCommands = CreateCommands('s', commandCountPerRole, commandLength);
        var bodyCommand = "x";
        var cleanupCommands = CreateCommands('c', commandCountPerRole, commandLength);
        var opaqueCommand = string.Concat(setupCommands)
            + bodyCommand
            + string.Concat(cleanupCommands.Reverse());
        var opaqueOperations = CreateProbeOperations(
            setupCommands,
            bodyCommand,
            cleanupCommands,
            opaqueCommand,
            operationCount: 1,
            scoped: false);
        var scopedOperations = CreateProbeOperations(
            setupCommands,
            bodyCommand,
            cleanupCommands,
            opaqueCommand,
            operationCount: 1,
            scoped: true);

        _ = fixture.Generate(opaqueOperations);
        _ = fixture.Generate(scopedOperations);

        var opaqueAllocation = MeasureMedianAllocation(() => fixture.Generate(opaqueOperations));
        var scopedAllocation = MeasureMedianAllocation(() => fixture.Generate(scopedOperations));

        AssertScopedAllocation(
            "handler fragmented",
            opaqueCommand.Length,
            scopeCount: 1,
            opaqueAllocation,
            scopedAllocation,
            MaximumHandlerScopedAllocationRatio);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(4_288, 1)]
    [InlineData(1_048_400, 1)]
    [InlineData(4_288, 4)]
    public void Provider_sql_mode_scope_does_not_amplify_allocations_by_sql_size(
        int commentLength,
        int scopeCount
    )
    {
        using var fixture = new ScopedAllocationFixture();
        var ordinaryOperations = CreateProviderScopeOperations(commentLength, scopeCount, scoped: false);
        var scopedOperations = CreateProviderScopeOperations(commentLength, scopeCount, scoped: true);

        _ = fixture.Generate(ordinaryOperations);
        _ = fixture.Generate(scopedOperations);

        var ordinaryAllocation = MeasureMedianAllocation(() => fixture.Generate(ordinaryOperations));
        var scopedAllocation = MeasureMedianAllocation(() => fixture.Generate(scopedOperations));

        AssertScopedAllocation(
            "provider sql_mode",
            commentLength,
            scopeCount,
            ordinaryAllocation,
            scopedAllocation,
            MaximumProviderScopedAllocationRatio);
    }

    [Fact]
    public void Provider_sql_mode_scope_reuses_invariant_fragments_across_many_scopes()
    {
        const int commentLength = 5;
        const int scopeCount = 256;
        using var fixture = new ScopedAllocationFixture();
        var ordinaryOperations = CreateProviderScopeOperations(commentLength, scopeCount, scoped: false);
        var scopedOperations = CreateProviderScopeOperations(commentLength, scopeCount, scoped: true);

        _ = fixture.Generate(ordinaryOperations);
        _ = fixture.Generate(scopedOperations);

        var ordinaryAllocation = MeasureMedianAllocation(() => fixture.Generate(ordinaryOperations));
        var scopedAllocation = MeasureMedianAllocation(() => fixture.Generate(scopedOperations));
        var incrementalBytesPerScope = (scopedAllocation - ordinaryAllocation) / scopeCount;

        Assert.InRange(incrementalBytesPerScope, 0L, MaximumProviderIncrementalBytesPerScope);
    }

    private static long MeasureAllocation(
        Func<long> operation
    )
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = operation();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static MigrationOperation[] CreateProbeOperations(
        IReadOnlyList<string> setupCommands,
        string bodyCommand,
        IReadOnlyList<string> cleanupCommands,
        string opaqueCommand,
        int operationCount,
        bool scoped
    )
    {
        var operations = new MigrationOperation[operationCount];

        for (var index = 0; index < operationCount; index++)
        {
            operations[index] = new ScopedAllocationOperation(
                setupCommands,
                bodyCommand,
                cleanupCommands,
                opaqueCommand,
                scoped);
        }

        return operations;
    }

    private static string[] CreateCommands(
        char value,
        int count,
        int length
    )
    {
        var commands = new string[count];

        for (var index = 0; index < commands.Length; index++)
        {
            commands[index] = new string(value, length);
        }

        return commands;
    }

    private static MigrationOperation[] CreateProviderScopeOperations(
        int commentLength,
        int operationCount,
        bool scoped
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(commentLength, 1);
        var comment = scoped
            ? "\\" + new string('x', commentLength - 1)
            : new string('x', commentLength);
        var operations = new MigrationOperation[operationCount];

        for (var index = 0; index < operationCount; index++)
        {
            operations[index] = new AddColumnOperation
            {
                Table = "ScopedAllocationEntries",
                Name = $"Value{index}",
                ClrType = typeof(string),
                ColumnType = "longtext",
                IsNullable = true,
                Comment = comment,
            };
        }

        return operations;
    }

    private static string CreateBodyCommand(
        int length
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        return new string('x', length);
    }

    private static long MeasureMedianAllocation(
        Func<long> operation
    )
    {
        Span<long> allocations = stackalloc long[5];

        for (var index = 0; index < allocations.Length; index++)
        {
            allocations[index] = MeasureAllocation(operation);
        }

        allocations.Sort();

        return allocations[allocations.Length / 2];
    }

    private static void AssertScopedAllocation(
        string scopePath,
        int payloadLength,
        int scopeCount,
        long ordinaryAllocation,
        long scopedAllocation,
        double maximumRatio
    )
    {
        var maximumScopedAllocation = (ordinaryAllocation * maximumRatio)
            + ScopedAllocationFixedAllowanceBytes;

        Assert.True(
            scopedAllocation <= maximumScopedAllocation,
            $"The {scopePath} scope allocated {scopedAllocation:N0} bytes versus "
            + $"{ordinaryAllocation:N0} ordinary bytes for payload length {payloadLength:N0} "
            + $"and {scopeCount:N0} scope(s); the limit is {maximumScopedAllocation:N0} bytes.");
    }

    private sealed class ScopedAllocationContext : DbContext
    {
        public ScopedAllocationContext(
            DbContextOptions<ScopedAllocationContext> options
        ) : base(options) { }
    }

    private sealed class ScopedAllocationFixture : IDisposable
    {
        private readonly DbContextOptions<ScopedAllocationContext> _options;
        private readonly ServiceProvider _services;

        public ScopedAllocationFixture()
        {
            var services = new ServiceCollection();
            services.AddEntityFrameworkDokaMySql();
            services.AddScoped<IMySqlMigrationOperationHandler, ScopedAllocationOperationHandler>();

            _services = services.BuildServiceProvider(validateScopes: true);
            _options = new DbContextOptionsBuilder<ScopedAllocationContext>()
                .UseInternalServiceProvider(_services)
                .UseMySql(ConnectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)))
                .Options;
        }

        public void Dispose() => _services.Dispose();

        public long Generate(
            IReadOnlyList<MigrationOperation> operations
        )
        {
            using var context = new ScopedAllocationContext(_options);
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
    }

    private sealed class ScopedAllocationOperation : MigrationOperation
    {
        public ScopedAllocationOperation(
            IReadOnlyList<string> setupCommands,
            string bodyCommand,
            IReadOnlyList<string> cleanupCommands,
            string opaqueCommand,
            bool scoped
        )
        {
            SetupCommands = setupCommands;
            BodyCommand = bodyCommand;
            CleanupCommands = cleanupCommands;
            OpaqueCommand = opaqueCommand;
            Scoped = scoped;
        }

        public IReadOnlyList<string> SetupCommands { get; }

        public string BodyCommand { get; }

        public IReadOnlyList<string> CleanupCommands { get; }

        public string OpaqueCommand { get; }

        public bool Scoped { get; }
    }

    private sealed class ScopedAllocationOperationHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.scoped_allocation";

        public Type OperationType => typeof(ScopedAllocationOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var operation = (ScopedAllocationOperation)context.Operation;
            var command = operation.Scoped
                ? MySqlMigrationCommandSpec.CreateScoped(
                    operation.SetupCommands,
                    operation.BodyCommand,
                    operation.CleanupCommands)
                : MySqlMigrationCommandSpec.Create(operation.OpaqueCommand);

            return MySqlMigrationOperationResult.Generated([command], "generated");
        }
    }
}
