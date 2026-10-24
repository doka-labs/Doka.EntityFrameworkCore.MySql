namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies branch-local dependency audits cannot turn incomplete package evidence into success.
/// </summary>
public sealed partial class DependencySecurityAuditTests
{
    private const string ProjectPath = "/repo/src/Provider/Provider.csproj";
    private const string AuditSource = "https://api.nuget.org/v3/index.json";

    private static Dictionary<string, object?> CleanAudit(
        string projectPath = ProjectPath
    ) => new(StringComparer.Ordinal)
    {
        ["version"] = 1,
        ["parameters"] = "--vulnerable --include-transitive",
        ["sources"] = new[] { AuditSource },
        ["projects"] = new[] { new Dictionary<string, object?> { ["path"] = projectPath } },
    };

    private static ProcessResult RunValidator(
        Dictionary<string, object?> document,
        string expectedProject = ProjectPath
    )
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "audit.json");
        File.WriteAllText(path, JsonSerializer.Serialize(document));

        return Run(
            FindRepositoryRoot(),
            Path.Combine(FindRepositoryRoot(), "eng/quality/check-vulnerability-audit.sh"),
            new Dictionary<string, string>(),
            path,
            expectedProject);
    }

    private static ProcessResult Run(
        string workingDirectory,
        string script,
        IReadOnlyDictionary<string, string> environment,
        params string[] arguments
    )
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("bash")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{script}'.");

        // WHY: A failed restore can fill stderr while stdout is still being read.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        // WHY: The contract fixture is synchronous; both pipes are already
        // drained concurrently, so this wait cannot fill an unread error pipe.
        // ReSharper disable once MethodHasAsyncOverload
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class TestDirectory : IDisposable
    {
        /// <summary>Creates one isolated fixture root with no shared cache state.</summary>
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"doka-security-audit-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        /// <summary>Gets the exact directory owned by this fixture.</summary>
        public string Path { get; }

        /// <inheritdoc />
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );
}
