namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed class RepositoryContractTests
{
    [Fact]
    public void Repository_contract_passes()
    {
        var report = RepositoryContractValidator.Validate(FindRepositoryRoot());

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(17, report.ExampleCount);
        Assert.True(report.MarkdownDocumentCount > 0);
        Assert.True(report.LocalLinkCount > 0);
    }

    [Fact]
    public void Documentation_contract_rejects_missing_targets_and_ignores_fences()
    {
        using var directory = new TemporaryDirectory();
        var docs = Path.Combine(directory.Path, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "guide.md"), "# Guide\n");
        File.WriteAllText(
            Path.Combine(directory.Path, "README.md"),
            "# Project\n\n"
            + "[Missing](docs/missing.md)\n"
            + "[Anchor](docs/guide.md#missing)\n\n"
            + "```markdown\n[Illustrative](ignored.md)\n```\n");

        var report = DocumentationContract.ValidateLocalLinks(directory.Path);

        Assert.Equal(2, report.LinkCount);
        Assert.Collection(
            report.Errors,
            error => Assert.Contains("Target file does not exist", error.Message, StringComparison.Ordinal),
            error => Assert.Contains("Anchor '#missing' does not exist", error.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void Image_pin_contract_rejects_a_mirror_with_another_digest()
    {
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

        var errors = ImagePinContract.Validate(directory.Path);

        Assert.Contains(errors, error => error.Message.Contains("docker/compose.yml pins", StringComparison.Ordinal));
    }

    [Fact]
    public void Commit_message_gate_accepts_the_contract_and_rejects_an_invalid_subject()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var messagePath = Path.Combine(directory.Path, "COMMIT_EDITMSG");
        File.WriteAllText(
            messagePath,
            "fix(provider): preserve exact tree trust\n\n"
            + "- Main qualification must reuse only identical PR content.\n\n"
            + "- Bind the merged commit to its qualified PR tree.\n"
            + "- Reject missing, ambiguous, or changed tree evidence.\n");

        var accepted = RunCommitMessageGate(repositoryRoot, messagePath);
        File.WriteAllText(messagePath, "invalid subject\n");
        var rejected = RunCommitMessageGate(repositoryRoot, messagePath);

        Assert.Equal(0, accepted.ExitCode);
        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("Commit message rejected", rejected.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Engineering_contract_rejects_Python_quality_code_and_general_test_discovery()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "eng/performance"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "eng/quality"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "eng/testing"));
        File.WriteAllText(Path.Combine(directory.Path, "eng/quality/policy.py"), "pass\n");
        File.WriteAllText(Path.Combine(directory.Path, "eng/testing/test.sh"), "python3 -m unittest discover\n");

        var errors = EngineeringSurfaceContract.ValidatePythonBoundary(directory.Path);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Contains("eng/quality", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("general provider test path", StringComparison.Ordinal));
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
