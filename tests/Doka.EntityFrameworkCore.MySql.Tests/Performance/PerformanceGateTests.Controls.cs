namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class PerformanceGateTests
{
    /// <summary>
    /// Verifies gate uses the selected target maximum and accepts its boundary.
    /// </summary>
    /// <param name="target">The target whose control maximum applies.</param>
    /// <param name="measuredMean">The measured control mean.</param>
    /// <param name="expectedExitCode">The expected gate result.</param>
    [Theory]
    [InlineData("mysql84", 3, 0)]
    [InlineData("mysql84", 3.01, 1)]
    [InlineData("mysql84", 4, 1)]
    [InlineData("mariadb114", 4, 0)]
    [InlineData("mariadb114", 5, 0)]
    [InlineData("mariadb114", 5.01, 1)]
    public void Gate_uses_the_selected_target_maximum_and_accepts_its_boundary(
        string target,
        double measuredMean,
        int expectedExitCode
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            reportTarget: target,
            controlsJson: RatioControl(""" "maximumByTarget": {"mysql84": 1.5, "mariadb114": 2.5} """),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", measuredMean),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        // Act
        var result = fixture.Evaluate(target);

        var exitCode = fixture.Run(target);

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Equal(expectedExitCode, result.Regressions.Count);
        Assert.Equal(expectedExitCode, exitCode);
    }

    /// <summary>
    /// Verifies gate detects slower execution without any allocation change.
    /// </summary>
    /// <param name="measuredMean">The measured control mean with unchanged allocation evidence.</param>
    /// <param name="expectedExitCode">The expected gate result.</param>
    [Theory]
    [InlineData(3, 0)]
    [InlineData(6, 1)]
    public void Gate_detects_slower_execution_without_any_allocation_change(
        double measuredMean,
        int expectedExitCode
    )
    {
        // Arrange
        using var fixture = PerformanceGateFixture.Create(
            controlsJson:
            """
            [
                {
                    "id": "allocation-control",
                    "type": "ControlBenchmark",
                    "method": "Measured",
                    "metric": "allocatedBytes",
                    "maximum": 0
                },
                {
                    "id": "ratio-control",
                    "type": "ControlBenchmark",
                    "method": "Measured",
                    "baselineMethod": "Baseline",
                    "metric": "meanRatio",
                    "maximum": 1.5
                }
            ]
            """,
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", measuredMean),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        // Act
        var result = fixture.Evaluate();

        var exitCode = fixture.Run();

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Equal(2, result.ControlCount);
        Assert.Equal(expectedExitCode, result.Regressions.Count);
        Assert.All(result.Regressions, error => Assert.Contains("ratio-control meanRatio", error));
        Assert.Equal(expectedExitCode, exitCode);
    }

    /// <summary>
    /// Verifies the checked-in sliding-cache controls enforce one engine's time budget independently of allocations.
    /// </summary>
    /// <param name="target">The target selected from the checked-in contract.</param>
    /// <param name="meanMultiplier">The measured mean relative to the exact target maximum.</param>
    /// <param name="expectedExitCode">The expected gate result.</param>
    [Theory]
    [MemberData(nameof(CacheSlidingControlCases))]
    public void Cache_sliding_controls_enforce_checked_in_target_time_budgets_without_allocation_changes(
        string target,
        double meanMultiplier,
        int expectedExitCode
    )
    {
        // Arrange
        using var document = LoadCheckedInPerformanceContract();
        var contract = document.RootElement;
        var controls = contract.GetProperty("benchmarkDotNetControls").EnumerateArray().ToArray();
        var throughput = controls.Single(control =>
            control.GetProperty("id").GetString() == "cache-parallel-sliding-buffer-throughput");

        var allocation = controls.Single(control =>
            control.GetProperty("id").GetString() == "cache-parallel-sliding-buffer-allocation");

        var type = nameof(Benchmarks.DistributedCacheBenchmark);
        var measuredMethod = nameof(Benchmarks.DistributedCacheBenchmark.ParallelSlidingBufferReadsAsync);
        var baselineMethod = nameof(Benchmarks.DistributedCacheBenchmark.ParallelBufferReadsAsync);
        var maximum = throughput.GetProperty("maximumByTarget").GetProperty(target).GetDouble();
        using var fixture = PerformanceGateFixture.Create(
            reportTarget: target,
            controlsJson: JsonSerializer.Serialize(new[] { throughput, allocation }),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation(measuredMethod, maximum * meanMultiplier, type: type),
                PerformanceGateFixture.ControlObservation(baselineMethod, 1, type: type),
            ],
            requiredTargets: contract.GetProperty("requiredTargets"));

        // Act
        var result = fixture.Evaluate(target);
        var exitCode = fixture.Run(target);

        // Assert
        Assert.Equal(11, contract.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(type, throughput.GetProperty("type").GetString());
        Assert.Equal(measuredMethod, throughput.GetProperty("method").GetString());
        Assert.Equal(baselineMethod, throughput.GetProperty("baselineMethod").GetString());
        Assert.Equal("meanRatio", throughput.GetProperty("metric").GetString());
        Assert.Equal(type, allocation.GetProperty("type").GetString());
        Assert.Equal(measuredMethod, allocation.GetProperty("method").GetString());
        Assert.Equal("allocatedBytes", allocation.GetProperty("metric").GetString());
        Assert.Empty(result.InvalidEvidence);
        Assert.Equal(2, result.ControlCount);
        Assert.Equal(expectedExitCode, result.Regressions.Count);
        Assert.All(result.Regressions, error => Assert.Contains("cache-parallel-sliding-buffer-throughput", error));
        Assert.Equal(expectedExitCode, exitCode);
    }

    /// <summary>
    /// Verifies parameterized collection controls accept the calibrated relative profile.
    /// </summary>
    [Fact]
    public void Parameterized_collection_controls_accept_the_calibrated_relative_profile()
    {
        // Arrange
        var controlsJson = ParameterizedCollectionControlsJson();
        var observations = ParameterizedCollectionObservations(
            binaryJsonMean: 0.6,
            charJsonAllocatedBytes: 0.3);

        using var fixture = PerformanceGateFixture.Create(
            controlsJson: controlsJson,
            controlObservations: observations);

        // Act
        var result = fixture.Evaluate();

        // Assert
        Assert.Empty(result.InvalidEvidence);
        Assert.Empty(result.Regressions);
        Assert.Equal(7, result.ControlCount);
    }

    /// <summary>
    /// Verifies parameterized collection controls reject a binary16 throughput regression.
    /// </summary>
    [Fact]
    public void Parameterized_collection_controls_reject_a_binary16_throughput_regression()
    {
        // Arrange
        var controlsJson = ParameterizedCollectionControlsJson();
        var observations = ParameterizedCollectionObservations(
            binaryJsonMean: 0.81,
            charJsonAllocatedBytes: 0.3);

        using var fixture = PerformanceGateFixture.Create(
            controlsJson: controlsJson,
            controlObservations: observations);

        // Act
        var result = fixture.Evaluate();

        // Assert
        var regression = Assert.Single(result.Regressions);
        Assert.Empty(result.InvalidEvidence);
        Assert.Contains("parameterized-collection-binary16-multiple-throughput", regression);
    }

    /// <summary>
    /// Verifies parameterized collection controls reject a char36 allocation regression.
    /// </summary>
    [Fact]
    public void Parameterized_collection_controls_reject_a_char36_allocation_regression()
    {
        // Arrange
        var controlsJson = ParameterizedCollectionControlsJson();
        var observations = ParameterizedCollectionObservations(
            binaryJsonMean: 0.6,
            charJsonAllocatedBytes: 0.36);

        using var fixture = PerformanceGateFixture.Create(
            controlsJson: controlsJson,
            controlObservations: observations);

        // Act
        var result = fixture.Evaluate();

        // Assert
        var regression = Assert.Single(result.Regressions);
        Assert.Empty(result.InvalidEvidence);
        Assert.Contains("parameterized-collection-char36-multiple-allocation", regression);
    }

    /// <summary>
    /// Verifies one checked-in generic LIKE allocation control enforces its budget and requires observations.
    /// </summary>
    /// <param name="controlId">The allocation control selected from the checked-in contract.</param>
    /// <param name="allocationExcess">The bytes measured above its exact allocation maximum.</param>
    /// <param name="includeObservation">Whether the benchmark result is supplied.</param>
    /// <param name="expectedExitCode">The expected gate result.</param>
    [Theory]
    [MemberData(nameof(GenericLikeControlCases))]
    public void Generic_like_controls_enforce_actual_allocation_budgets_and_require_evidence(
        string controlId,
        int allocationExcess,
        bool includeObservation,
        int expectedExitCode
    )
    {
        // Arrange
        using var document = LoadCheckedInPerformanceContract();
        var controls = document.RootElement
            .GetProperty("benchmarkDotNetControls")
            .EnumerateArray()
            .Where(control => control.GetProperty("type").GetString() == nameof(Benchmarks.GenericLikeBenchmark))
            .ToArray();

        var control = controls.Single(candidate => candidate.GetProperty("id").GetString() == controlId);
        var method = control.GetProperty("method").GetString()!;
        var maximum = control.GetProperty("maximum").GetDouble();
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: JsonSerializer.Serialize(new[] { control }),
            controlObservations: includeObservation
                ? [PerformanceGateFixture.ControlObservation(
                    method,
                    mean: 1,
                    allocatedBytes: maximum + allocationExcess,
                    type: nameof(Benchmarks.GenericLikeBenchmark))]
                : []);

        // Act
        var result = fixture.Evaluate();
        var exitCode = fixture.Run();

        // Assert
        Assert.Equal(2, controls.Length);
        Assert.Equal("allocatedBytes", control.GetProperty("metric").GetString());
        Assert.Equal(includeObservation ? 0 : 1, result.InvalidEvidence.Count);
        Assert.Equal(allocationExcess, result.Regressions.Count);
        Assert.All(result.Regressions, error => Assert.Contains($"{controlId} allocatedBytes", error));
        Assert.Equal(expectedExitCode, exitCode);
    }
}
