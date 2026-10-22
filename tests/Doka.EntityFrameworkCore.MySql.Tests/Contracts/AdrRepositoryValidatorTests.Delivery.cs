namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>
    /// Verifies every delivery path invokes the same validator.
    /// </summary>
    [Theory]
    [InlineData("eng/testing/test.sh", "dotnet build")]
    [InlineData("eng/release/release-candidate.sh", "run_specification_gate")]
    [InlineData("eng/quality/quality-gates.sh", "dotnet format")]
    public void Every_delivery_path_invokes_the_same_validator(
        string relativePath,
        string followingCommand
    )
    {
        // Arrange
        var path = Path.Combine(FindRepositoryRoot(), relativePath);

        // Act
        var script = File.ReadAllText(path);

        // Assert
        AssertShellGate(script, followingCommand, path);
    }

    /// <summary>
    /// Keeps model drift, bundle lifecycle coverage, and retained evidence wired
    /// into both exhaustive CI and release-candidate paths.
    /// </summary>
    [Fact]
    public void Migration_deployment_gate_is_wired_into_ci_and_release_candidates()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var modelGate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "quality", "check-migration-model.sh"));

        var deploymentGate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "test-migration-deployment.sh"));

        var bundleBuilder = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "build-migration-bundle.sh"));

        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release", "release-candidate.sh"));

        var qualityGates = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "quality", "quality-gates.sh"));

        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        // Assert
        Assert.Contains("migrations has-pending-model-changes", modelGate, StringComparison.Ordinal);
        Assert.Contains("migrations script", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("--idempotent", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("migrate-direct", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("database update", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("execute_script", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("assert_failed_script_terminates_session", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("DokaFailedScopedScriptContinuation", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("MigrationWorkflowHandlerEvidence", deploymentGate, StringComparison.Ordinal);
        Assert.Contains(
            "bash \"${repo_root}/eng/testing/build-migration-bundle.sh\" \"${bundle_path}\"",
            deploymentGate,
            StringComparison.Ordinal);
        Assert.Contains("migrations bundle", bundleBuilder, StringComparison.Ordinal);
        Assert.Contains("DokaIsolatedNuGetLockRoot", bundleBuilder, StringComparison.Ordinal);
        Assert.Contains(
            "run_bundle_command \"${connection_string}\" \"${server_version}\" 0",
            deploymentGate,
            StringComparison.Ordinal);
        Assert.Contains("\"mysql84\"", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("\"mariadb114\"", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("\"mariadb118\"", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("run_migration_deployment_gate", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_MIGRATION_DEPLOYMENT_EVIDENCE_ROOT=\"${migration_deployment_root}\"",
            releaseCandidate,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"${repo_root}/eng/quality/check-migration-model.sh\"",
            qualityGates,
            StringComparison.Ordinal);
        Assert.Contains("run: bash eng/quality-gates.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("run: bash eng/test-migration-deployment.sh", workflow, StringComparison.Ordinal);
    }
}
