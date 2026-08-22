namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures only provider temporal-literal formatting. Mappings and values are
/// prepared once so the returned SQL string is the only required allocation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class TemporalLiteralBenchmark
{
    private readonly MySqlTimeOnlyTypeMapping _timeOnlyPrecision0 = new("time", 0);
    private readonly MySqlTimeOnlyTypeMapping _timeOnlyPrecision3 = new("time(3)", 3);
    private readonly MySqlTimeOnlyTypeMapping _timeOnlyPrecision6 = new("time(6)", 6);
    private readonly MySqlTimeSpanTypeMapping _timeSpanPrecision6 = new("time(6)", 6);
    private readonly TimeOnly _timeOnly = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1_234_567));
    private readonly TimeSpan _positiveTimeSpan = TimeSpan.FromHours(27) + TimeSpan.FromTicks(1_234_567);

    private readonly TimeSpan _negativeBoundary = -(TimeSpan.FromHours(838)
        + TimeSpan.FromMinutes(59)
        + TimeSpan.FromSeconds(59));

    [Benchmark]
    public string GenerateTimeOnlyPrecision0Literal() => _timeOnlyPrecision0.GenerateSqlLiteral(_timeOnly);

    [Benchmark]
    public string GenerateTimeOnlyPrecision3Literal() => _timeOnlyPrecision3.GenerateSqlLiteral(_timeOnly);

    [Benchmark]
    public string GenerateTimeOnlyPrecision6Literal() => _timeOnlyPrecision6.GenerateSqlLiteral(_timeOnly);

    [Benchmark]
    public string GeneratePositiveTimeSpanPrecision6Literal() =>
        _timeSpanPrecision6.GenerateSqlLiteral(_positiveTimeSpan);

    [Benchmark]
    public string GenerateNegativeBoundaryTimeSpanPrecision6Literal() =>
        _timeSpanPrecision6.GenerateSqlLiteral(_negativeBoundary);
}
