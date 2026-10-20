namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class PerformanceGateTests
{
    /// <summary>Verifies the actual evaluator process applies the hosted policy before choosing its exit code.</summary>
    /// <param name="actions">The hosted execution marker.</param>
    /// <param name="invalidSamples">Whether raw report statistics contradict their samples.</param>
    /// <param name="expectedExitCode">The required command-line verdict.</param>
    [Theory]
    [InlineData("true", false, 0)]
    [InlineData("false", false, 1)]
    [InlineData("true", true, 78)]
    public async Task Evaluator_process_keeps_hosted_timing_diagnostic_without_masking_bad_evidence(
        string actions,
        bool invalidSamples,
        int expectedExitCode
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            timeMultiplier: 100000, sampleCountOffset: invalidSamples ? 1 : 0);
        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in new[]
                 {
                     typeof(Benchmarks.Program).Assembly.Location, "--evaluate", fixture.ContractPath,
                     fixture.ReportsPath, "mysql84", "smoke"
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GITHUB_ACTIONS"] = actions;
        startInfo.Environment["DOKA_BENCHMARK_EVALUATION"] = "full";

        // Act
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the benchmark evaluator.");

        // WHY: Drain both streams with the same non-cancellable lifetime as the
        // owned evaluator below, so cancellation cannot leave unread output.
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None);
        var diagnostics = await output + await error;

        // Assert
        Assert.True(process.ExitCode == expectedExitCode, diagnostics);
    }

    /// <summary>Verifies every hosted request resolves to allocation-only enforcement.</summary>
    /// <param name="requested">The requested local evaluation mode.</param>
    /// <param name="actions">The GitHub Actions marker.</param>
    /// <param name="allocationsOnly">Whether host-sensitive budgets must be diagnostic.</param>
    [Theory]
    [InlineData(null, "true", true)]
    [InlineData("full", "true", true)]
    [InlineData("full", "TRUE", true)]
    [InlineData("allocations-only", "true", true)]
    [InlineData("allocations-only", null, true)]
    [InlineData(null, null, false)]
    [InlineData("full", "false", false)]
    public void Hosted_policy_cannot_enable_hardware_sensitive_rejection(
        string? requested,
        string? actions,
        bool allocationsOnly
    )
    {
        // Arrange
        var expected = allocationsOnly
            ? Benchmarks.PerformanceEvaluationMode.AllocationsOnly
            : Benchmarks.PerformanceEvaluationMode.Full;

        // Act
        var actual = Benchmarks.PerformanceEvaluationPolicy.Resolve(requested, actions);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Verifies misspelled policies fail instead of silently weakening checks.</summary>
    /// <param name="requested">The unsupported mode.</param>
    [Theory]
    [InlineData("allocation")]
    [InlineData("FULL")]
    [InlineData(" ")]
    public void Evaluation_policy_rejects_unknown_modes(
        string requested
    )
    {
        // Arrange
        Action act = () => Benchmarks.PerformanceEvaluationPolicy.Resolve(requested, "true");

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>Verifies a slower CPU and different GC cadence do not reject identical allocations.</summary>
    /// <param name="timeMultiplier">The simulated CPU slowdown.</param>
    [Theory]
    [InlineData(10)]
    [InlineData(100000)]
    public void Hosted_workloads_report_timing_and_GC_thresholds_without_rejection(
        double timeMultiplier
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(timeMultiplier: timeMultiplier, gen2Collections: 100);

        // Act
        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "smoke", null,
            Benchmarks.PerformanceEvaluationMode.AllocationsOnly);

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Empty(result.Regressions);
        Assert.Equal(4, result.Diagnostics.Count);
        Assert.Contains(result.Diagnostics, error => error.Contains("medianNanoseconds", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, error => error.Contains("p95Nanoseconds", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, error => error.Contains("p99Nanoseconds", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, error => error.Contains("gen2CollectionsPer1000", StringComparison.Ordinal));
    }

    /// <summary>Verifies hosted controls do not treat timing ratios as hardware independent.</summary>
    [Fact]
    public void Hosted_time_ratio_violation_remains_diagnostic()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(""" "maximum": 1 """),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 1000),
                PerformanceGateFixture.ControlObservation("Baseline", 1),
            ]);

        // Act
        var exitCode = BenchmarkPerformanceGate.Run(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "smoke", null,
            Benchmarks.PerformanceEvaluationMode.AllocationsOnly);

        // Assert
        Assert.Equal(0, exitCode);
    }

    /// <summary>Verifies actual allocation regressions still reject hosted controls.</summary>
    /// <param name="metric">The enforced allocation metric.</param>
    [Theory]
    [InlineData("allocatedBytes")]
    [InlineData("allocationRatio")]
    public void Hosted_controls_reject_allocation_regressions(
        string metric
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(""" "maximum": 1 """, metric),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 1000, allocatedBytes: 10),
                PerformanceGateFixture.ControlObservation("Baseline", 1, allocatedBytes: 1),
            ]);

        // Act
        var exitCode = BenchmarkPerformanceGate.Run(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "smoke", null,
            Benchmarks.PerformanceEvaluationMode.AllocationsOnly);

        // Assert
        Assert.Equal(1, exitCode);
    }

    /// <summary>Verifies per-operation allocation budgets remain enforced for provider workloads.</summary>
    [Fact]
    public void Hosted_workloads_reject_allocation_regressions()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(workloadAllocatedBytes: 1000);

        // Act
        var exitCode = BenchmarkPerformanceGate.Run(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "smoke", null,
            Benchmarks.PerformanceEvaluationMode.AllocationsOnly);

        // Assert
        Assert.Equal(1, exitCode);
    }

    /// <summary>Verifies diagnostic-only controls still require their complete baseline evidence.</summary>
    [Fact]
    public void Hosted_diagnostics_do_not_accept_missing_baseline_evidence()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(""" "maximum": 1 """),
            controlObservations: [PerformanceGateFixture.ControlObservation("Measured", 1000)]);

        // Act
        var exitCode = BenchmarkPerformanceGate.Run(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "smoke", null,
            Benchmarks.PerformanceEvaluationMode.AllocationsOnly);

        // Assert
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, exitCode);
    }

    /// <summary>Verifies resource ownership remains rejecting while host-dependent soak signals do not.</summary>
    /// <param name="scenario">The changed soak scenario.</param>
    /// <param name="metric">The changed raw metric.</param>
    /// <param name="value">The measured budget violation.</param>
    /// <param name="expectedExitCode">The policy-specific gate result.</param>
    [Theory]
    [InlineData("soak.working-set-stabilization", "workingSetGrowthBytes", 2048, 0)]
    [InlineData("soak.working-set-stabilization", "managedHeapGrowthBytes", 2048, 0)]
    [InlineData("soak.concurrent-throughput-retention", "throughputRetentionRatio", 0.1, 0)]
    [InlineData("soak.hilo-cache-bound", "cacheEntries", 2048, 1)]
    [InlineData("soak.pooled-buffer-return", "outstandingBuffers", 1, 1)]
    [InlineData("soak.connection-cleanup", "connectionDelta", 2, 1)]
    [InlineData("soak.migration-lock-cleanup", "heldLocks", 1, 1)]
    public void Hosted_soak_distinguishes_diagnostics_from_resource_regressions(
        string scenario,
        string metric,
        double value,
        int expectedExitCode
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create();
        var soakPath = WriteChangedSoak(fixture, scenario, metric, value);

        // Act
        var exitCode = BenchmarkPerformanceGate.Run(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "scorecard", soakPath,
            Benchmarks.PerformanceEvaluationMode.AllocationsOnly);

        // Assert
        Assert.Equal(expectedExitCode, exitCode);
    }

    /// <summary>Verifies missing raw metrics cannot disguise execution errors as diagnostic violations.</summary>
    [Fact]
    public void Hosted_soak_rejects_failed_diagnostic_scenarios_without_metrics()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create();
        var soakPath = WriteChangedSoak(
            fixture, "soak.concurrent-throughput-retention", "throughputRetentionRatio", null);

        // Act
        var exitCode = BenchmarkPerformanceGate.Run(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "scorecard", soakPath,
            Benchmarks.PerformanceEvaluationMode.AllocationsOnly);

        // Assert
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, exitCode);
    }

    /// <summary>Verifies a rewritten success flag cannot erase a measured diagnostic threshold crossing.</summary>
    [Fact]
    public void Hosted_soak_rejects_rewritten_raw_success_flags()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create();
        var soakPath = WriteChangedSoak(
            fixture, "soak.concurrent-throughput-retention", "throughputRetentionRatio", 0.1,
            reportSuccess: true);

        // Act
        var exitCode = BenchmarkPerformanceGate.Run(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "scorecard", soakPath,
            Benchmarks.PerformanceEvaluationMode.AllocationsOnly);

        // Assert
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, exitCode);
    }

    /// <summary>Verifies a complete passing soak remains valid under either enforcement policy.</summary>
    /// <param name="allocationsOnly">Whether hardware-sensitive thresholds are diagnostic.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Gate_accepts_complete_passing_soak_under_either_policy(
        bool allocationsOnly
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create();
        var soakPath = fixture.WriteSoak(managedHeapGrowthBytes: 0);
        var mode = allocationsOnly
            ? Benchmarks.PerformanceEvaluationMode.AllocationsOnly
            : Benchmarks.PerformanceEvaluationMode.Full;

        // Act
        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "scorecard", soakPath, mode);

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Empty(result.Regressions);
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>Verifies unknown metrics require an explicit enforcement decision.</summary>
    [Fact]
    public void Hosted_policy_rejects_unclassified_metrics()
    {
        // Arrange
        Action act = () => Benchmarks.PerformanceEvaluationPolicy.IsDiagnostic(
            Benchmarks.PerformanceEvaluationMode.AllocationsOnly, "newMetric");

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidDataException>(exception);
    }

    /// <summary>Verifies an inconsistent internal evaluation mode cannot weaken the validator.</summary>
    [Fact]
    public void Gate_rejects_undefined_evaluation_modes()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create();
        Action act = () => BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath, fixture.ReportsPath, "mysql84", "smoke", null,
            (Benchmarks.PerformanceEvaluationMode)42);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    private static string WriteChangedSoak(
        PerformanceGateFixture fixture,
        string scenarioId,
        string metric,
        double? value,
        bool reportSuccess = false
    )
    {
        var path = fixture.WriteSoak(managedHeapGrowthBytes: 0);
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var scenario = root["scenarios"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == scenarioId)!;
        var metrics = scenario["metrics"]!.AsObject();
        if (value is not null)
        {
            metrics[metric] = value.Value;
        }
        else
        {
            metrics.Remove(metric);
        }

        scenario["success"] = false;
        root["success"] = reportSuccess;
        File.WriteAllText(path, root.ToJsonString());

        return path;
    }
}
