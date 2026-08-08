namespace Doka.EntityFrameworkCore.MySql.AdrValidator;

internal static class Program
{
    internal static int Main(
        string[] args
    )
    {
        try
        {
            var options = ParseOptions(args);
            if (options.ShowHelp)
            {
                WriteUsage();
                return 0;
            }

            var report = AdrRepositoryValidator.Validate(
                options.RepositoryRoot,
                validateGeneratedArtifacts: !options.WriteIndex);

            if (!report.IsValid)
            {
                WriteErrors(report.Errors);
                return 1;
            }

            if (options.WriteIndex)
            {
                AdrIndexRenderer.WriteGeneratedArtifacts(options.RepositoryRoot, report.Documents);
                report = AdrRepositoryValidator.Validate(options.RepositoryRoot);
                if (!report.IsValid)
                {
                    WriteErrors(report.Errors);
                    return 1;
                }
            }

            Console.WriteLine($"Validated {report.Documents.Count} ADRs against MADR 4.0.0 and Doka profile 1.0.");
            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            WriteUsage();
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ADR validation failed unexpectedly: {exception.Message}");
            return 2;
        }
    }

    private static Options ParseOptions(
        string[] args
    )
    {
        var repositoryRoot = Directory.GetCurrentDirectory();
        var writeIndex = false;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--root":
                    if (++index >= args.Length)
                    {
                        throw new ArgumentException("--root requires a path.", nameof(args));
                    }

                    repositoryRoot = Path.GetFullPath(args[index]);
                    break;
                case "--write-index":
                    writeIndex = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[index]}'.", nameof(args));
            }
        }

        return new Options(repositoryRoot, writeIndex, showHelp);
    }

    private static void WriteErrors(
        IReadOnlyList<AdrValidationError> errors
    )
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine(error);
        }

        Console.Error.WriteLine($"ADR validation failed with {errors.Count} error(s).");
    }

    private static void WriteUsage() => Console.WriteLine("Usage: AdrValidator [--root <repository>] [--write-index]");

    private sealed record Options(
        string RepositoryRoot,
        bool WriteIndex,
        bool ShowHelp
    );
}
