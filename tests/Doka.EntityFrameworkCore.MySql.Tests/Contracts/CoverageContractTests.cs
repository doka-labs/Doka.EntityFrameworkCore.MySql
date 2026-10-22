namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies coverage evidence freshness, identity and aggregation contracts.</summary>
public sealed class CoverageContractTests
{
    /// <summary>
    /// Verifies coverage contract accepts fresh evidence at every floor.
    /// </summary>
    [Fact]
    public void Coverage_contract_accepts_fresh_evidence_at_every_floor()
    {
        // Arrange
        using var fixture = CoverageFixture.Create();

        // Act
        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        // Assert
        Assert.Empty(evaluation.Errors);
        Assert.Collection(
            evaluation.Results,
            result => Assert.Contains("assembly Provider", result, StringComparison.Ordinal),
            result => Assert.Contains("critical class Provider.Critical", result, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies coverage contract rejects stale missing and below floor evidence.
    /// </summary>
    [Theory]
    [InlineData("stale", "old")]
    [InlineData("missing", "missing shipped assembly")]
    [InlineData("line_floor", "line coverage")]
    [InlineData("branch_floor", "branch coverage")]
    public void Coverage_contract_rejects_stale_missing_and_below_floor_evidence(
        string scenario,
        string expectedError
    )
    {
        // Arrange
        using var fixture = CoverageFixture.Create(
            assemblyName: scenario == "missing" ? "Other.Assembly" : "Provider",
            className: scenario == "missing" ? "Other.Critical" : "Provider.Critical",
            lineHits: scenario == "line_floor" ? (1, 0) : (1, 1),
            branchFraction: scenario == "branch_floor" ? "0% (0/2)" : "50% (1/2)");

        // Act
        var invalid = CoverageContract.Evaluate(
            fixture.ReportPath,
            fixture.PolicyPath,
            nowTimestamp: scenario == "stale" ? 2000 : 1001);

        // Assert
        Assert.Contains(invalid.Errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies coverage contract rejects stale raw input before merge.
    /// </summary>
    [Fact]
    public void Coverage_contract_rejects_stale_raw_input_before_merge()
    {
        // Arrange
        using var fixture = CoverageFixture.Create();

        // Act
        var errors = CoverageContract.EvaluateFreshness([fixture.ReportPath], fixture.PolicyPath, nowTimestamp: 2000);

        // Assert
        Assert.Single(errors);
        Assert.Contains("old", errors[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies coverage contract aggregates distinct partial class sources.
    /// </summary>
    [Fact]
    public void Coverage_contract_aggregates_distinct_partial_class_sources()
    {
        // Arrange
        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Critical.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"50% (1/2)\" />"
            + "</lines></class>"
            + "<class name=\"Provider.Critical\" filename=\"Critical.Partial.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" />"
            + "</lines></class>");

        // Act
        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        // Assert
        Assert.Empty(evaluation.Errors);
        Assert.Contains("lines 2/2", evaluation.Results[1], StringComparison.Ordinal);
        Assert.Contains("branches 1/2", evaluation.Results[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies coverage contract rejects ambiguous partial class sources.
    /// </summary>
    [Fact]
    public void Coverage_contract_rejects_ambiguous_partial_class_sources()
    {
        // Arrange
        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Critical.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"50% (1/2)\" />"
            + "</lines></class>"
            + "<class name=\"Provider.Critical\" filename=\"Critical.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" />"
            + "</lines></class>");

        // Act
        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        // Assert
        Assert.Contains(
            evaluation.Errors,
            error => error.Contains("ambiguous source fragments", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies coverage contract accepts an explicitly branch free surface.
    /// </summary>
    [Fact]
    public void Coverage_contract_accepts_an_explicitly_branch_free_surface()
    {
        // Arrange
        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Critical.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" />"
            + "<line number=\"2\" hits=\"1\" />"
            + "</lines></class>"
            + "<class name=\"Provider.Helper\" filename=\"Helper.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"100% (2/2)\" />"
            + "</lines></class>",
            criticalMinimumBranchPercent: null);

        // Act
        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        // Assert
        Assert.Empty(evaluation.Errors);
        Assert.Contains("branches 0/0", evaluation.Results[1], StringComparison.Ordinal);
        Assert.Contains("minimum N/A", evaluation.Results[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies coverage contract rejects missing or zero branch floors.
    /// </summary>
    [Theory]
    [InlineData(null, "declares no branch floor")]
    [InlineData(0d, "greater than zero")]
    public void Coverage_contract_rejects_missing_or_zero_branch_floors(
        double? branchFloor,
        string expectedError
    )
    {
        // Arrange
        using var fixture = CoverageFixture.Create(criticalMinimumBranchPercent: branchFloor);

        // Act
        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        // Assert
        Assert.Contains(evaluation.Errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    private sealed class CoverageFixture : IDisposable
    {
        private CoverageFixture(
            string path
        )
        {
            Path = path;
        }

        public string Path { get; }

        public string ReportPath => System.IO.Path.Combine(Path, "coverage.cobertura.xml");

        public string PolicyPath => System.IO.Path.Combine(Path, "coverage-policy.json");

        public static CoverageFixture Create(
            string assemblyName = "Provider",
            string className = "Provider.Critical",
            (int First, int Second)? lineHits = null,
            string branchFraction = "50% (1/2)",
            string? classElements = null,
            double? criticalMinimumBranchPercent = 50
        )
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"doka-coverage-contract-{Guid.NewGuid():N}");

            Directory.CreateDirectory(path);
            var fixture = new CoverageFixture(path);
            var hits = lineHits ?? (1, 1);
            classElements ??= $"<class name=\"{className}\"><lines>"
                + $"<line number=\"1\" hits=\"{hits.First}\" branch=\"true\" "
                + $"condition-coverage=\"{branchFraction}\" />"
                + $"<line number=\"2\" hits=\"{hits.Second}\" />"
                + "</lines></class>";
            File.WriteAllText(
                fixture.ReportPath,
                "<coverage timestamp=\"1000\"><packages>"
                + $"<package name=\"{assemblyName}\"><classes>{classElements}</classes></package>"
                + "</packages></coverage>");
            File.WriteAllText(
                fixture.PolicyPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        evidenceMaxAgeSeconds = 100,
                        assemblies = new[]
                        {
                            new
                            {
                                name = "Provider",
                                minimumLinePercent = 100,
                                minimumBranchPercent = 50,
                            },
                        },
                        criticalClasses = new[]
                        {
                            new
                            {
                                assembly = "Provider",
                                name = "Provider.Critical",
                                minimumLinePercent = 100,
                                minimumBranchPercent = criticalMinimumBranchPercent,
                            },
                        },
                    }));

            return fixture;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
