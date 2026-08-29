using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Records throughput and allocation cost for provider-owned
/// <see cref="JsonElement"/> materialization. This benchmark remains
/// observational until repeated runs provide a calibrated regression budget.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class JsonElementMaterializationBenchmark
{
    private static readonly ValueConverter s_converter =
        MySqlJsonTypeMapping.CreateJsonElementMapping().Converter!;

    private string _payload = null!;

    [Params(256, 4096, 65536)]
    public int PayloadBytes { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        const string prefix = "{\"payload\":\"";
        const string suffix = "\"}";

        _payload = prefix + new string('x', PayloadBytes - prefix.Length - suffix.Length) + suffix;
    }

    [Benchmark]
    public JsonElement ConvertFromProvider() =>
        (JsonElement)s_converter.ConvertFromProvider(_payload)!;
}
