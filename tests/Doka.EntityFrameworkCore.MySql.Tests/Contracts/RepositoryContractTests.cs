namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies repository contracts fail precisely at their public engineering boundaries.</summary>
public sealed class RepositoryContractTests
{
    /// <summary>
    /// Verifies repository contract passes.
    /// </summary>
    [Fact]
    public void Repository_contract_passes()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var report = RepositoryContractValidator.Validate(repositoryRoot);

        // Assert
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(17, report.ExampleCount);
        Assert.True(report.MarkdownDocumentCount > 0);
        Assert.True(report.LocalLinkCount > 0);
    }

    /// <summary>
    /// Verifies documentation links reject a missing file or anchor independently.
    /// </summary>
    [Theory]
    [InlineData("docs/missing.md", "Target file does not exist")]
    [InlineData("docs/guide.md#missing", "Anchor '#missing' does not exist")]
    public void Documentation_contract_rejects_missing_targets(
        string target,
        string expectedError
    )
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var docs = Path.Combine(directory.Path, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "guide.md"), "# Guide\n");
        File.WriteAllText(
            Path.Combine(directory.Path, "README.md"),
            $"# Project\n\n[Missing]({target})\n");

        // Act
        var report = DocumentationContract.ValidateLocalLinks(directory.Path);

        // Assert
        Assert.Equal(1, report.LinkCount);
        Assert.Contains(expectedError, Assert.Single(report.Errors).Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies illustrative links inside fenced code do not become documentation dependencies.</summary>
    [Fact]
    public void Documentation_contract_ignores_fenced_examples()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "README.md"),
            "# Project\n\n```markdown\n[Illustrative](ignored.md)\n```\n");

        // Act
        var report = DocumentationContract.ValidateLocalLinks(directory.Path);

        // Assert
        Assert.Equal(0, report.LinkCount);
        Assert.Empty(report.Errors);
    }

    /// <summary>
    /// Verifies image pin contract rejects a mirror with another digest.
    /// </summary>
    [Fact]
    public void Image_pin_contract_rejects_a_mirror_with_another_digest()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        using var directory = new TemporaryDirectory();
        foreach (var relativePath in new[]
                 {
                     "docker/compose.yml", ".github/workflows/ci.yml", "benchmarks/performance-contract.json",
                     "tests/Doka.EntityFrameworkCore.MySql.TestUtilities/TestDatabaseImages.cs",
                 })
        {
            var destination = Path.Combine(directory.Path, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(repositoryRoot, relativePath), destination);
        }
        var workflowPath = Path.Combine(directory.Path, ".github/workflows/ci.yml");
        var workflow = File.ReadAllText(workflowPath);
        var pin = Regex.Match(workflow, "mysql:8\\.4[^\\s]+@sha256:[0-9a-f]{64}", RegexOptions.CultureInvariant)
            .Value;

        var replacement = pin[..^1] + (pin[^1] == '0' ? '1' : '0');
        File.WriteAllText(workflowPath, workflow.Replace(pin, replacement, StringComparison.Ordinal));

        // Act
        var errors = ImagePinContract.Validate(directory.Path);

        // Assert
        Assert.Contains(errors, error => error.Message.Contains("docker/compose.yml pins", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies commit message gate accepts the contract and rejects an invalid subject.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Commit_message_gate_accepts_only_the_repository_contract(
        bool valid
    )
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var messagePath = Path.Combine(directory.Path, "COMMIT_EDITMSG");
        File.WriteAllText(
            messagePath,
            valid ? "fix(provider): preserve exact tree trust\n\n"
            + "- Main qualification must reuse only identical PR content.\n\n"
            + "- Bind the merged commit to its qualified PR tree.\n"
            + "- Reject missing, ambiguous, or changed tree evidence.\n" : "invalid subject\n");

        // Act
        var result = RunCommitMessageGate(repositoryRoot, messagePath);

        // Assert
        Assert.Equal(valid ? 0 : 1, result.ExitCode);

        if (!valid)
        {
            Assert.Contains("Commit message rejected", result.StandardError, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies engineering contract rejects python quality code and general test discovery.
    /// </summary>
    [Theory]
    [InlineData("eng/quality/policy.py", "pass\n", "eng/quality")]
    [InlineData("eng/testing/test.sh", "python3 -m unittest discover\n", "general provider test path")]
    public void Engineering_contract_rejects_Python_quality_code_and_general_test_discovery(
        string relativePath,
        string content,
        string expectedError
    )
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "eng/performance"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "eng/quality"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "eng/testing"));
        // WHY: Every isolated violation still needs the test entrypoint that this contract always inspects.
        File.WriteAllText(Path.Combine(directory.Path, "eng/testing/test.sh"), "#!/usr/bin/env bash\nset -euo pipefail\n");
        File.WriteAllText(Path.Combine(directory.Path, relativePath), content);

        // Act
        var errors = EngineeringSurfaceContract.ValidatePythonBoundary(directory.Path);

        // Assert
        Assert.Contains(expectedError, Assert.Single(errors), StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static (int ExitCode, string StandardError) RunCommitMessageGate(
        string repositoryRoot,
        string messagePath
    )
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("bash")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "eng/quality/commit-message.sh"));
        startInfo.ArgumentList.Add(messagePath);

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Commit message gate did not start.");

        process.WaitForExit();

        return (process.ExitCode, process.StandardError.ReadToEnd());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"doka-repository-contract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
