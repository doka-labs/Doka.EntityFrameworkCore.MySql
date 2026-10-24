namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class DependencySecurityAuditTests
{
    /// <summary>Provides independent malformed or incomplete reports that cannot prove a clean audit.</summary>
    public static IEnumerable<object[]> InvalidAuditDocuments()
    {
        yield return ["boolean schema", "version", true];
        yield return ["missing source", "sources", null!];
        yield return ["empty sources", "sources", Array.Empty<string>()];
        yield return ["non-string source", "sources", new object[] { 42 }];
        yield return ["empty projects", "projects", Array.Empty<object>()];
        yield return ["non-array projects", "projects", new { path = ProjectPath }];
        yield return ["non-string project path", "projects", new[] { new { path = 42 } }];
        yield return
        [
            "duplicate projects", "projects", new[] { new { path = ProjectPath }, new { path = ProjectPath } },
        ];
        yield return ["warning result", "problems", new[] { new { level = "warning", text = "Missing database" } }];
        yield return ["error result", "problems", new[] { new { level = "error", text = "Source unavailable" } }];
        yield return ["malformed problems", "problems", "unavailable"];
        yield return ["malformed framework", "projects", new[] { new { path = ProjectPath, frameworks = "invalid" } }];
    }

    /// <summary>Rejects invalid evidence independently of the CLI's successful process exit.</summary>
    /// <param name="scenario">The independent schema or completeness violation.</param>
    /// <param name="property">The audit property to replace.</param>
    /// <param name="value">The malformed or incomplete replacement.</param>
    [Theory]
    [MemberData(nameof(InvalidAuditDocuments))]
    public void Validator_rejects_incomplete_or_malformed_audits(
        string scenario,
        string property,
        object? value
    )
    {
        // Arrange
        var document = CleanAudit();
        document[property] = value;

        // Act
        var result = RunValidator(document);

        // Assert
        Assert.True(result.ExitCode != 0, $"Accepted {scenario}: {result.StandardOutput}");
    }

    /// <summary>Preserves the legitimate NuGet shape which omits frameworks when no packages are vulnerable.</summary>
    [Fact]
    public void Validator_accepts_clean_audit_without_framework_nodes()
    {
        // Arrange
        var document = CleanAudit();
        document["problems"] = Array.Empty<object>();

        // Act
        var result = RunValidator(document);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0", result.StandardOutput.Trim());
    }

    /// <summary>Rejects a clean document that belongs to another project.</summary>
    [Fact]
    public void Validator_rejects_a_different_project_identity()
    {
        // Arrange
        var document = CleanAudit("/repo/src/Other/Other.csproj");

        // Act
        var result = RunValidator(document);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("does not cover", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>Every severity and dependency depth blocks the audit.</summary>
    /// <param name="collection">Whether the vulnerable package is direct or transitive.</param>
    /// <param name="severity">The vulnerability severity reported by NuGet.</param>
    [Theory]
    [InlineData("topLevelPackages", "Low")]
    [InlineData("topLevelPackages", "Moderate")]
    [InlineData("transitivePackages", "High")]
    [InlineData("transitivePackages", "Critical")]
    public void Validator_rejects_all_reported_vulnerability_severities(
        string collection,
        string severity
    )
    {
        // Arrange
        var document = CleanAudit();
        document["projects"] = new[]
        {
            new
            {
                path = ProjectPath,
                frameworks = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["framework"] = "net10.0",
                        [collection] = new[]
                        {
                            new
                            {
                                id = "Example.Package",
                                resolvedVersion = "1.0.0",
                                vulnerabilities = new[] { new { severity, advisoryurl = "https://example.invalid/a" } },
                            },
                        },
                    },
                },
            },
        };

        // Act
        var result = RunValidator(document);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("vulnerable package entries", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>Malformed vulnerability fields cannot disappear from the finding counter.</summary>
    /// <param name="vulnerabilities">The invalid report representation.</param>
    [Theory]
    [InlineData("ignored")]
    [InlineData(null)]
    public void Validator_rejects_malformed_vulnerability_collections(
        string? vulnerabilities
    )
    {
        // Arrange
        var document = CleanAudit();
        document["projects"] = new[]
        {
            new
            {
                path = ProjectPath,
                frameworks = new[]
                {
                    new
                    {
                        framework = "net10.0",
                        transitivePackages = new[]
                        {
                            new { id = "Example", resolvedVersion = "1.0.0", vulnerabilities },
                        },
                    },
                },
            },
        };

        // Act
        var result = RunValidator(document);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("malformed", result.StandardError, StringComparison.Ordinal);
    }
}
