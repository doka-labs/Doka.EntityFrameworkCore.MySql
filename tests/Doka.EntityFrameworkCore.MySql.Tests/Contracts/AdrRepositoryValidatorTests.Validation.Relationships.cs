namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>
    /// Verifies asymmetric relationship is rejected.
    /// </summary>
    [Fact]
    public void Asymmetric_relationship_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteDecision(id: "D-002", slug: "second-decision", title: "Second decision", amends: ["D-001"]);

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("not bidirectional", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies self relationship is rejected.
    /// </summary>
    [Fact]
    public void Self_relationship_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision", amends: ["D-001"]);

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("cannot reference the same ADR", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies relationship to missing decision is rejected.
    /// </summary>
    [Fact]
    public void Relationship_to_missing_decision_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision", amends: ["D-002"]);

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("references missing ADR", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies superseded by relationship requires superseded status.
    /// </summary>
    [Fact]
    public void Superseded_by_relationship_requires_superseded_status()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision", supersededBy: ["D-002"]);
        repository.WriteDecision(id: "D-002", slug: "second-decision", title: "Second decision", supersedes: ["D-001"]);

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("must use status 'superseded'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies superseded status requires successor.
    /// </summary>
    [Fact]
    public void Superseded_status_requires_successor()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content
                .Replace("status: implemented", "status: superseded", StringComparison.Ordinal)
                .Replace("status implemented.", "status superseded.", StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("must identify its successor", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies valid bidirectional supersession passes.
    /// </summary>
    [Fact]
    public void Valid_bidirectional_supersession_passes()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            supersededBy: ["D-002"],
            transform: content => content
                .Replace("status: implemented", "status: superseded", StringComparison.Ordinal)
                .Replace("status implemented.", "status superseded.", StringComparison.Ordinal));
        repository.WriteDecision(id: "D-002", slug: "second-decision", title: "Second decision", supersedes: ["D-001"]);

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.True(report.IsValid, FormatErrors(report));
    }
}
