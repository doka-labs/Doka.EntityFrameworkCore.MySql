namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the immutable exact-type registry across the complete 0/1/8
/// handler by 1/100/1000 operation contract matrix.
/// </summary>
/// <remarks>
/// One invocation covers every matrix cell so hosted evidence has one stable,
/// parameter-free allocation control. Registry construction remains outside
/// the measured path; the benchmark isolates exact-type dispatch.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class MigrationOperationHandlerDispatchBenchmark
{
    private static readonly int[] s_operationCounts = [1, 100, 1000];

    private static readonly Type[] s_operationTypes =
    [
        typeof(Operation1),
        typeof(Operation2),
        typeof(Operation3),
        typeof(Operation4),
        typeof(Operation5),
        typeof(Operation6),
        typeof(Operation7),
        typeof(Operation8),
    ];

    private MySqlMigrationOperationHandlerRegistry[] _registries = null!;

    [GlobalSetup]
    public void Setup()
    {
        _registries =
        [
            CreateRegistry(0),
            CreateRegistry(1),
            CreateRegistry(8)
        ];
    }

    [Benchmark]
    public int DispatchExactTypeMatrix()
    {
        var matches = 0;

        foreach (var registry in _registries)
        {
            foreach (var operationCount in s_operationCounts)
            {
                for (var index = 0; index < operationCount; index++)
                {
                    var operationType = s_operationTypes[index % s_operationTypes.Length];
                    if (registry.TryGet(operationType, out _))
                    {
                        matches++;
                    }
                }
            }
        }

        return matches;
    }

    private static MySqlMigrationOperationHandlerRegistry CreateRegistry(
        int handlerCount
    )
    {
        var handlers = s_operationTypes
            .Take(handlerCount)
            .Select((
                operationType,
                index
            ) => new Handler($"bench.handler_{index}", operationType));

        return new MySqlMigrationOperationHandlerRegistry(handlers);
    }

    private sealed class Handler : IMySqlMigrationOperationHandler
    {
        public Handler(
            string handlerId,
            Type operationType
        )
        {
            HandlerId = handlerId;
            OperationType = operationType;
        }

        public string HandlerId { get; }

        public Type OperationType { get; }

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => throw new NotSupportedException();
    }

    private sealed class Operation1 : MigrationOperation;

    private sealed class Operation2 : MigrationOperation;

    private sealed class Operation3 : MigrationOperation;

    private sealed class Operation4 : MigrationOperation;

    private sealed class Operation5 : MigrationOperation;

    private sealed class Operation6 : MigrationOperation;

    private sealed class Operation7 : MigrationOperation;

    private sealed class Operation8 : MigrationOperation;
}
