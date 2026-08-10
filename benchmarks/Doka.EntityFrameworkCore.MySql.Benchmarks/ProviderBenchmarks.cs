namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class OptionsBuildBenchmarks
{
    [Benchmark]
    public object BuildProviderOptions()
    {
        return new DbContextOptionsBuilder<BenchmarkContext>().UseMySql(
                BenchmarkEnvironment.CreateConnectionString(BenchmarkEnvironment.DatabaseNameValue),
                BenchmarkEnvironment.ServerVersionValue,
                mySqlOptions => mySqlOptions.UseNetTopologySuite())
            .Options;
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class ModelInitializationBenchmarks
{
    private DbContextOptions<BenchmarkContext> _warmOptions = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        BenchmarkEnvironment.EnsureInitialized();
        _warmOptions = BenchmarkEnvironment.CreateOptions<BenchmarkContext>();

        using var context = new BenchmarkContext(_warmOptions);
        _ = context.Model;
    }

    [Benchmark]
    public int InitializeColdModel()
    {
        var options = BenchmarkEnvironment.CreateOptions<BenchmarkContext>(serviceProviderCaching: false);
        using var context = new BenchmarkContext(options);

        return context
            .Model.GetEntityTypes()
            .Count();
    }

    [Benchmark]
    public int AccessWarmModel()
    {
        using var context = new BenchmarkContext(_warmOptions);

        return context
            .Model.GetEntityTypes()
            .Count();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class QueryTranslationBenchmarks
{
    private const string GuidText = "00112233-4455-6677-8899-aabbccddeeff";

    private readonly Point _referencePoint = new(13.4050, 52.5200) { SRID = 4326 };
    private TranslationCorpusDto _corpus = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        BenchmarkEnvironment.EnsureInitialized();
        _corpus = BenchmarkCorpora.LoadTranslationCorpus();
    }

    [Benchmark]
    public int TranslateRepresentativeCorpus()
    {
        using var context = BenchmarkEnvironment.CreateContext();
        var totalLength = 0;

        foreach (var query in _corpus.Queries)
        {
            totalLength += query.Id switch
            {
                "string-length-filter" => context
                    .BasicEntities.Where(entity => entity.Name.Length > 10)
                    .ToQueryString()
                    .Length,
                "date-year-filter" => context
                    .BasicEntities.Where(entity => entity.CreatedAt.Year == 2024)
                    .ToQueryString()
                    .Length,
                "json-string-contains" => context
                    .BasicEntities.Where(entity => entity.Payload.Contains("\"kind\":\"benchmark\""))
                    .ToQueryString()
                    .Length,
                "spatial-distance-sphere" => context
                    .SpatialEntities.Where(entity =>
                        EF.Functions.DistanceSphere(entity.Location, _referencePoint) < 250000d)
                    .ToQueryString()
                    .Length,
                "guid-format-filter" => context
                    .TranslationEntities.Where(entity => entity.Token.ToString() == GuidText)
                    .ToQueryString()
                    .Length,
                "signed-bitwise-projection" => context
                    .TranslationEntities.Select(entity => new
                    {
                        Left = entity.SignedValue << entity.ShiftCount,
                        Right = entity.SignedValue >> entity.ShiftCount,
                        Complement = ~entity.SignedValue,
                        And = entity.SignedValue & entity.Id,
                        Or = entity.SignedValue | entity.Id,
                        Xor = entity.SignedValue ^ entity.Id,
                    })
                    .ToQueryString()
                    .Length,
                "temporal-components-projection" => context
                    .TranslationEntities.Select(entity => new
                    {
                        CreatedYear = entity.CreatedAt.Year,
                        CreatedDayOfYear = entity.CreatedAt.DayOfYear,
                        CreatedTimeOfDay = entity.CreatedAt.TimeOfDay,
                        UnixMilliseconds = entity.RecordedAt.ToUnixTimeMilliseconds(),
                        RecordedDayOfYear = entity.RecordedAt.DayOfYear,
                        DurationDays = entity.Duration.TotalDays,
                        DurationMicroseconds = entity.Duration.TotalMicroseconds,
                    })
                    .ToQueryString()
                    .Length,
                "byte-array-projection" => context
                    .TranslationEntities.Select(entity => new
                    {
                        Length = entity.BinaryPayload.Length,
                        First = entity.BinaryPayload[0],
                    })
                    .ToQueryString()
                    .Length,
                "string-transform-projection" => context
                    .TranslationEntities.Select(entity => new
                    {
                        Length = entity
                            .Name.Trim()
                            .Replace("old", "new")
                            .Length,
                        Segment = entity.Name.Substring(1, 4),
                    })
                    .ToQueryString()
                    .Length,
                "math-projection" => context
                    .TranslationEntities.Select(entity => new
                    {
                        Absolute = Math.Abs(entity.Score),
                        Sine = Math.Sin(entity.Score),
                        Logarithm = Math.Log(entity.Score),
                    })
                    .ToQueryString()
                    .Length,
                "numeric-convert-projection" => context
                    .TranslationEntities.Select(entity => Convert.ToInt64(entity.Score))
                    .ToQueryString()
                    .Length,
                "ordered-group-concat-projection" => context
                    .TranslationEntities.GroupBy(entity => entity.ShiftCount)
                    .Select(group => string.Join(
                        ", ",
                        group
                            .OrderBy(entity => entity.Name)
                            .ThenByDescending(entity => entity.Id)
                            .Select(entity => entity.Name)))
                    .ToQueryString()
                    .Length,
                _ => throw new InvalidOperationException($"Unknown translation scenario '{query.Id}'."),
            };
        }

        return totalLength;
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class HotPathBenchmarks
{
    [ParamsSource(nameof(RowCountValues))]
    public int RowCount { get; set; }

    public IEnumerable<int> RowCountValues() => BenchmarkProfiles.HotPathRowCounts();

    [GlobalSetup]
    public void GlobalSetup() => BenchmarkEnvironment.EnsureInitialized();

    [Benchmark]
    public int HotQueryThroughput()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return context
            .BasicEntities.AsNoTracking()
            .Where(entity => entity.CreatedAt.Year == 2024)
            .Take(RowCount)
            .Count();
    }

    [Benchmark]
    public List<BasicBenchmarkEntity> MaterializeRows()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return context
            .BasicEntities.AsNoTracking()
            .OrderBy(entity => entity.Id)
            .Take(RowCount)
            .ToList();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class SaveChangesBenchmarks
{
    [ParamsSource(nameof(EntityCountValues))]
    public int EntityCount { get; set; }

    public IEnumerable<int> EntityCountValues() => BenchmarkProfiles.SaveChangesEntityCounts();

    [GlobalSetup]
    public void GlobalSetup() => BenchmarkEnvironment.EnsureInitialized();

    [IterationSetup]
    public void IterationSetup() => BenchmarkEnvironment.ResetSaveChangesTable();

    [Benchmark]
    public int SaveChangesTrackedEntities()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        for (var index = 0; index < EntityCount; index++)
        {
            context.SaveChangeEntities.Add(
                new SaveChangeBenchmarkEntity
                {
                    Name = $"savechanges-{index}",
                });
        }

        return context.SaveChanges();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class MigrationsSqlGenerationBenchmarks
{
    private MigrationCorpusDto _corpus = null!;

    [GlobalSetup]
    public void GlobalSetup() => _corpus = BenchmarkCorpora.LoadMigrationCorpus();

    [Benchmark]
    public int GenerateMigrationSqlCorpus()
    {
        var commandCount = 0;

        foreach (var diff in _corpus.Diffs)
        {
            using var sourceContext = CreateSourceContext(diff.Id);
            using var targetContext = CreateTargetContext(diff.Id);
            var differ = targetContext.GetService<IMigrationsModelDiffer>();
            var migrationsSqlGenerator = targetContext.GetService<IMigrationsSqlGenerator>();
            var operations = differ.GetDifferences(
                sourceContext
                    .GetService<IDesignTimeModel>()
                    .Model.GetRelationalModel(),
                targetContext
                    .GetService<IDesignTimeModel>()
                    .Model.GetRelationalModel());
            var commands = migrationsSqlGenerator.Generate(operations, targetContext.Model);
            commandCount += commands.Count;
        }

        return commandCount;
    }

    private static DbContext CreateSourceContext(
        string scenarioId
    ) => scenarioId switch
    {
        "empty-to-rich" => new EmptyMigrationContext(BenchmarkEnvironment.CreateOptions<EmptyMigrationContext>()),
        "rich-to-spatial" => new RichMigrationContext(BenchmarkEnvironment.CreateOptions<RichMigrationContext>()),
        _ => throw new InvalidOperationException($"Unknown migration scenario '{scenarioId}'."),
    };

    private static DbContext CreateTargetContext(
        string scenarioId
    ) => scenarioId switch
    {
        "empty-to-rich" => new RichMigrationContext(BenchmarkEnvironment.CreateOptions<RichMigrationContext>()),
        "rich-to-spatial" => new SpatialMigrationContext(BenchmarkEnvironment.CreateOptions<SpatialMigrationContext>()),
        _ => throw new InvalidOperationException($"Unknown migration scenario '{scenarioId}'."),
    };
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class JsonBenchmarks
{
    [ParamsSource(nameof(RowCountValues))]
    public int RowCount { get; set; }

    public IEnumerable<int> RowCountValues() => BenchmarkProfiles.JsonRowCounts();

    [GlobalSetup]
    public void GlobalSetup() => BenchmarkEnvironment.EnsureInitialized();

    [Benchmark]
    public List<string> ReadJsonPayloads()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return context
            .BasicEntities.AsNoTracking()
            .OrderBy(entity => entity.Id)
            .Select(entity => entity.Payload)
            .Take(RowCount)
            .ToList();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class SpatialBenchmarks
{
    private readonly Point _referencePoint = new(13.4050, 52.5200) { SRID = 4326 };

    [GlobalSetup]
    public void GlobalSetup() => BenchmarkEnvironment.EnsureInitialized();

    [Benchmark]
    public List<SpatialBenchmarkEntity> MaterializeDistanceSphereRows()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return context
            .SpatialEntities.AsNoTracking()
            .Where(entity => EF.Functions.DistanceSphere(entity.Location, _referencePoint) < 250000d)
            .Take(100)
            .ToList();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class ProjectionBenchmarks
{
    [GlobalSetup]
    public void GlobalSetup() => BenchmarkEnvironment.EnsureInitialized();

    [Benchmark]
    public List<BasicBenchmarkEntity> FullEntityMaterialization()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return context
            .BasicEntities.AsNoTracking()
            .Take(100)
            .ToList();
    }

    [Benchmark]
    public List<BenchmarkProjection> AnonymousProjectionMaterialization()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return context
            .BasicEntities.AsNoTracking()
            .OrderBy(entity => entity.Id)
            .Select(entity => new BenchmarkProjection
            {
                Id = entity.Id,
                Name = entity.Name,
            })
            .Take(100)
            .ToList();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class PaginationBenchmarks
{
    [ParamsSource(nameof(OffsetValues))]
    public int Offset { get; set; }

    public IEnumerable<int> OffsetValues() => BenchmarkProfiles.MeasuresCompleteMatrix()
        ?
        [
            0,
            100,
            500
        ]
        : [100];

    [GlobalSetup]
    public void GlobalSetup() => BenchmarkEnvironment.EnsureInitialized();

    [Benchmark]
    public int PaginatedQuery()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return context
            .BasicEntities.AsNoTracking()
            .OrderBy(e => e.Id)
            .Skip(Offset)
            .Take(25)
            .Count();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class GroupByBenchmarks
{
    [GlobalSetup]
    public void GlobalSetup() => BenchmarkEnvironment.EnsureInitialized();

    [Benchmark]
    public int GroupByWithCount()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return context
            .BasicEntities.AsNoTracking()
            .GroupBy(e => e.CreatedAt.Year)
            .Select(g => new
            {
                Year = g.Key,
                Count = g.Count()
            })
            .Count();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class CompiledQueryBenchmarks
{
    private static readonly Func<BenchmarkContext, int, IEnumerable<BasicBenchmarkEntity>> s_compiledQuery =
        EF.CompileQuery((
            BenchmarkContext ctx,
            int year
        ) => ctx.BasicEntities.Where(e => e.CreatedAt.Year == year));

    [GlobalSetup]
    public void GlobalSetup() => BenchmarkEnvironment.EnsureInitialized();

    [Benchmark]
    public int CompiledQueryExecution()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return s_compiledQuery(context, 2024)
            .Count();
    }

    [Benchmark]
    public int DynamicQueryExecution()
    {
        using var context = BenchmarkEnvironment.CreateContext();

        return context.BasicEntities.Count(e => e.CreatedAt.Year == 2024);
    }
}
