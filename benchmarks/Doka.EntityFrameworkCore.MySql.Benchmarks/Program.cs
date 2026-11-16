namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>Runs benchmark measurements and evaluates retained raw performance evidence.</summary>
public static class Program
{
    private const int InvalidEvidenceExitCode = 78;

    /// <summary>Executes benchmarks, isolated allocation probes, soak collection, or evidence evaluation.</summary>
    /// <param name="args">BenchmarkDotNet arguments or an explicit probe/evaluation/soak command.</param>
    /// <returns>Zero on success, one for a regression, or 78 for invalid evidence or execution.</returns>
    public static async Task<int> Main(
        string[] args
    )
    {
        try
        {
            if (args.Length > 0
                && string.Equals(args[0], "--allocation-probe", StringComparison.Ordinal))
            {
                if (args.Length != 2)
                {
                    throw new ArgumentException("--allocation-probe requires exactly one scenario.");
                }

                Console.WriteLine(JsonSerializer.Serialize(JsonAllocationProbe.Run(args[1])));

                return 0;
            }

            if (args.Length is 5 or 6
                && string.Equals(args[0], "--evaluate", StringComparison.Ordinal))
            {
                return PerformanceGate.Run(
                    args[1],
                    args[2],
                    args[3],
                    args[4],
                    args.Length == 6
                        ? args[5]
                        : null,
                    PerformanceEvaluationPolicy.Current);
            }

            if (TryReadOutputArgument(args, "--soak", out var soakOutput))
            {
                // WHY: The standalone command-line entry point has no caller cancellation token.
                return await PerformanceSoakRunner
                    .RunAsync(soakOutput, CancellationToken.None)
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

            return failed
                ? InvalidEvidenceExitCode
                : 0;
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
