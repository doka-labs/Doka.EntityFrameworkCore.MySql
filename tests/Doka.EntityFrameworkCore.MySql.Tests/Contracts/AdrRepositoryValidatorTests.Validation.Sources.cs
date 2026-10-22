namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>
    /// Verifies undated external source is rejected.
    /// </summary>
    [Fact]
    public void Undated_external_source_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- [Vendor documentation](https://example.com/reference)");

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Invalid source entry", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies invalid source retrieval date is rejected.
    /// </summary>
    [Fact]
    public void Invalid_source_retrieval_date_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- [Vendor documentation](https://example.com/reference) "
            + "(primary source; retrieved 2026-13-40)");

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("invalid retrieval date", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies wrapped external sources and reference links are accepted.
    /// </summary>
    [Fact]
    public void Wrapped_external_sources_and_reference_links_are_accepted()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source:
            """
            - [Vendor documentation](https://example.com/reference)
              (primary source; retrieved 2026-07-27)
            - [Vendor reference documentation][vendor-reference]
              (primary source; retrieved 2026-07-27)

            [vendor-reference]:
              https://example.com/reference-link
            """);

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.True(report.IsValid, FormatErrors(report));
    }

    /// <summary>
    /// Verifies undefined source reference is rejected.
    /// </summary>
    [Fact]
    public void Undefined_source_reference_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source:
            """
            - [Vendor documentation][missing-reference]
              (primary source; retrieved 2026-07-27)
            """);

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("undefined link", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies non primary source declaration is rejected.
    /// </summary>
    [Fact]
    public void Non_primary_source_declaration_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- [Third-party summary](https://example.com/reference) "
            + "(secondary source; retrieved 2026-07-27)");

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Invalid source entry", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies repository only marker cannot be mixed with external sources.
    /// </summary>
    [Fact]
    public void Repository_only_marker_cannot_be_mixed_with_external_sources()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- No external sources; repository evidence only.\n"
            + "- [Vendor documentation](https://example.com/reference) "
            + "(primary source; retrieved 2026-07-27)");

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains(
                "cannot be combined with external sources",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies dated primary source passes syntactic validation.
    /// </summary>
    [Fact]
    public void Dated_primary_source_passes_syntactic_validation()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- [Vendor documentation](https://example.com/reference) "
            + "(primary source; retrieved 2026-07-27)");

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.True(report.IsValid, FormatErrors(report));
    }
}
