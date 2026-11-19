namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class PerformanceGateTests
{
    /// <summary>Verifies the legacy internal policy cannot restore hardware-sensitive rejection.</summary>
    /// <param name="legacyFull">Whether to pass the legacy policy identity directly.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_evaluation_identity_keeps_machine_sensitive_thresholds_diagnostic(
        bool legacyFull
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(timeMultiplier: 100000, gen2Collections: 100);
        var mode = legacyFull
            ? PerformanceEvaluationMode.Full
            : PerformanceEvaluationMode.AllocationsOnly;

        // Act
        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            null,
            mode);

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Empty(result.Regressions);
        Assert.Equal(4, result.Diagnostics.Count);
    }

    /// <summary>Verifies policy normalization never conceals a real allocation regression.</summary>
    /// <param name="legacyFull">Whether to pass the legacy identity rather than the default.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_evaluation_identity_retains_allocation_rejection(
        bool legacyFull
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(workloadAllocatedBytes: 1000);
        var mode = legacyFull
            ? PerformanceEvaluationMode.Full
            : PerformanceEvaluationMode.AllocationsOnly;

        // Act
        var exitCode = BenchmarkPerformanceGate.Run(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            null,
            mode);

        // Assert
        Assert.Equal(1, exitCode);
    }

    /// <summary>Verifies the legacy identity cannot restore machine-sensitive soak rejection.</summary>
    /// <param name="scenario">The diagnostic soak scenario.</param>
    /// <param name="metric">The retained threshold observation.</param>
    /// <param name="value">A finite observation beyond the diagnostic threshold.</param>
    [Theory]
    [InlineData("soak.working-set-stabilization", "workingSetGrowthBytes", 2048)]
    [InlineData("soak.working-set-stabilization", "managedHeapGrowthBytes", 2048)]
    [InlineData("soak.concurrent-throughput-retention", "throughputRetentionRatio", 0.1)]
    public void Legacy_full_identity_cannot_restore_soak_timing_rejection(
        string scenario,
        string metric,
        double value
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create();
        var soakPath = WriteChangedSoak(fixture, scenario, metric, value);

        // Act
        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "scorecard",
            soakPath,
            PerformanceEvaluationMode.Full);

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Empty(result.Regressions);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains(metric, StringComparison.Ordinal));
    }

    /// <summary>Verifies a standalone soak cannot reject machine-dependent threshold observations.</summary>
    /// <param name="scenarioId">The runner-owned diagnostic scenario.</param>
    [Theory]
    [InlineData("soak.working-set-stabilization")]
    [InlineData("soak.concurrent-throughput-retention")]
    public void Standalone_soak_keeps_diagnostic_thresholds_out_of_its_exit_status(
        string scenarioId
    )
    {
        // Arrange
        var metric = scenarioId == "soak.working-set-stabilization"
            ? "workingSetGrowthBytes"
            : "throughputRetentionRatio";

        SoakScenarioResult[] scenarios =
        [
            new()
            {
                Id = scenarioId,
                Success = false,
                Metrics = new Dictionary<string, double> { [metric] = 1_000_000 },
                Error = "One or more sustained-use budgets were exceeded.",
            },
        ];

        // Act
        var rejects = PerformanceSoakRunner.HasRejectingFailure(scenarios);

        // Assert
        Assert.False(rejects);
        Assert.False(scenarios[0].Success);
    }

    /// <summary>Verifies resource failures, incomplete measurements and execution exceptions still reject.</summary>
    /// <param name="scenarioId">The measured scenario.</param>
    /// <param name="includeMetric">Whether the scenario produced observations.</param>
    /// <param name="executionError">Whether an actual operation failed instead of a threshold crossing.</param>
    [Theory]
    [InlineData("soak.hilo-cache-bound", true, false)]
    [InlineData("soak.pooled-buffer-return", true, false)]
    [InlineData("soak.connection-cleanup", true, false)]
    [InlineData("soak.migration-lock-cleanup", true, false)]
    [InlineData("soak.working-set-stabilization", false, true)]
    [InlineData("soak.concurrent-throughput-retention", false, true)]
    [InlineData("soak.working-set-stabilization", true, true)]
    [InlineData("soak.concurrent-throughput-retention", true, true)]
    public void Standalone_soak_preserves_resource_and_execution_failures(
        string scenarioId,
        bool includeMetric,
        bool executionError
    )
    {
        // Arrange
        var metric = scenarioId switch
        {
            "soak.working-set-stabilization" => "workingSetGrowthBytes",
            "soak.concurrent-throughput-retention" => "throughputRetentionRatio",
            _ => "heldLocks",
        };

        SoakScenarioResult[] scenarios =
        [
            new()
            {
                Id = scenarioId,
                Success = false,
                Metrics = includeMetric
                    ? new Dictionary<string, double> { [metric] = 1 }
                    : [],
                Error = executionError
                    ? "The native command failed."
                    : "One or more sustained-use budgets were exceeded.",
            },
        ];

        // Act
        var rejects = PerformanceSoakRunner.HasRejectingFailure(scenarios);

        // Assert
        Assert.True(rejects);
    }

    /// <summary>Verifies a diagnostic scenario label cannot conceal an enforced resource observation.</summary>
    [Fact]
    public void Standalone_soak_rejects_enforced_metrics_inside_diagnostic_scenarios()
    {
        // Arrange
        SoakScenarioResult[] scenarios =
        [
            new()
            {
                Id = "soak.working-set-stabilization",
                Success = false,
                Metrics = new Dictionary<string, double>
                {
                    ["workingSetGrowthBytes"] = 2048,
                    ["heldLocks"] = 1,
                },
                Error = "One or more sustained-use budgets were exceeded.",
            },
        ];

        // Act
        var rejects = PerformanceSoakRunner.HasRejectingFailure(scenarios);

        // Assert
        Assert.True(rejects);
    }

    /// <summary>Verifies future metrics need a classification instead of silently bypassing enforcement.</summary>
    [Fact]
    public void Standalone_soak_rejects_unclassified_observations()
    {
        // Arrange
        SoakScenarioResult[] scenarios =
        [
            new()
            {
                Id = "soak.working-set-stabilization",
                Success = false,
                Metrics = new Dictionary<string, double> { ["newMetric"] = 1 },
                Error = "One or more sustained-use budgets were exceeded.",
            },
        ];

        // Act
        var exception = Record.Exception(() => PerformanceSoakRunner.HasRejectingFailure(scenarios));

        // Assert
        Assert.IsType<InvalidDataException>(exception);
    }

    /// <summary>Verifies a diagnostic label cannot make non-finite observations usable.</summary>
    [Fact]
    public void Standalone_soak_rejects_nonfinite_diagnostic_observations()
    {
        // Arrange
        SoakScenarioResult[] scenarios =
        [
            new()
            {
                Id = "soak.working-set-stabilization",
                Success = false,
                Metrics = new Dictionary<string, double> { ["workingSetGrowthBytes"] = double.NaN },
                Error = "One or more sustained-use budgets were exceeded.",
            },
        ];

        // Act
        var rejects = PerformanceSoakRunner.HasRejectingFailure(scenarios);

        // Assert
        Assert.True(rejects);
    }
}
