namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>Accepts the last history date while preserving the original recording date.</summary>
    [Theory]
    [InlineData("2026-07-27")]
    [InlineData("2026-07-28")]
    public void Metadata_date_tracks_the_latest_history_entry(
        string updatedDate
    )
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content
                .Replace("date: 2026-07-27", $"date: {updatedDate}", StringComparison.Ordinal)
                .Replace(
                    "- 2026-07-27: Decision recorded with status implemented.",
                    "- 2026-07-27: Decision recorded with status implemented.\n"
                    + $"- {updatedDate}: Updated the decision rationale.",
                    StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.True(report.IsValid, FormatErrors(report));
        Assert.Equal(updatedDate,
            Assert
                .Single(report.Documents)
                .Date
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>Rejects stale metadata and history that runs backwards.</summary>
    [Theory]
    [InlineData("2026-07-27", "2026-07-28", "must match the last Decision History entry")]
    [InlineData("2026-07-26", "2026-07-26", "must be chronological")]
    public void Invalid_history_date_binding_is_rejected(
        string metadataDate,
        string historyDate,
        string expectedDiagnostic
    )
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content
                .Replace("date: 2026-07-27", $"date: {metadataDate}", StringComparison.Ordinal)
                .Replace(
                    "- 2026-07-27: Decision recorded with status implemented.",
                    "- 2026-07-27: Decision recorded with status implemented.\n"
                    + $"- {historyDate}: Updated the decision rationale.",
                    StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(report.Errors, error => error.Message.Contains(expectedDiagnostic, StringComparison.Ordinal));
    }

    /// <summary>Rejects missing, expanded, or unrelated ADR notification groups.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("[Provider contributors]")]
    [InlineData("[\"@doka-labs/core-maintainers\", \"another-group\"]")]
    public void Informed_metadata_requires_the_exact_maintainer_group(
        string informed
    )
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "informed: [\"@doka-labs/core-maintainers\"]",
                $"informed: {informed}",
                StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(report.Errors,
            static error => error.Message.Contains("informed must contain exactly", StringComparison.Ordinal));
    }
}
