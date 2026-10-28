namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class DependencySecurityAuditTests
{
    /// <summary>Retains portable evidence and failure diagnostics while removing NuGet's disposable HTTP cache.</summary>
    /// <param name="failure">The audit boundary that fails, or an empty string for a clean audit.</param>
    /// <param name="expectedExitCode">The process result that must survive evidence generation and cleanup.</param>
    /// <param name="diagnostic">The original failure detail that must remain in the uploaded evidence.</param>
    [Theory]
    [InlineData("", 0, "")]
    [InlineData("restore", 1, "NU1900: advisory source unavailable")]
    [InlineData("command", 1, "package audit command failed")]
    [InlineData("vulnerable", 1, "vulnerabilities")]
    public void Runner_preserves_portable_evidence_when_nuget_cache_names_are_not_uploadable(
        string failure,
        int expectedExitCode,
        string diagnostic
    )
    {
        // Arrange
        using var fixture = new AuditFixture();

        // Act
        var result = fixture.Execute(new Dictionary<string, string> { ["DOKA_FIXTURE_FAILURE"] = failure });

        // Assert
        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Equal(expectedExitCode == 0 ? "passed" : "failed", fixture.Summary.GetProperty("result").GetString());
        var paths = Directory.GetFileSystemEntries(fixture.RunPath, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(paths);
        // WHY: upload-artifact applies this portable filename contract even on Unix,
        // where NuGet's real colon-containing cache entries are otherwise valid.
        Assert.All(paths, path => Assert.DoesNotContain(
            Path.GetRelativePath(fixture.RunPath, path),
            character => character is '"' or ':' or '<' or '>' or '|' or '*' or '?' or '\r' or '\n'));

        Assert.True(File.Exists(Path.Combine(fixture.RunPath, "context.json")));
        Assert.True(File.Exists(Path.Combine(fixture.RunPath, "inventory.txt")));
        Assert.True(File.Exists(Path.Combine(fixture.RunPath, "sdk.log")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.TemporaryPath));
        if (diagnostic.Length != 0)
        {
            Assert.Contains(Directory.GetFiles(fixture.RunPath, "*", SearchOption.AllDirectories),
                path => File.ReadAllText(path).Contains(diagnostic, StringComparison.Ordinal));
        }
    }

    /// <summary>An unavailable temporary root cannot produce a successful audit or erase setup diagnostics.</summary>
    [Fact]
    public void Runner_retains_diagnostics_when_http_cache_creation_fails()
    {
        // Arrange
        using var fixture = new AuditFixture();
        var unavailableRoot = Path.Combine(fixture.Root, "missing-parent", "temporary");

        // Act
        var result = fixture.Execute(new Dictionary<string, string> { ["TMPDIR"] = unavailableRoot });

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("failed", fixture.Summary.GetProperty("result").GetString());
        Assert.Equal(0, fixture.Summary.GetProperty("projectCount").GetInt32());
        Assert.NotEmpty(File.ReadAllText(Path.Combine(fixture.RunPath, "http-cache.log")));
        Assert.True(File.Exists(Path.Combine(fixture.RunPath, "sdk.log")));
    }

    /// <summary>A caller-selected temporary root cannot put disposable HTTP data back into artifact evidence.</summary>
    /// <param name="useSymlink">Whether the temporary root aliases evidence through a directory symlink.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runner_rejects_a_temporary_cache_root_inside_evidence(
        bool useSymlink
    )
    {
        // Arrange
        using var fixture = new AuditFixture();
        var temporaryRoot = Path.Combine(fixture.Root, "evidence");
        Directory.CreateDirectory(temporaryRoot);
        if (useSymlink)
        {
            var link = Path.Combine(fixture.Root, "evidence-link");
            Directory.CreateSymbolicLink(link, temporaryRoot);
            temporaryRoot = link;
        }

        // Act
        var result = fixture.Execute(new Dictionary<string, string>
        {
            ["TMPDIR"] = temporaryRoot,
        });

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("failed", fixture.Summary.GetProperty("result").GetString());
        Assert.Contains("outside audit evidence",
            File.ReadAllText(Path.Combine(fixture.RunPath, "http-cache.log")), StringComparison.Ordinal);
        Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, "evidence")));
    }

    /// <summary>Cleanup errors fail the process while retaining the original project outcomes and diagnostics.</summary>
    /// <param name="failure">An optional independent project failure preceding cleanup.</param>
    [Theory]
    [InlineData("")]
    [InlineData("restore")]
    public void Runner_preserves_audit_results_when_owned_cache_cleanup_fails(
        string failure
    )
    {
        // Arrange
        using var fixture = new AuditFixture();
        fixture.RejectCacheCleanup();

        // Act
        var result = fixture.Execute(new Dictionary<string, string> { ["DOKA_FIXTURE_FAILURE"] = failure });

        // Assert
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("failed", fixture.Summary.GetProperty("result").GetString());
        Assert.Equal(5, fixture.Summary.GetProperty("projectCount").GetInt32());
        Assert.Equal(failure.Length == 0 ? 5 : 4, fixture.Summary.GetProperty("passedCount").GetInt32());
        Assert.Equal(failure.Length == 0 ? 0 : 1, fixture.Summary.GetProperty("failedCount").GetInt32());
        Assert.Contains("Simulated cache cleanup failure.",
            File.ReadAllText(Path.Combine(fixture.RunPath, "http-cache-cleanup.log")), StringComparison.Ordinal);
        if (failure.Length != 0)
        {
            Assert.Contains(Directory.GetFiles(fixture.RunPath, "restore.log", SearchOption.AllDirectories),
                path => File.ReadAllText(path).Contains("NU1900: advisory source unavailable", StringComparison.Ordinal));
        }
    }
}
