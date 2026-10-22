namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlObservabilityContractTests
{
    /// <summary>Returns each operation and metric tag domain as an independently reported case.</summary>
    public static IEnumerable<object[]> MetricTagDomainCases
    {
        get
        {
            using var contract = LoadContract();

            foreach (var operation in contract.RootElement.GetProperty("operations").EnumerateArray())
            {
                yield return new object[]
                {
                    OperationId(operation),
                    MySqlDiagnosticTags.Engine,
                    new[] { MySqlDiagnosticTags.MariaDb, MySqlDiagnosticTags.MySql },
                };
            }

            yield return new object[]
            {
                "server-version-resolution", "support_status", Enum.GetNames<MySqlServerVersionSupportStatus>(),
            };
            yield return new object[]
            {
                "server-version-resolution", "compatibility_mode", Enum.GetNames<MySqlServerVersionCompatibilityMode>(),
            };
            yield return new object[]
            {
                "migration-lock-acquire", "outcome",
                new[] { MySqlDiagnosticTags.Acquired, MySqlDiagnosticTags.Failed, MySqlDiagnosticTags.Timeout },
            };
            yield return new object[]
            {
                "retry-attempt", "outcome", new[] { MySqlDiagnosticTags.Attempt },
            };
            yield return new object[]
            {
                "command-cancellation", "path", new[] { MySqlDiagnosticTags.Hard, MySqlDiagnosticTags.Soft },
            };
        }
    }
}
