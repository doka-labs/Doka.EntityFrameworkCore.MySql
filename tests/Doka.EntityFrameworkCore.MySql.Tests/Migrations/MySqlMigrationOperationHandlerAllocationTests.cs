using Doka.EntityFrameworkCore.MySql.Benchmarks;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Enforces allocation and scaling budgets for the complete migration-operation
/// handler generation path.
/// </summary>
public sealed class MySqlMigrationOperationHandlerAllocationTests
{
    private const long MaximumMatrixAllocationBytes = 48L * 1024L * 1024L;
    private const double MaximumNormalizedGrowthRatio = 1.10D;

    [Fact]
    public void Complete_handler_generation_stays_bounded_and_scales_linearly()
    {
        var benchmark = new MigrationOperationHandlerGenerationBenchmark();
        benchmark.GlobalSetup();

        try
        {
            _ = benchmark.GenerateHandlerOperationMatrix();
            var matrixAllocation = MeasureAllocation(benchmark.GenerateHandlerOperationMatrix);
            var hundredAllocation = MeasureAllocation(() => benchmark.GenerateOperationPopulation(1));
            var thousandAllocation = MeasureAllocation(() => benchmark.GenerateOperationPopulation(2));
            var normalizedGrowth = (thousandAllocation / 1000D) / (hundredAllocation / 100D);

            Assert.InRange(matrixAllocation, 1L, MaximumMatrixAllocationBytes);
            Assert.InRange(normalizedGrowth, 0D, MaximumNormalizedGrowthRatio);
        }
        finally
        {
            benchmark.GlobalCleanup();
        }
    }

    private static long MeasureAllocation(
        Func<long> operation
    )
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = operation();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
