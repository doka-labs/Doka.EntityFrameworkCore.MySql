namespace Doka.EntityFrameworkCore.MySql.CoverageGate;

internal static class Program
{
    internal static int Main(
        string[] args
    )
    {
        try
        {
            if (args is ["freshness", var policyPath, var reportPath, .. var additionalReports])
            {
                var errors = CoverageContract.EvaluateFreshness(
                    new[] { reportPath }.Concat(additionalReports),
                    policyPath);
                foreach (var error in errors)
                {
                    Console.Error.WriteLine(error);
                }

                if (errors.Count > 0)
                {
                    Console.Error.WriteLine("Coverage input freshness policy not met.");
                    return 1;
                }

                Console.WriteLine($"Coverage input freshness met for {additionalReports.Length + 1} report(s).");
                return 0;
            }

            if (args is [var mergedReportPath, var mergedPolicyPath])
            {
                var evaluation = CoverageContract.Evaluate(mergedReportPath, mergedPolicyPath);
                foreach (var result in evaluation.Results)
                {
                    Console.WriteLine(result);
                }

                foreach (var error in evaluation.Errors)
                {
                    Console.Error.WriteLine(error);
                }

                if (evaluation.Errors.Count > 0)
                {
                    Console.Error.WriteLine("Coverage policy not met.");
                    return 1;
                }

                Console.WriteLine("Coverage policy met.");
                return 0;
            }

            WriteUsage();
            return 2;
        }
        catch (Exception exception) when (exception is IOException
                                              or InvalidDataException
                                              or InvalidOperationException
                                              or JsonException
                                              or FormatException
                                              or OverflowException)
        {
            Console.Error.WriteLine($"Coverage contract is malformed: {exception.Message}");
            return 2;
        }
    }

    private static void WriteUsage() => Console.Error.WriteLine(
        "Usage: CoverageGate <merged-cobertura.xml> <coverage-policy.json>\n"
        + "       CoverageGate freshness <coverage-policy.json> <report> [report...]");
}
