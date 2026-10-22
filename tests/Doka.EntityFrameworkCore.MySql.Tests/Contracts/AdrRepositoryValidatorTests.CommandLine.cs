namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>
    /// Verifies command line write index validates and generates artifacts.
    /// </summary>
    [Fact]
    public void Command_line_write_index_validates_and_generates_artifacts()
    {
        // Arrange
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");

        // Act
        var exitCode = Program.Main(
        [
            "--root",
            repository.Root,
            "--write-index",
        ]);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(repository.Root, AdrIndexRenderer.ReadmeRelativePath)));
        Assert.True(File.Exists(Path.Combine(repository.Root, AdrIndexRenderer.JsonRelativePath)));
    }

    /// <summary>
    /// Verifies command line returns validation failure for invalid repository.
    /// </summary>
    [Fact]
    public void Command_line_returns_validation_failure_for_invalid_repository()
    {
        // Arrange
        using var repository = TestRepository.Create();

        // Act
        var exitCode = Program.Main(
        [
            "--root",
            repository.Root
        ]);

        // Assert
        Assert.Equal(1, exitCode);
    }

    /// <summary>
    /// Verifies command line help succeeds.
    /// </summary>
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Command_line_help_succeeds(
        string option
    )
    {
        // Arrange: The theory supplies option.

        // Act
        var exitCode = Program.Main([option]);

        // Assert
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// Verifies command line usage errors return two.
    /// </summary>
    [Theory]
    [InlineData("--unknown")]
    [InlineData("--root")]
    public void Command_line_usage_errors_return_two(
        string option
    )
    {
        // Arrange: The theory supplies option.

        // Act
        var exitCode = Program.Main([option]);

        // Assert
        Assert.Equal(2, exitCode);
    }
}
