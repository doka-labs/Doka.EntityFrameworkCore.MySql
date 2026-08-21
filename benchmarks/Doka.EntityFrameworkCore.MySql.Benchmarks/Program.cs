namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public static class Program
{
    private const int InvalidEvidenceExitCode = 78;

    public static async Task<int> Main(
        string[] args
    )
    {
        try
        {
            if (TryReadWorkloadDiagnosticArguments(
                    args,
                    out var workloadId,
                    out var workloadDiagnosticOutput))
            {
                return await PerformanceWorkloadRunner
                    .RunDiagnosticAsync(workloadDiagnosticOutput, workloadId)
                    .ConfigureAwait(false);
            }

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
                return InvalidEvidenceExitCode;
            }

            var failed = summaries.Any(summary =>
                summary.HasCriticalValidationErrors || summary.Reports.Any(report => !report.Success));

            return failed ? InvalidEvidenceExitCode : 0;
        }
        catch (MeasurementQualityException exception)
        {
            // A measurement condition leaves through its own exit code so the
            // attempt path records `measurement-inconclusive` and may retry.
            // Exit 1 would classify as a regression, which is a verdict about
            // the provider that this run never reached.
            Console.Error.WriteLine(exception.Message);

            return MeasurementQualityException.ExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return InvalidEvidenceExitCode;
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

    private static bool TryReadWorkloadDiagnosticArguments(
        string[] args,
        out string workloadId,
        out string outputPath
    )
    {
        workloadId = string.Empty;
        outputPath = string.Empty;

        if (args.Length == 0
            || !string.Equals(args[0], "--workload", StringComparison.Ordinal))
        {
            return false;
        }

        if (args.Length != 3
            || string.IsNullOrWhiteSpace(args[1])
            || string.IsNullOrWhiteSpace(args[2]))
        {
            throw new ArgumentException(
                "--workload requires exactly one workload ID and one output path.");
        }

        workloadId = args[1];
        outputPath = Path.GetFullPath(args[2]);
        return true;
    }
}
