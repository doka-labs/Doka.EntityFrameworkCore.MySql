namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public static class Program
{
    public static async Task<int> Main(
        string[] args
    )
    {
        try
        {
            if (TryReadOutputArgument(args, "--workloads", out var workloadOutput))
            {
                return await PerformanceWorkloadRunner
                    .RunAsync(workloadOutput)
                    .ConfigureAwait(false);
            }

            if (TryReadOutputArgument(args, "--soak", out var soakOutput))
            {
                return await PerformanceSoakRunner
                    .RunAsync(soakOutput)
                    .ConfigureAwait(false);
            }

            if (args.Length == 1
                && string.Equals(args[0], "--list-workloads", StringComparison.Ordinal))
            {
                PerformanceWorkloadRunner.WriteApplicableWorkloadIds(Console.Out);
                return 0;
            }

            var summaries = BenchmarkSwitcher
                .FromAssembly(typeof(Program).Assembly)
                .Run(args, BenchmarkConfiguration.Create())
                .ToArray();

            if (summaries.Length == 0)
            {
                await Console.Error.WriteLineAsync("BenchmarkDotNet did not execute any benchmark.");
                return 1;
            }

            var failed = summaries.Any(summary =>
                summary.HasCriticalValidationErrors || summary.Reports.Any(report => !report.Success));

            return failed ? 1 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

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
