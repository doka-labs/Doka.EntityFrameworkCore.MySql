using Doka.EntityFrameworkCore.MySql.Benchmarks;

namespace Doka.EntityFrameworkCore.MySql.Tests.Performance;

[Collection(PerformanceWorkloadRunnerTestGroup.Name)]
public sealed class PerformanceWorkloadRunnerTests
{
    [Fact]
    public async Task Adaptive_batch_reports_latency_and_allocations_per_operation()
    {
        var fixedResult = await MeasureAsync(adaptiveOperationsPerSample: false);
        var adaptiveResult = await MeasureAsync(adaptiveOperationsPerSample: true);

        Assert.Equal(1, fixedResult.OperationsPerSample);
        Assert.Equal(8, adaptiveResult.OperationsPerSample);

        Assert.InRange(
            adaptiveResult.MedianNanoseconds / fixedResult.MedianNanoseconds,
            0.5,
            2.0);
        Assert.InRange(
            (double)adaptiveResult.AllocatedBytesPerOperation
            / fixedResult.AllocatedBytesPerOperation,
            0.5,
            2.0);
    }

    private static Task<PerformanceWorkloadResult> MeasureAsync(
        bool adaptiveOperationsPerSample
    ) => PerformanceWorkloadRunner.MeasureAsync(
        new PerformanceWorkload(
            "adaptive-normalization",
            ExecuteAsync),
        new PerformanceWorkloadDefinition
        {
            Id = "adaptive-normalization",
            Family = "model",
            OperationsPerSample = 1,
        },
        profileWarmupSamples: 3,
        minimumSampleCount: 32,
        minimumMeasurementDurationMilliseconds: adaptiveOperationsPerSample ? 2000 : 0,
        maximumMeasurementSampleMultiplier: 1,
        adaptiveOperationsPerSample,
        operationBatchingDurationHeadroomPercent: adaptiveOperationsPerSample ? 120 : 100,
        operationBatchingPilotSamples: adaptiveOperationsPerSample ? 3 : 0,
        maximumOperationsPerSampleMultiplier: adaptiveOperationsPerSample ? 8 : 1,
        maximumRelativeStandardError: 1,
        calibrationKind: "cpu",
        calibrationSamplesPerPulse: 1,
        calibrationIntervalSamples: 32,
        maximumCalibrationRelativeStandardError: 1,
        measurementQualityPolicy: "observe",
        CancellationToken.None);

    private static ValueTask<long> ExecuteAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = GC.AllocateUninitializedArray<byte>(4096);

        Thread.SpinWait(20_000);
        GC.KeepAlive(buffer);

        return ValueTask.FromResult((long)buffer.Length);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceWorkloadRunnerTestGroup
{
    public const string Name = "Performance workload runner";
}
