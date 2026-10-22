namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>
    /// Verifies stale generated index is rejected.
    /// </summary>
    [Fact]
    public void Stale_generated_index_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteGeneratedArtifacts();
        File.AppendAllText(Path.Combine(repository.Root, AdrIndexRenderer.JsonRelativePath), "stale");

        // Act
        var report = repository.Validate();

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Generated decision artifact is stale", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies missing generated artifacts are rejected.
    /// </summary>
    [Fact]
    public void Missing_generated_artifacts_are_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");

        // Act
        var report = repository.Validate();
        var actual = report.Errors.Count(static error => error.Message.Contains(
                "Generated decision artifact is missing",
                StringComparison.Ordinal));

        // Assert
        Assert.Equal(
            2,
            actual);
    }

    /// <summary>
    /// Verifies stale generated relationship graph is rejected.
    /// </summary>
    [Fact]
    public void Stale_generated_relationship_graph_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteGeneratedArtifacts();
        File.AppendAllText(Path.Combine(repository.Root, AdrIndexRenderer.ReadmeRelativePath), "stale");

        // Act
        var report = repository.Validate();

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Generated decision artifact is stale", StringComparison.Ordinal));
    }
}
