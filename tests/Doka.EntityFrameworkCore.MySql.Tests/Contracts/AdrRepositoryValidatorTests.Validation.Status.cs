namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>
    /// Verifies illegal status transition is rejected.
    /// </summary>
    [Fact]
    public void Illegal_status_transition_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "- 2026-07-27: Decision recorded with status implemented.",
                "- 2026-07-27: Decision recorded with status proposed.\n"
                + "- 2026-07-27: Status changed from proposed to deprecated.",
                StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("is not allowed", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies malformed status change is rejected.
    /// </summary>
    [Fact]
    public void Malformed_status_change_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content
                .Replace("status: implemented", "status: accepted", StringComparison.Ordinal)
                .Replace(
                    "- 2026-07-27: Decision recorded with status implemented.",
                    "- 2026-07-27: Decision recorded with status proposed.\n"
                    + "- 2026-07-27: Status changed from proposed into accepted.",
                    StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Status changes must use", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies valid status transition chain passes.
    /// </summary>
    [Fact]
    public void Valid_status_transition_chain_passes()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "- 2026-07-27: Decision recorded with status implemented.",
                "- 2026-07-27: Decision recorded with status proposed.\n"
                + "- 2026-07-27: Status changed from proposed to accepted.\n"
                + "- 2026-07-27: Status changed from accepted to implemented.",
                StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.True(report.IsValid, FormatErrors(report));
    }
}
