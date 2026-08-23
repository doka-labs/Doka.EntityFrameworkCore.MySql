namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static class Program
{
    internal static int Main(
        string[] args
    )
    {
        try
        {
            var root = ParseRepositoryRoot(args);
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

    private static void WriteUsage() => Console.WriteLine("Usage: RepositoryContract [--root <repository>]");
}
