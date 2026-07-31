using BenchmarkDotNet.Diagnosers;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class BenchmarkConfiguration
{
    public static IConfig Create() => ManualConfig
        .Create(DefaultConfig.Instance)
        .AddExporter(JsonExporter.Full)
        .AddDiagnoser(MemoryDiagnoser.Default)
        .WithOption(ConfigOptions.StopOnFirstError, true);
}
