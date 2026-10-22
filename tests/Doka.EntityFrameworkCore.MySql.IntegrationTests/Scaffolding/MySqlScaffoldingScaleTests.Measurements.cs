namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Shares the measurements responsibility without changing the test fixture.</summary>
public sealed partial class MySqlScaffoldingScaleTests
{
    private sealed class ManagedHeapSampler : IDisposable
    {
        private readonly long _baselineBytes;
        private readonly ManualResetEventSlim _stop = new();
        private readonly Thread _thread;
        private long _peakBytes;
        private int _stopped;

        public ManagedHeapSampler(
            long baselineBytes
        )
        {
            _baselineBytes = baselineBytes;
            _peakBytes = baselineBytes;
            _thread = new Thread(Sample)
            {
                IsBackground = true,
                Name = "Doka scaffolding managed-heap sampler",
            };
            _thread.Start();
        }

        public long Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
            {
                _stop.Set();
                _thread.Join();
            }

            return Math.Max(0, Volatile.Read(ref _peakBytes) - _baselineBytes);
        }

        public void Dispose()
        {
            Stop();
            _stop.Dispose();
        }

        private void Sample()
        {
            while (!_stop.Wait(TimeSpan.FromMilliseconds(1)))
            {
                UpdatePeak(GC.GetTotalMemory(forceFullCollection: false));
            }

            UpdatePeak(GC.GetTotalMemory(forceFullCollection: false));
        }

        private void UpdatePeak(
            long currentBytes
        )
        {
            var observedPeak = Volatile.Read(ref _peakBytes);

            while (currentBytes > observedPeak)
            {
                var replaced = Interlocked.CompareExchange(
                    ref _peakBytes,
                    currentBytes,
                    observedPeak);

                if (replaced == observedPeak)
                {
                    return;
                }

                observedPeak = replaced;
            }
        }
    }

    private sealed record RestrictedAccount(
        string UserName,
        string Password
    )
    {
        public static RestrictedAccount Create()
        {
            var token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12];

            return new RestrictedAccount(
                "doka_scale_" + token,
                "DokaRestricted_" + token);
        }
    }

    private sealed record ScaffoldingRunMetrics(
        TimeSpan Elapsed,
        long AllocatedBytes,
        long ManagedHeapGrowthBytes,
        int EntityFileCount,
        string Signature
    );

    private sealed record MetadataCommandMetrics(
        int CommandCount,
        IReadOnlyList<string> CommandTexts
    );

    private sealed class ScaffoldingPerformancePolicy
    {
        public required int SchemaVersion { get; init; }

        public required string ScenarioId { get; init; }

        public required int TableCount { get; init; }

        public required int ColumnsPerTable { get; init; }

        public required int RestrictedVisibleTableCount { get; init; }

        public required int MeasuredRuns { get; init; }

        public required ScaffoldingPerformanceBudgets Budgets { get; init; }

        public required string[] CleanupAndCancellationTests { get; init; }
    }

    private sealed class ScaffoldingPerformanceBudgets
    {
        public required int MaximumSetBasedMetadataCommandsPerRun { get; init; }

        public required int MaximumElapsedMillisecondsPerRun { get; init; }

        public required long MaximumAllocatedBytesPerRun { get; init; }

        public required long MaximumManagedHeapGrowthBytesPerRun { get; init; }
    }
}
