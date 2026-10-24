namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static class Program
{
    internal static int Main(
        string[] args
    )
    {
        try
        {
            if (args.Length >= 2 && args[0] == "--actionlint-snapshot")
            {
                var snapshotRoot = ParseRepositoryRoot(args[2..]);
                var errors = EngineeringSurfaceContract.PrepareActionlintSnapshot(snapshotRoot, args[1]);
                foreach (var error in errors)
                {
                    Console.Error.WriteLine(error);
                }

                Console.WriteLine($"Actionlint compatibility snapshot completed with {errors.Count} error(s).");

                return errors.Count == 0 ? 0 : 1;
            }

            var concurrencyOnly = args.Length > 0 && args[0] == "--workflow-concurrency";
            var root = ParseRepositoryRoot(concurrencyOnly ? args[1..] : args);
            if (concurrencyOnly)
            {
                var errors = EngineeringSurfaceContract.ValidateWorkflowConcurrencyFiles(root);
                foreach (var error in errors)
                {
                    Console.Error.WriteLine(error);
                }

                Console.WriteLine($"Workflow concurrency contract completed with {errors.Count} error(s).");

                return errors.Count == 0 ? 0 : 1;
            }

            var report = RepositoryContractValidator.Validate(root);
            if (!report.IsValid)
            {
                foreach (var error in report.Errors)
                {
                    Console.Error.WriteLine(error);
                }

                Console.Error.WriteLine($"Repository contract failed with {report.Errors.Count} error(s).");
                return 1;
            }

            Console.WriteLine(
                $"Validated {report.LocalLinkCount} local links across "
                + $"{report.MarkdownDocumentCount} Markdown documents, "
                + $"{report.ExampleCount} examples, and all database image pins.");
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
            Console.Error.WriteLine($"Repository contract failed unexpectedly: {exception.Message}");
            return 2;
        }
    }

    private static string ParseRepositoryRoot(
        string[] args
    )
    {
        if (args.Length == 0)
        {
            return Directory.GetCurrentDirectory();
        }

        if (args is ["--root", var repositoryRoot])
        {
            return Path.GetFullPath(repositoryRoot);
        }

        throw new ArgumentException("Repository validation accepts only --root <path>.", nameof(args));
    }

    private static void WriteUsage() => Console.WriteLine(
        "Usage: RepositoryContract [--workflow-concurrency | --actionlint-snapshot <directory>] [--root <repository>]");
}
