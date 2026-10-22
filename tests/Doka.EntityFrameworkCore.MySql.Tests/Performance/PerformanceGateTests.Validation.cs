namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class PerformanceGateTests
{
    /// <summary>
    /// Verifies gate rejects incomplete ambiguous or malformed control maxima.
    /// </summary>
    /// <param name="maximumProperties">The incomplete, ambiguous, or malformed JSON maximum properties.</param>
    /// <param name="expectedError">The expected validation diagnostic.</param>
    [Theory]
    [InlineData("", "exactly one")]
    [InlineData(""" "maximum": 1, "maximumByTarget": {} """, "exactly one")]
    [InlineData(""" "maximum": 1, "maximum": 2 """, "exactly one")]
    [InlineData(""" "maximumByTarget": {}, "maximumByTarget": {} """, "exactly one")]
    [InlineData(""" "maximumByTarget": null """, "must be an object")]
    [InlineData(""" "maximumByTarget": [] """, "must be an object")]
    [InlineData(""" "maximumByTarget": 2 """, "must be an object")]
    [InlineData(""" "maximumByTarget": {} """, "missing target")]
    [InlineData(""" "maximumByTarget": {"mysql84": 1.5} """, "missing target 'mariadb114'")]
    [InlineData(""" "maximumByTarget": {"mariadb114": 2.5} """, "missing target 'mysql84'")]
    [InlineData(
        """ "maximumByTarget": {"mysql84": 1.5, "mariadb114": 2.5, "other": 3} """,
        "unknown target 'other'")]
    [InlineData(
        """ "maximumByTarget": {"mysql84": 1.5, "MariaDb114": 2.5} """,
        "unknown target 'MariaDb114'")]
    [InlineData(
        """ "maximumByTarget": {"mysql84": 1.5, "mysql84": 100, "mariadb114": 2.5} """,
        "repeats target 'mysql84'")]
    [InlineData(""" "maximumByTarget": {"mysql84": "1.5", "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": true, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": null, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": {}, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": 1e999, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": -1, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": 1.5, "mariadb114": -1} """, "finite nonnegative")]
    [InlineData(""" "maximum": "1.5" """, "finite nonnegative")]
    [InlineData(""" "maximum": null """, "finite nonnegative")]
    [InlineData(""" "maximum": -1 """, "finite nonnegative")]
    [InlineData(""" "maximum": 1e999 """, "finite nonnegative")]
    public void Gate_rejects_incomplete_ambiguous_or_malformed_control_maxima(
        string maximumProperties,
        string expectedError
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(maximumProperties),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 3),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        // Act
        var result = fixture.Evaluate();

        var exitCode = fixture.Run();

        // Assert
        Assert.Contains(result.InvalidEvidence, error => error.Contains(expectedError, StringComparison.Ordinal));
        Assert.Empty(result.Regressions);
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, exitCode);
    }

    /// <summary>
    /// Verifies gate requires exactly one measured and baseline observation.
    /// </summary>
    /// <param name="method">The method whose observation count is changed.</param>
    /// <param name="count">The missing or duplicate observation count.</param>
    [Theory]
    [InlineData("Measured", 0)]
    [InlineData("Measured", 2)]
    [InlineData("Baseline", 0)]
    [InlineData("Baseline", 2)]
    public void Gate_requires_exactly_one_measured_and_baseline_observation(
        string method,
        int count
    )
    {
        // Arrange
        var observations = Enumerable.Repeat(
                PerformanceGateFixture.ControlObservation("Measured", 3),
                method == "Measured" ? count : 1)
            .Concat(Enumerable.Repeat(
                PerformanceGateFixture.ControlObservation("Baseline", 2),
                method == "Baseline" ? count : 1))
            .ToArray();

        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(""" "maximum": 1.5 """),
            controlObservations: observations);

        // Act
        var result = fixture.Evaluate();

        var exitCode = fixture.Run();

        // Assert
        Assert.Contains(
            result.InvalidEvidence,
            error => error.Contains($"ControlBenchmark.{method} result, found {count}", StringComparison.Ordinal));
        Assert.Empty(result.Regressions);
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, exitCode);
    }

    /// <summary>
    /// Verifies gate accepts a zero allocation baseline only for zero allocations.
    /// </summary>
    /// <param name="allocatedBytes">The measured allocation count against a zero-allocation baseline.</param>
    /// <param name="expectedExitCode">The expected gate result.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 78)]
    public void Gate_accepts_a_zero_allocation_baseline_only_for_zero_allocations(
        double allocatedBytes,
        int expectedExitCode
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(""" "maximum": 0 """, metric: "allocationRatio"),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 3, allocatedBytes),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        // Act
        var result = fixture.Evaluate();

        var exitCode = fixture.Run();

        // Assert
        Assert.Equal(expectedExitCode == 0, result.InvalidEvidence.Count == 0);
        Assert.Empty(result.Regressions);
        Assert.Equal(expectedExitCode, exitCode);
    }

    /// <summary>
    /// Verifies driver rejects a zero mean baseline as invalid measurement evidence.
    /// </summary>
    [Fact]
    public async Task Driver_rejects_a_zero_mean_baseline_as_invalid_measurement_evidence()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(""" "maximum": 1.5 """),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 3),
                PerformanceGateFixture.ControlObservation("Baseline", 0),
            ]);
        Action act = () => fixture.Evaluate();

        // Act
        var recordedException = Record.Exception(act);
        var exitCode = await Benchmarks.Program.Main(
            ["--evaluate", fixture.ContractPath, fixture.ReportsPath, "mysql84", "smoke"]);

        // Assert
        var exception = Assert.IsType<InvalidDataException>(recordedException);
        Assert.Contains("no finite positive samples", exception.Message);
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, exitCode);
    }

    /// <summary>
    /// Verifies gate preserves invalid evidence precedence when a budget also regresses.
    /// </summary>
    [Fact]
    public void Gate_preserves_invalid_evidence_precedence_when_a_budget_also_regresses()
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            medianBudget: 4,
            controlsJson: RatioControl(""" "maximumByTarget": {"mysql84": 1.5} """),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 3),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        // Act
        var result = fixture.Evaluate();

        var exitCode = fixture.Run();

        // Assert
        Assert.Contains(
            result.InvalidEvidence,
            error => error.Contains("missing target 'mariadb114'", StringComparison.Ordinal));
        Assert.Contains(
            result.Regressions,
            error => error.Contains("medianNanoseconds", StringComparison.Ordinal));
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, exitCode);
    }
}
