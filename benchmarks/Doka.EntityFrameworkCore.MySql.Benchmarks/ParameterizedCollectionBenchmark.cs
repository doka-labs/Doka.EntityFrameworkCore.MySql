namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Compares the three EF Core parameterized-collection strategies against an
/// indexed 14,000-row data set and a 10,000-value GUID predicate.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class ParameterizedCollectionBenchmark : IDisposable
{
    private const int EntityCount = 14000;
    private const int FilterCount = 10000;

    private BenchmarkContext? _context;
    private Guid[] _filterIds = [];

    /// <summary>
    /// Creates the deterministic benchmark corpus and verifies that the
    /// predicate cardinality is the same for both GUID storage formats.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        BenchmarkEnvironment.EnsureInitialized();

        _filterIds = Enumerable
            .Range(1, FilterCount)
            .Select(CreateGuid)
            .ToArray();

        using (var setupContext = BenchmarkEnvironment.CreateContext())
        {
            if (!setupContext.ParameterizedCollectionEntities.Any())
            {
                setupContext.ParameterizedCollectionEntities.AddRange(
                    Enumerable
                        .Range(1, EntityCount)
                        .Select(index => new ParameterizedCollectionBenchmarkEntity
                        {
                            Id = index,
                            BinaryId = CreateGuid(index),
                            CharId = CreateGuid(index),
                        }));
                setupContext.SaveChanges();
            }
        }

        _context = BenchmarkEnvironment.CreateContext();

        // Execute each shape sequentially before measurement. This proves that
        // every comparison returns the same rows and warms its query pipeline
        // without issuing concurrent operations on one DbContext.
        var matchCounts = new[]
        {
            DefaultParameterBinary16().GetAwaiter().GetResult(),
            MultipleParametersBinary16().GetAwaiter().GetResult(),
            ConstantBinary16().GetAwaiter().GetResult(),
            DefaultParameterChar36().GetAwaiter().GetResult(),
            MultipleParametersChar36().GetAwaiter().GetResult(),
            ConstantChar36().GetAwaiter().GetResult(),
        };

        if (matchCounts.Any(matchCount => matchCount != FilterCount))
        {
            throw new InvalidOperationException(
                "The parameterized-collection benchmark corpus does not match its declared cardinality.");
        }
    }

    /// <summary>
    /// Executes the provider-default single-parameter strategy for Binary16 GUIDs.
    /// </summary>
    [Benchmark]
    public Task<int> DefaultParameterBinary16()
    {
        var context = GetContext();

        return context.ParameterizedCollectionEntities.CountAsync(
            entity => _filterIds.Contains(entity.BinaryId),
            CancellationToken.None);
    }

    /// <summary>
    /// Executes EF Core's one-scalar-parameter-per-value strategy for Binary16 GUIDs.
    /// </summary>
    [Benchmark]
    public Task<int> MultipleParametersBinary16()
    {
        var context = GetContext();

        return context.ParameterizedCollectionEntities.CountAsync(
            entity => EF.MultipleParameters(_filterIds).Contains(entity.BinaryId),
            CancellationToken.None);
    }

    /// <summary>
    /// Executes EF Core's SQL-constant strategy for Binary16 GUIDs.
    /// </summary>
    [Benchmark]
    public Task<int> ConstantBinary16()
    {
        var context = GetContext();

        return context.ParameterizedCollectionEntities.CountAsync(
            entity => EF.Constant(_filterIds).Contains(entity.BinaryId),
            CancellationToken.None);
    }

    /// <summary>
    /// Executes the provider-default single-parameter strategy for Char36 GUIDs.
    /// </summary>
    [Benchmark]
    public Task<int> DefaultParameterChar36()
    {
        var context = GetContext();

        return context.ParameterizedCollectionEntities.CountAsync(
            entity => _filterIds.Contains(entity.CharId),
            CancellationToken.None);
    }

    /// <summary>
    /// Executes EF Core's one-scalar-parameter-per-value strategy for Char36 GUIDs.
    /// </summary>
    [Benchmark]
    public Task<int> MultipleParametersChar36()
    {
        var context = GetContext();

        return context.ParameterizedCollectionEntities.CountAsync(
            entity => EF.MultipleParameters(_filterIds).Contains(entity.CharId),
            CancellationToken.None);
    }

    /// <summary>
    /// Executes EF Core's SQL-constant strategy for Char36 GUIDs.
    /// </summary>
    [Benchmark]
    public Task<int> ConstantChar36()
    {
        var context = GetContext();

        return context.ParameterizedCollectionEntities.CountAsync(
            entity => EF.Constant(_filterIds).Contains(entity.CharId),
            CancellationToken.None);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context?.Dispose();
        _context = null;
        GC.SuppressFinalize(this);
    }

    private BenchmarkContext GetContext() => _context
        ?? throw new InvalidOperationException(
            "The parameterized-collection benchmark has not been initialized.");

    private static Guid CreateGuid(
        int value
    ) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
