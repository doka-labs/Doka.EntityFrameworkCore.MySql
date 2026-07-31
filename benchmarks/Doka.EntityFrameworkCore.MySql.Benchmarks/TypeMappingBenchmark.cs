using Microsoft.Extensions.DependencyInjection;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the cold-path FindMapping cost for the most common store-type lookups.
/// The store-type dictionary is a FrozenDictionary keyed by StringComparer.Ordinal
/// IgnoreCase, so the per-call cost should hover at one dictionary read; the
/// MemoryDiagnoser verifies no per-lookup allocation. Run before and after type-
/// mapping changes to catch performance regressions in the cold-path translation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class TypeMappingBenchmark
{
    private readonly IRelationalTypeMappingSource _source;

    public TypeMappingBenchmark()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MappingBenchmarkContext>(options => options.UseMySql(
            "Server=localhost;Database=bench;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MappingBenchmarkContext>();
        _source = context.GetService<IRelationalTypeMappingSource>();
    }

    [Benchmark]
    public RelationalTypeMapping? FindByStoreTypeVarchar() => _source.FindMapping("varchar(255)");

    [Benchmark]
    public RelationalTypeMapping? FindByStoreTypeDecimal() => _source.FindMapping("decimal(18,2)");

    [Benchmark]
    public RelationalTypeMapping? FindByStoreTypeDatetime() => _source.FindMapping("datetime(6)");

    [Benchmark]
    public RelationalTypeMapping? FindByStoreTypeLongtext() => _source.FindMapping("longtext");

    [Benchmark]
    public RelationalTypeMapping? FindByClrTypeString() => _source.FindMapping(typeof(string));

    [Benchmark]
    public RelationalTypeMapping? FindByClrTypeDecimal() => _source.FindMapping(typeof(decimal));

    [Benchmark]
    public RelationalTypeMapping? FindByClrTypeGuid() => _source.FindMapping(typeof(Guid));

    [Benchmark]
    public RelationalTypeMapping? FindByClrTypeInt() => _source.FindMapping(typeof(int));

    private sealed class MappingBenchmarkContext : DbContext
    {
        public MappingBenchmarkContext(
            DbContextOptions<MappingBenchmarkContext> options
        ) : base(options) { }
    }
}
