namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>Runs benchmark measurements and evaluates retained raw performance evidence.</summary>
public static class Program
{
    private const int InvalidEvidenceExitCode = 78;

    /// <summary>Executes measurements, soak collection, or the selected evidence evaluation mode.</summary>
    /// <param name="args">BenchmarkDotNet arguments or the explicit evaluation/soak command.</param>
    /// <returns>Zero on success, one for a regression, or 78 for invalid evidence or execution.</returns>
    public static async Task<int> Main(
        string[] args
    )
    {
        try
        {
            if (args.Length is 5 or 6
                && string.Equals(args[0], "--evaluate", StringComparison.Ordinal))
            {
                return PerformanceGate.Run(
                    args[1],
                    args[2],
                    args[3],
                    args[4],
                    args.Length == 6 ? args[5] : null,
                    PerformanceEvaluationPolicy.Current);
            }

            if (TryReadOutputArgument(args, "--soak", out var soakOutput))
            {
                return await PerformanceSoakRunner
                    .RunAsync(soakOutput)
                    .ConfigureAwait(false);
            }

            var summaries = BenchmarkSwitcher
                .FromAssembly(typeof(Program).Assembly)
                .Run(args, BenchmarkConfiguration.Create())
                .ToArray();

            if (summaries.Length == 0)
            {
                await Console.Error.WriteLineAsync("BenchmarkDotNet did not execute any benchmark.");
                return InvalidEvidenceExitCode;
            }

            var failed = summaries.Any(summary =>
                summary.HasCriticalValidationErrors
                || HasFailedBenchmarkReports(summary.Reports));

            return failed ? InvalidEvidenceExitCode : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return InvalidEvidenceExitCode;
        }
    }

    internal static bool HasFailedBenchmarkReports(
        IEnumerable<BenchmarkReport> reports
    ) => reports.Any(static report => !report.Success);

    private static bool TryReadOutputArgument(
        string[] args,
        string option,
        out string outputPath
    )
    {
        outputPath = string.Empty;

        if (args.Length == 0
            || !string.Equals(args[0], option, StringComparison.Ordinal))
        {
            return false;
        }

        if (args.Length != 2
            || string.IsNullOrWhiteSpace(args[1]))
        {
            throw new ArgumentException($"{option} requires exactly one output path.");
        }

        outputPath = Path.GetFullPath(args[1]);
        return true;
    }

}
