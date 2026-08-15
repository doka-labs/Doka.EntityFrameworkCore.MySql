namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

// The production source remains runtime-backed; this boundary lets call-site
// tests prove batching normalization without treating machine noise as data.
internal interface IPerformanceMeasurementSource
{
    long TimestampFrequency { get; }

    long GetTimestamp();

    long GetTotalAllocatedBytes();
}

internal sealed class RuntimePerformanceMeasurementSource : IPerformanceMeasurementSource
{
    public static RuntimePerformanceMeasurementSource Instance { get; } = new();

    public long TimestampFrequency => Stopwatch.Frequency;

    private RuntimePerformanceMeasurementSource() { }

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public long GetTotalAllocatedBytes() => GC.GetTotalAllocatedBytes(precise: true);
}
