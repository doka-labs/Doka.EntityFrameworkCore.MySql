namespace Doka.EntityFrameworkCore.MySql.SpecificationContract;

/// <summary>
/// Creates the deterministic, assembly-derived inventory of unresolved provider contracts.
/// </summary>
internal static class SpecificationDebtReport
{
    internal const int SchemaVersion = 1;

    /// <summary>
    /// Creates a stable report only from a valid specification contract result.
    /// </summary>
    internal static SpecificationDebtDocument Create(
        SpecificationContractReport report
    )
    {
        if (!report.IsValid)
        {
            throw new InvalidOperationException(
                "Provider debt cannot be reported from an invalid specification contract.");
        }

        var currentProviderGaps = report
            .CurrentProviderGaps.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        if (currentProviderGaps.Length != report.CurrentProviderGapCount)
        {
            throw new InvalidDataException(
                $"Provider debt count is {report.CurrentProviderGapCount}, "
                + $"but {currentProviderGaps.Length} unique contract IDs were supplied.");
        }

        return new SpecificationDebtDocument(
            SchemaVersion,
            report.EfCoreVersion,
            report.InitialProviderGapCount,
            report.CurrentProviderGapCount,
            currentProviderGaps);
    }

    /// <summary>
    /// Writes the current provider-debt report using the shared deterministic JSON contract.
    /// </summary>
    internal static void Write(
        string path,
        SpecificationContractReport report
    ) => ContractJson.Write(path, Create(report));
}

/// <summary>
/// Captures every unresolved provider contract for one exact EF Core patch boundary.
/// </summary>
internal sealed record SpecificationDebtDocument(
    int SchemaVersion,
    string EfCoreVersion,
    int InitialProviderGapCount,
    int CurrentProviderGapCount,
    IReadOnlyList<string> CurrentProviderGaps
);
