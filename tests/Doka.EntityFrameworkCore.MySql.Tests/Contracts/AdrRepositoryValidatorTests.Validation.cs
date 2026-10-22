namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>
    /// Verifies valid repository and generated artifacts pass.
    /// </summary>
    [Fact]
    public void Valid_repository_and_generated_artifacts_pass()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision", amendedBy: ["D-002"]);
        repository.WriteDecision(id: "D-002", slug: "second-decision", title: "Second decision", amends: ["D-001"]);
        repository.WriteGeneratedArtifacts();

        // Act
        var report = repository.Validate();

        // Assert
        Assert.True(report.IsValid, FormatErrors(report));
        Assert.Equal(2, report.Documents.Count);
    }

    /// <summary>
    /// Verifies unknown or reordered metadata is rejected.
    /// </summary>
    [Fact]
    public void Unknown_or_reordered_metadata_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "id: D-001\n",
                "id: D-001\nunknown: value\n",
                StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Metadata keys must appear exactly", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies empty confirmation is rejected.
    /// </summary>
    [Fact]
    public void Empty_confirmation_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "### Confirmation\n\n- Run `eng/validate-adrs.sh`.\n",
                "### Confirmation\n\n",
                StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Confirmation' must not be empty", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies missing symmetric option tradeoff is rejected.
    /// </summary>
    [Fact]
    public void Missing_symmetric_option_tradeoff_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "- Bad, because the chosen option requires maintenance.\n\n" + "### Rejected option",
                "### Rejected option",
                StringComparison.Ordinal));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains(
                "Option 'Chosen option' must contain a '- Bad, because",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies decision contract violation is rejected.
    /// </summary>
    [Theory]
    [InlineData("Deterministic validation", "Deterministic validati\u00F6n", "Non-ASCII byte")]
    [InlineData("---\nid:", "id:", "must begin with YAML front matter")]
    [InlineData(
        "doka-profile-version: \"1.0\"\n---\n\n#",
        "doka-profile-version: \"1.0\"\n\n#",
        "front matter has no closing delimiter")]
    [InlineData("status: implemented", "status implemented", "Metadata must use one flat")]
    [InlineData("id: D-001", "id: D-001\nid: D-001", "Duplicate metadata key")]
    [InlineData("# D-001 -- First decision", "# First decision", "title must use")]
    [InlineData("## Decision Drivers", "## Drivers", "Missing required heading")]
    [InlineData(
        "## Context and Problem Statement",
        "## Context and Problem Statement\n\n" + "Duplicate context.\n\n" + "## Context and Problem Statement",
        "must appear exactly once")]
    [InlineData(
        "This fixture exercises the repository validator.",
        "### Original record metadata\n\n" + "This fixture exercises the repository validator.",
        "Original record metadata")]
    [InlineData(
        "This fixture exercises the repository validator.",
        "This fixture exercises the repository validator.\n\n" + "- **Status:** Implemented",
        "must not repeat the front matter 'status' key")]
    [InlineData(
        "This fixture exercises the repository validator.",
        "This fixture exercises the repository validator.\n\n" + "- **Date:** 2026-07-27",
        "must not repeat the front matter 'date' key")]
    [InlineData(
        "This fixture exercises the repository validator.",
        "This fixture exercises the repository validator.\n\n" + "- **Scope:** Test scope",
        "must not repeat the front matter 'scope' key")]
    [InlineData(
        "This fixture exercises the repository validator.\n\n" + "### Re-evaluation Triggers",
        "This fixture exercises the repository validator.\n" + "### Re-evaluation Triggers",
        "headings must be preceded by a blank line")]
    [InlineData("- Deterministic validation\n- Reviewable trade-offs", "", "Decision Drivers' must not be empty")]
    [InlineData("- Rejected option\n", "", "at least two options")]
    [InlineData("- Rejected option\n", "- Chosen option\n", "option titles must be unique")]
    [InlineData(
        "Chosen option: \"Chosen option\", because",
        "Selected option: \"Chosen option\", because",
        "Decision Outcome must contain")]
    [InlineData(
        "Chosen option: \"Chosen option\", because",
        "Chosen option: \"Missing option\", because",
        "must exactly match")]
    [InlineData(
        "- Good, because governance drift fails deterministically.\n",
        "",
        "Consequences must contain a '- Good, because")]
    [InlineData(
        "- Run `eng/validate-adrs.sh`.",
        "Inspect the result manually.",
        "Confirmation must contain at least one bullet")]
    [InlineData("### Rejected option", "#### Rejected option", "Missing trade-off section")]
    [InlineData(
        "- 2026-07-27: Decision recorded with status implemented.",
        "Decision recorded with status implemented.",
        "Decision History must contain at least one dated entry")]
    [InlineData(
        "- 2026-07-27: Decision recorded with status implemented.",
        "- 2026-13-40: Decision recorded with status implemented.",
        "Decision History entry has an invalid date")]
    [InlineData(
        "Decision recorded with status implemented.",
        "Decision captured with status implemented.",
        "Decision History must begin")]
    [InlineData(
        "- `eng/validate-adrs.sh`",
        "`eng/validate-adrs.sh`",
        "Implementation References must contain at least one bullet")]
    [InlineData(
        "The repository needs a deterministic decision contract.",
        "See https://example.com before deciding.",
        "External URLs must be consolidated")]
    [InlineData("- No external sources; repository evidence only.", "", "Sources must contain repository evidence")]
    [InlineData("status: implemented", "status: unknown", "Unsupported status")]
    [InlineData("date: 2026-07-27", "date: 2026-13-40", "Date must use YYYY-MM-DD")]
    [InlineData(
        "decision-makers: [Doka maintainers]",
        "decision-makers: []",
        "Metadata list 'decision-makers' must not be empty")]
    [InlineData("consulted: []", "consulted: Reviewer", "must use inline YAML list syntax")]
    [InlineData("consulted: []", "consulted: [Reviewer, ]", "contains an empty entry")]
    [InlineData("consulted: []", "consulted: [Reviewer, Reviewer]", "contains duplicate entries")]
    [InlineData("scope: \"Test scope\"", "scope: \"\"", "Scope must not be empty")]
    [InlineData("madr-version: \"4.0.0\"", "madr-version: \"3.0.0\"", "madr-version must be pinned")]
    [InlineData(
        "doka-profile-version: \"1.0\"",
        "doka-profile-version: \"2.0\"",
        "doka-profile-version must be pinned")]
    [InlineData("id: D-001", "id: D-002", "identifiers must match")]
    public void Decision_contract_violation_is_rejected(
        string original,
        string replacement,
        string expectedError
    )
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content =>
            {
                // WHY: A template drift must fail the fixture rather than silently skip its invalid mutation.
                if (!content.Contains(original, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The ADR fixture does not contain its mutation target.");
                }

                return content.Replace(original, replacement, StringComparison.Ordinal);
            });

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(report.Errors, error => error.Message.Contains(expectedError, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies out of order required heading is rejected.
    /// </summary>
    [Fact]
    public void Out_of_order_required_heading_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content =>
            {
                var contextStart = content.IndexOf("## Context and Problem Statement", StringComparison.Ordinal);
                var driversStart = content.IndexOf("## Decision Drivers", StringComparison.Ordinal);
                var optionsStart = content.IndexOf("## Considered Options", StringComparison.Ordinal);

                if (contextStart < 0 || driversStart <= contextStart || optionsStart <= driversStart)
                {
                    throw new InvalidOperationException("The ADR fixture does not have its expected heading order.");
                }

                var context = content[contextStart..driversStart];
                var drivers = content[driversStart..optionsStart];

                return content[..contextStart] + drivers + context + content[optionsStart..];
            });

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("out of canonical order", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies invalid filename is rejected.
    /// </summary>
    [Fact]
    public void Invalid_filename_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "Uppercase-slug", title: "First decision");

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("filenames must use a lowercase", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies missing decision directory is rejected.
    /// </summary>
    [Fact]
    public void Missing_decision_directory_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        Directory.Delete(Path.Combine(repository.Root, "docs", "decisions"));

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Decision directory does not exist", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies empty decision directory is rejected.
    /// </summary>
    [Fact]
    public void Empty_decision_directory_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("No ADR files were found", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies duplicate identifier is rejected.
    /// </summary>
    [Fact]
    public void Duplicate_identifier_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteDecision(id: "D-001", slug: "duplicate-decision", title: "Duplicate decision");

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Duplicate ADR identifier", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies identifier gap is rejected.
    /// </summary>
    [Fact]
    public void Identifier_gap_is_rejected()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteDecision(id: "D-003", slug: "third-decision", title: "Third decision");

        // Act
        var report = repository.Validate(validateGeneratedArtifacts: false);

        // Assert
        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("identifiers must be contiguous", StringComparison.Ordinal));
    }
}
