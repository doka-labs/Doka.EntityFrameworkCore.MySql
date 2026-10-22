namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies performance evidence validation, checked-in control budgets, and gate exit-code contracts.
/// </summary>
public sealed partial class PerformanceGateTests
{
    /// <summary>
    /// Verifies the benchmark driver rejects unsuccessful BenchmarkDotNet reports.
    /// </summary>
    /// <param name="success">Whether BenchmarkDotNet reports successful execution.</param>
    /// <param name="expectedFailure">Whether the driver must reject that report.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Driver_rejects_unsuccessful_BenchmarkDotNet_reports(
        bool success,
        bool expectedFailure
    )
    {
        // Arrange
        var report = new BenchmarkReport(
            success,
            benchmarkCase: null!,
            generateResult: null!,
            buildResult: null!,
            executeResults: [],
            metrics: []);

        // Act
        var actual = Benchmarks.Program.HasFailedBenchmarkReports([report]);

        // Assert
        Assert.Equal(
            expectedFailure,
            actual);
    }

    /// <summary>
    /// Verifies the provider workload remains inheritable by BenchmarkDotNet's generated runners.
    /// </summary>
    [Fact]
    public void Provider_workload_benchmark_is_compatible_with_BenchmarkDotNet()
    {
        // Arrange
        var benchmarkType = typeof(Benchmarks.ProviderWorkloadBenchmarks);

        // Act
        var actual = benchmarkType.IsSealed;

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies the gate accepts complete finite BenchmarkDotNet evidence at its exact budgets.
    /// </summary>
    [Fact]
    public void Gate_accepts_complete_finite_BenchmarkDotNet_evidence_at_the_budgets()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create();

        // Act
        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            soakPath: null);

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Empty(result.Regressions);
        Assert.Equal(1, result.WorkloadCount);
        Assert.Equal(1, result.ControlCount);
    }

    /// <summary>
    /// Verifies gate reports a budget regression separately from invalid evidence.
    /// </summary>
    [Fact]
    public void Gate_reports_a_budget_regression_separately_from_invalid_evidence()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(medianBudget: 4);

        // Act
        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            soakPath: null);

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Contains(result.Regressions, error => error.Contains("medianNanoseconds", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies gate rejects a report bound to another target.
    /// </summary>
    [Fact]
    public void Gate_rejects_a_report_bound_to_another_target()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(reportTarget: "mariadb114");

        // Act
        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            soakPath: null);

        // Assert
        Assert.Contains(
            result.InvalidEvidence,
            error => error.Contains("expected 'mysql84'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies gate rejects statistics that do not match raw samples.
    /// </summary>
    [Fact]
    public void Gate_rejects_statistics_that_do_not_match_raw_samples()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(sampleCountOffset: 1);
        Action act = () => BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            soakPath: null);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidDataException>(recordedException);
    }

    /// <summary>
    /// Verifies gate recomputes managed heap soak budget from the raw metric.
    /// </summary>
    [Fact]
    public void Gate_recomputes_managed_heap_soak_budget_from_the_raw_metric()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create();
        var soakPath = fixture.WriteSoak(managedHeapGrowthBytes: 2048);

        // Act
        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "scorecard",
            soakPath);

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Contains(
            result.Regressions,
            error => error.Contains("managedHeapGrowthBytes", StringComparison.Ordinal));
    }
}
