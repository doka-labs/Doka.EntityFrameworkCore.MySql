namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed class CoverageContractTests
{
    [Fact]
    public void Coverage_contract_accepts_fresh_evidence_at_every_floor()
    {
        using var fixture = CoverageFixture.Create();

        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        Assert.Empty(evaluation.Errors);
        Assert.Collection(
            evaluation.Results,
            result => Assert.Contains("assembly Provider", result, StringComparison.Ordinal),
            result => Assert.Contains("critical class Provider.Critical", result, StringComparison.Ordinal));
    }

    [Fact]
    public void Coverage_contract_rejects_stale_missing_and_below_floor_evidence()
    {
        using var fixture = CoverageFixture.Create(
            assemblyName: "Other.Assembly",
            className: "Other.Critical",
            lineHits: (1, 0),
            branchFraction: "0% (0/2)");

        var stale = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 2000);
        var invalid = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        Assert.Contains(stale.Errors, error => error.Contains("old", StringComparison.Ordinal));
        Assert.Contains(invalid.Errors, error => error.Contains("missing shipped assembly", StringComparison.Ordinal));
    }

    [Fact]
    public void Coverage_contract_rejects_stale_raw_input_before_merge()
    {
        using var fixture = CoverageFixture.Create();

        var errors = CoverageContract.EvaluateFreshness([fixture.ReportPath], fixture.PolicyPath, nowTimestamp: 2000);

        Assert.Single(errors);
        Assert.Contains("old", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_contract_aggregates_distinct_partial_class_sources()
    {
        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Critical.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"50% (1/2)\" />"
            + "</lines></class>"
            + "<class name=\"Provider.Critical\" filename=\"Critical.Partial.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" />"
            + "</lines></class>");

        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        Assert.Empty(evaluation.Errors);
        Assert.Contains("lines 2/2", evaluation.Results[1], StringComparison.Ordinal);
        Assert.Contains("branches 1/2", evaluation.Results[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_contract_rejects_ambiguous_partial_class_sources()
    {
        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Critical.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"50% (1/2)\" />"
            + "</lines></class>"
            + "<class name=\"Provider.Critical\" filename=\"Critical.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" />"
            + "</lines></class>");

        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        Assert.Contains(
            evaluation.Errors,
            error => error.Contains("ambiguous source fragments", StringComparison.Ordinal));
    }

    [Fact]
    public void Coverage_contract_accepts_an_explicitly_branch_free_surface()
    {
        using var fixture = CoverageFixture.Create(
            classElements: "<class name=\"Provider.Critical\" filename=\"Critical.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" />"
            + "<line number=\"2\" hits=\"1\" />"
            + "</lines></class>"
            + "<class name=\"Provider.Helper\" filename=\"Helper.cs\"><lines>"
            + "<line number=\"1\" hits=\"1\" branch=\"true\" condition-coverage=\"100% (2/2)\" />"
            + "</lines></class>",
            criticalMinimumBranchPercent: null);

        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

        Assert.Empty(evaluation.Errors);
        Assert.Contains("branches 0/0", evaluation.Results[1], StringComparison.Ordinal);
        Assert.Contains("minimum N/A", evaluation.Results[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "declares no branch floor")]
    [InlineData(0d, "greater than zero")]
    public void Coverage_contract_rejects_missing_or_zero_branch_floors(
        double? branchFloor,
        string expectedError
    )
    {
        using var fixture = CoverageFixture.Create(criticalMinimumBranchPercent: branchFloor);

        var evaluation = CoverageContract.Evaluate(fixture.ReportPath, fixture.PolicyPath, nowTimestamp: 1001);

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
