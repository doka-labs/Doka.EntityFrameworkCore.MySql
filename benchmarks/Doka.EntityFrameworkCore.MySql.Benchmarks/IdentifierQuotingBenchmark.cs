using Microsoft.Extensions.DependencyInjection;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the identifier-quoting hot path that fires on every query-translation pass.
/// Compares the four shapes of <c>MySqlSqlGenerationHelper.DelimitIdentifier</c> against
/// the no-backtick fast-path (the overwhelming common case) and the per-char slow-path
/// (an identifier that already contains a backtick). The MemoryDiagnoser reports both
/// throughput and allocation; the fast-path commits to one allocation per call for the
/// string overload and zero allocations for the StringBuilder overload.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class IdentifierQuotingBenchmark
{
    private const string PlainIdentifier = "CustomerOrderLineItem";
    private const string BacktickIdentifier = "Customer`Order`LineItem";
    private const string Schema = "warehouse_schema";

    private readonly ISqlGenerationHelper _helper;
    private readonly StringBuilder _builder = new(256);

    public IdentifierQuotingBenchmark()
    {
        var services = new ServiceCollection();
        services.AddDbContext<HelperBenchmarkContext>(options => options.UseMySql(
            "Server=localhost;Database=bench;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<HelperBenchmarkContext>();
        _helper = context.GetService<ISqlGenerationHelper>();
    }

    /// <summary>
    /// Naive reference for the plain-identifier path: per-char StringBuilder loop with manual
    /// backtick-escape. The fast-path under test (<see cref="DelimitStringPlain"/>) is the
    /// optimized Span-based implementation; BDN reports Ratio = Mean[fast]/Mean[naive], so a
    /// fast-path >= 2x faster than naive shows up as Ratio &lt;= 0.5 (the DoD gate).
    /// </summary>
    [Benchmark(Baseline = true)]
    public string NaiveDelimitStringPlain() => NaiveDelimit(PlainIdentifier, schema: null);

    [Benchmark]
    public string DelimitStringPlain() => _helper.DelimitIdentifier(PlainIdentifier);

    [Benchmark]
    public string DelimitStringBacktick() => _helper.DelimitIdentifier(BacktickIdentifier);

    [Benchmark]
    public string DelimitStringSchemaPlain() => _helper.DelimitIdentifier(PlainIdentifier, Schema);

    [Benchmark]
    public int DelimitBuilderPlain()
    {
        _builder.Clear();
        _helper.DelimitIdentifier(_builder, PlainIdentifier);
        return _builder.Length;
    }

    [Benchmark]
    public int DelimitBuilderBacktick()
    {
        _builder.Clear();
        _helper.DelimitIdentifier(_builder, BacktickIdentifier);
        return _builder.Length;
    }

    [Benchmark]
    public int DelimitBuilderSchemaPlain()
    {
        _builder.Clear();
        _helper.DelimitIdentifier(_builder, PlainIdentifier, Schema);
        return _builder.Length;
    }

    private static string NaiveDelimit(
        string identifier,
        string? schema
    )
    {
        var sb = new StringBuilder(identifier.Length + (schema?.Length ?? 0) + 5);

        if (schema is not null)
        {
            sb.Append('`');
            foreach (var c in schema)
            {
                if (c == '`')
                {
                    sb.Append('`');
                }
                sb.Append(c);
            }
            sb.Append('`').Append('.');
        }

        sb.Append('`');
        foreach (var c in identifier)
        {
            if (c == '`')
            {
                sb.Append('`');
            }
            sb.Append(c);
        }
        sb.Append('`');

        return sb.ToString();
    }

    private sealed class HelperBenchmarkContext : DbContext
    {
        public HelperBenchmarkContext(
            DbContextOptions<HelperBenchmarkContext> options
        ) : base(options) { }
    }
}
