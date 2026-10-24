namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>Runs release-boundary regressions before merge and before expensive qualification.</summary>
    [Theory]
    [InlineData("ci.yml", "repo-tests")]
    [InlineData("release-candidate.yml", "preflight")]
    public void Release_tool_contracts_run_before_provider_and_candidate_work(
        string workflowName,
        string jobName
    )
    {
        // Arrange
        var path = Path.Combine(FindRepositoryRoot(), ".github", "workflows", workflowName);

        // Act
        var job = GetWorkflowJob(File.ReadAllText(path), jobName);

        // Assert
        Assert.Contains("bash ./eng/release/test-tools.sh", job, StringComparison.Ordinal);
    }

    /// <summary>Does not mistake an identically named dispatch input for a producer job.</summary>
    [Fact]
    public void Workflow_job_selection_ignores_dispatch_input_names()
    {
        // Arrange
        const string workflow = "name: release\non:\n  workflow_dispatch:\n    inputs:\n"
            + "      publish:\n        type: boolean\njobs:\n  publish:\n    runs-on: ubuntu-24.04\n"
            + "  next:\n    runs-on: ubuntu-24.04\n";

        // Act
        var publisher = GetWorkflowJob(workflow, "publish");

        // Assert
        Assert.Equal("  publish:\n    runs-on: ubuntu-24.04", publisher);
    }

    /// <summary>Prevents qualification-only runs from requesting a publisher environment.</summary>
    [Fact]
    public void Release_publishers_require_current_authority_and_retain_the_existing_environment()
    {
        // Arrange
        var path = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "release-candidate.yml");

        // Act
        var workflow = File.ReadAllText(path);
        var publisher = GetWorkflowJob(workflow, "publish");
        var preflight = GetWorkflowJob(workflow, "preflight");

        // Assert
        Assert.Contains("needs.preflight.outputs.publish_allowed == 'true'", publisher, StringComparison.Ordinal);
        Assert.Contains("name: nuget", publisher, StringComparison.Ordinal);
        Assert.DoesNotContain("publisher_environment", workflow, StringComparison.Ordinal);
        Assert.Contains("python3 -m eng.release.publication preflight", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("    environment:", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", preflight, StringComparison.Ordinal);
    }

    /// <summary>Serializes release-line publishers without cancelling an active publication.</summary>
    [Fact]
    public void Release_publishers_share_one_noncancelling_publication_queue()
    {
        // Arrange
        var path = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "release-candidate.yml");

        // Act
        var job = GetWorkflowJob(File.ReadAllText(path), "publish");

        // Assert
        Assert.Contains("group: doka-publication", job, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", job, StringComparison.Ordinal);
        Assert.Contains("queue: max", job, StringComparison.Ordinal);
        Assert.Contains("DOKA_PUBLICATION_LOCK_GROUP: doka-publication", job, StringComparison.Ordinal);
        Assert.Contains("DOKA_PUBLICATION_LOCK_HELD: \"1\"", job, StringComparison.Ordinal);
    }

    /// <summary>Keeps qualification outside the shared publisher lock and protected environment.</summary>
    [Theory]
    [InlineData("preflight")]
    [InlineData("foundation")]
    [InlineData("engine-contracts")]
    [InlineData("sbom")]
    [InlineData("assemble")]
    [InlineData("attest")]
    public void Qualification_does_not_acquire_publication_authority(string jobName)
    {
        // Arrange
        var path = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "release-candidate.yml");

        // Act
        var qualification = GetWorkflowJob(File.ReadAllText(path), jobName);

        // Assert
        Assert.DoesNotContain("concurrency:", qualification, StringComparison.Ordinal);
        Assert.DoesNotContain("\n    environment:", qualification, StringComparison.Ordinal);
    }

    /// <summary>Freezes admission before the first write and before any NuGet credential request.</summary>
    [Fact]
    public void Shared_publisher_persists_admission_before_remote_mutations()
    {
        // Arrange
        var path = Path.Combine(FindRepositoryRoot(), ".github", "actions", "publish-release", "action.yml");

        // Act
        var publisher = File.ReadAllText(path);
        var admission = publisher.IndexOf("python3 -m eng.release.publication admit", StringComparison.Ordinal);
        var durableAdmission = publisher.IndexOf("name: publication-admission-attempt-", StringComparison.Ordinal);
        var binding = publisher.IndexOf("python3 -m eng.release.nuget bind", StringComparison.Ordinal);
        var draft = publisher.IndexOf("python3 -m eng.release.github stage", StringComparison.Ordinal);
        var login = publisher.IndexOf("uses: NuGet/login@", StringComparison.Ordinal);

        // Assert
        Assert.True(admission >= 0 && durableAdmission > admission);
        Assert.True(binding > durableAdmission && draft > binding && login > draft);
        Assert.Contains("retention-days: 30", publisher[durableAdmission..binding], StringComparison.Ordinal);
        Assert.Contains("authorization-observation.json", publisher, StringComparison.Ordinal);
        Assert.Contains("current-authorization-observation.json", publisher, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.NUGET_API_KEY", publisher, StringComparison.Ordinal);
    }

    /// <summary>Independently binds source authorization and the attestation signer.</summary>
    [Fact]
    public void Shared_publisher_verifies_the_qualifier_signer_not_its_caller_filename()
    {
        // Arrange
        var path = Path.Combine(FindRepositoryRoot(), ".github", "actions", "publish-release", "action.yml");

        // Act
        var publisher = File.ReadAllText(path);

        // Assert
        Assert.Contains("using: composite", publisher, StringComparison.Ordinal);
        Assert.Contains(
            "--signer-workflow \"${GITHUB_REPOSITORY}/.github/workflows/release-candidate.yml\"",
            publisher,
            StringComparison.Ordinal);
        Assert.Contains("--signer-digest \"${GITHUB_SHA}\"", publisher, StringComparison.Ordinal);
        Assert.Contains("--source-ref \"${GITHUB_REF}\"", publisher, StringComparison.Ordinal);
        Assert.Contains("--source-digest \"${GITHUB_SHA}\"", publisher, StringComparison.Ordinal);
    }
}
