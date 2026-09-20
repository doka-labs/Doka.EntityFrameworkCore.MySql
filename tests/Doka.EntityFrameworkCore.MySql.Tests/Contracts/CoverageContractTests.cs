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
            assemblyName: scenario == "missing"
                ? "Other.Assembly"
                : "Provider",
            className: scenario == "missing"
                ? "Other.Critical"
                : "Provider.Critical",
            lineHits: scenario == "line_floor"
                ? (1, 0)
                : (1, 1),
            branchFraction: scenario == "branch_floor"
                ? "0% (0/2)"
                : "50% (1/2)");

        // Act
        var invalid = CoverageContract.Evaluate(
            fixture.ReportPath,
            fixture.PolicyPath,
            nowTimestamp: scenario == "stale"
                ? 2000
                : 1001);

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

    /// <summary>Rejects canonical coverage below its floor despite covered method-detail rows.</summary>
    /// <param name="metric">The canonical metric that falls below the configured floor.</param>
    /// <param name="useNamespace">Whether the Cobertura elements use a default XML namespace.</param>
    [Theory]
    [InlineData("line", false)]
    [InlineData("line", true)]
    [InlineData("branch", false)]
    [InlineData("branch", true)]
    public void Covered_method_detail_cannot_inflate_assembly_or_critical_coverage(
        string metric,
        bool useNamespace
    )
    {
        // Arrange
        var secondHit = metric == "line"
            ? 0
            : 1;

        var canonicalBranches = metric == "branch"
            ? "50% (1/2)"
            : "100% (2/2)";

        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Critical.cs\">"
            + "<methods><method><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"100% (2/2)\" />"
            + "<line number=\"2\" hits=\"1\" branch=\"true\" condition-coverage=\"100% (2/2)\" />"
            + "</lines></method></methods><lines>"
            + $"<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"{canonicalBranches}\" />"
            + $"<line number=\"2\" hits=\"{secondHit}\" />"
            + "</lines></class>",
            criticalMinimumBranchPercent: 75,
            minimumLinePercent: 75,
            assemblyMinimumBranchPercent: 75,
            useNamespace: useNamespace);

        // Act
        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        // Assert
        Assert.Collection(
            evaluation.Errors,
            error => Assert.StartsWith($"assembly Provider {metric} coverage", error, StringComparison.Ordinal),
            error => Assert.StartsWith(
                $"critical class Provider.Critical {metric} coverage",
                error,
                StringComparison.Ordinal));

        Assert.All(
            evaluation.Results,
            result => Assert.Contains(
                metric == "line"
                    ? "lines 1/2"
                    : "branches 1/2",
                result,
                StringComparison.Ordinal));
    }

    /// <summary>Accepts complete canonical coverage despite uncovered method-detail rows.</summary>
    /// <param name="metric">The metric whose redundant method detail is uncovered.</param>
    /// <param name="useNamespace">Whether the Cobertura elements use a default XML namespace.</param>
    [Theory]
    [InlineData("line", false)]
    [InlineData("line", true)]
    [InlineData("branch", false)]
    [InlineData("branch", true)]
    public void Uncovered_method_detail_cannot_deflate_assembly_or_critical_coverage(
        string metric,
        bool useNamespace
    )
    {
        // Arrange
        var methodHit = metric == "line"
            ? 0
            : 1;

        var methodBranches = metric == "branch"
            ? " branch=\"true\" condition-coverage=\"0% (0/2)\""
            : string.Empty;

        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Critical.cs\">"
            + "<methods><method><lines>"
            + $"<line number=\"1\" hits=\"{methodHit}\"{methodBranches} />"
            + $"<line number=\"2\" hits=\"{methodHit}\"{methodBranches} />"
            + "</lines></method></methods><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"100% (2/2)\" />"
            + "<line number=\"2\" hits=\"1\" />"
            + "</lines></class>",
            criticalMinimumBranchPercent: 100,
            assemblyMinimumBranchPercent: 100,
            useNamespace: useNamespace);

        // Act
        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        // Assert
        Assert.Empty(evaluation.Errors);
        Assert.All(evaluation.Results, result => Assert.Contains("lines 2/2", result, StringComparison.Ordinal));
        Assert.All(evaluation.Results, result => Assert.Contains("branches 2/2", result, StringComparison.Ordinal));
    }

    /// <summary>Rejects method-only evidence when canonical class lines are missing or empty.</summary>
    /// <param name="emptyLines">Whether an empty canonical lines container is present.</param>
    /// <param name="useNamespace">Whether the Cobertura elements use a default XML namespace.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Method_detail_cannot_replace_missing_canonical_class_lines(
        bool emptyLines,
        bool useNamespace
    )
    {
        // Arrange
        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Critical.cs\">"
            + "<methods><method><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"100% (2/2)\" />"
            + "</lines></method></methods>"
            + (emptyLines
                ? "<lines />"
                : string.Empty)
            + "</class>",
            useNamespace: useNamespace);

        // Act
        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        // Assert
        Assert.Collection(
            evaluation.Errors,
            error => Assert.Equal("assembly Provider has no instrumented lines.", error),
            error => Assert.Equal("assembly Provider has no instrumented branches.", error),
            error => Assert.Equal("critical class Provider.Critical has no instrumented lines.", error),
            error => Assert.Equal("critical class Provider.Critical has no instrumented branches.", error));
    }

    /// <summary>Retains distinct class identities that share a physical file and line number.</summary>
    [Fact]
    public void Distinct_classes_on_one_physical_line_retain_their_canonical_evidence()
    {
        // Arrange
        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Shared.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"100% (2/2)\" />"
            + "</lines></class>"
            + "<class name=\"Provider.Helper\" filename=\"Shared.cs\"><lines>"
            + "<line number=\"1\" hits=\"0\" branch=\"true\" condition-coverage=\"0% (0/2)\" />"
            + "</lines></class>",
            minimumLinePercent: 75,
            assemblyMinimumBranchPercent: 75);

        // Act
        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        // Assert
        Assert.Collection(
            evaluation.Errors,
            error => Assert.StartsWith("assembly Provider line coverage", error, StringComparison.Ordinal),
            error => Assert.StartsWith("assembly Provider branch coverage", error, StringComparison.Ordinal));

        Assert.Contains("lines 1/2", evaluation.Results[0], StringComparison.Ordinal);
        Assert.Contains("branches 2/4", evaluation.Results[0], StringComparison.Ordinal);
        Assert.Contains("lines 1/1", evaluation.Results[1], StringComparison.Ordinal);
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

        public void Dispose() => Directory.Delete(Path, recursive: true);

        public static CoverageFixture Create(
            string assemblyName = "Provider",
            string className = "Provider.Critical",
            (int First, int Second)? lineHits = null,
            string branchFraction = "50% (1/2)",
            string? classElements = null,
            double? criticalMinimumBranchPercent = 50,
            double minimumLinePercent = 100,
            double assemblyMinimumBranchPercent = 50,
            bool useNamespace = false
        )
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"doka-coverage-contract-{Guid.NewGuid():N}");

            Directory.CreateDirectory(path);
            var fixture = new CoverageFixture(path);
            var (firstHits, secondHits) = lineHits ?? (1, 1);
            classElements ??= $"<class name=\"{className}\"><lines>"
                + $"<line number=\"1\" hits=\"{firstHits}\" branch=\"true\" "
                + $"condition-coverage=\"{branchFraction}\" />"
                + $"<line number=\"2\" hits=\"{secondHits}\" />"
                + "</lines></class>";

            File.WriteAllText(
                fixture.ReportPath,
                (useNamespace
                    ? "<coverage xmlns=\"urn:coverage\" timestamp=\"1000\">"
                    : "<coverage timestamp=\"1000\">")
                + "<packages>"
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
                                minimumLinePercent,
                                minimumBranchPercent = assemblyMinimumBranchPercent,
                            },
                        },
                        criticalClasses = new[]
                        {
                            new
                            {
                                assembly = "Provider",
                                name = "Provider.Critical",
                                minimumLinePercent,
                                minimumBranchPercent = criticalMinimumBranchPercent,
                            },
                        },
                    }));

            return fixture;
        }
    }
}
