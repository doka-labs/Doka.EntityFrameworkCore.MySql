using Doka.EntityFrameworkCore.MySql.Benchmarks;

namespace Doka.EntityFrameworkCore.MySql.Tests.Performance;

[Collection(PerformanceWorkloadRunnerTestGroup.Name)]
public sealed class PerformanceWorkloadRunnerTests
{
    [Fact]
    public async Task Adaptive_batch_reports_latency_and_allocations_per_operation()
    {
        var fixedMeasurement = DeterministicMeasurementSource.CreateFixed();
        var adaptiveMeasurement = DeterministicMeasurementSource.CreateAdaptive();
        var fixedResult = await MeasureAsync(adaptiveOperationsPerSample: false, fixedMeasurement);
        var adaptiveResult = await MeasureAsync(adaptiveOperationsPerSample: true, adaptiveMeasurement);

        Assert.Equal(1, fixedResult.OperationsPerSample);
        Assert.Equal(8, adaptiveResult.OperationsPerSample);
        Assert.Equal(10_000_000, fixedResult.MedianNanoseconds);
        Assert.Equal(100, fixedResult.AllocatedBytesPerOperation);
        Assert.Equal(fixedResult.MedianNanoseconds, adaptiveResult.MedianNanoseconds);
        Assert.Equal(fixedResult.AllocatedBytesPerOperation, adaptiveResult.AllocatedBytesPerOperation);
        fixedMeasurement.AssertConsumed();
        adaptiveMeasurement.AssertConsumed();
    }

    private static Task<PerformanceWorkloadResult> MeasureAsync(
        bool adaptiveOperationsPerSample,
        IPerformanceMeasurementSource measurementSource
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
        CancellationToken.None,
        measurementSource);

    private static ValueTask<long> ExecuteAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(1L);
    }

    private sealed class DeterministicMeasurementSource : IPerformanceMeasurementSource
    {
        private readonly Queue<long> _timestamps;
        private readonly Queue<long> _allocatedBytes;

        private DeterministicMeasurementSource(
            IEnumerable<long> timestamps,
            IEnumerable<long> allocatedBytes
        )
        {
            _timestamps = new Queue<long>(timestamps);
            _allocatedBytes = new Queue<long>(allocatedBytes);
        }

        public long TimestampFrequency => 1000;

        public static DeterministicMeasurementSource CreateFixed() => new(
            SampleBoundaries(sampleCount: 32, elapsedTicks: 10),
            SampleBoundaries(sampleCount: 32, elapsedTicks: 100));

        public static DeterministicMeasurementSource CreateAdaptive() => new(
            SampleBoundaries(sampleCount: 3, elapsedTicks: 10)
                .Concat(SampleBoundaries(sampleCount: 32, elapsedTicks: 80)),
            SampleBoundaries(sampleCount: 32, elapsedTicks: 800));

        public long GetTimestamp() => _timestamps.Dequeue();

        public long GetTotalAllocatedBytes() => _allocatedBytes.Dequeue();

        public void AssertConsumed()
        {
            Assert.Empty(_timestamps);
            Assert.Empty(_allocatedBytes);
        }

        private static IEnumerable<long> SampleBoundaries(
            int sampleCount,
            long elapsedTicks
        )
        {
            for (var sample = 0; sample < sampleCount; sample++)
            {
                yield return 0;
                yield return elapsedTicks;
            }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceWorkloadRunnerTestGroup
{
    public const string Name = "Performance workload runner";
}
