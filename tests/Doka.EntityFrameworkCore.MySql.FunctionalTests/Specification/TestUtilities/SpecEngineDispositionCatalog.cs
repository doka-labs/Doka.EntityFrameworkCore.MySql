namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Resolves engine-limitation targets from the copied disposition ledger.
/// </summary>
/// <remarks>
/// The ledger carries the primary sources, executable probe observations, and
/// exact target set for a disposition. Reading that set here keeps discovery
/// and evidence under one owner when an LTS line enters or leaves support.
/// </remarks>
internal static class SpecEngineDispositionCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> s_targets = new(
        LoadTargets,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Returns the supported targets affected by an engine limitation.
    /// </summary>
    public static IReadOnlyList<string> GetTargets(
        string dispositionId,
        IReadOnlyCollection<string> annotatedTargets
    )
    {
        if (!s_targets.Value.TryGetValue(dispositionId, out var targets))
        {
            throw new InvalidOperationException(
                $"Engine disposition '{dispositionId}' is absent from SpecDispositions.json.");
        }

        var unknownAnnotatedTargets = annotatedTargets
            .Except(targets, StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();

        if (unknownAnnotatedTargets.Length > 0)
        {
            throw new InvalidOperationException(
                $"Engine disposition '{dispositionId}' no longer covers annotated target(s): "
                + $"{string.Join(", ", unknownAnnotatedTargets)}.");
        }

        return targets;
    }

    private static Dictionary<string, string[]> LoadTargets()
    {
        var ledgerPath = Path.Combine(AppContext.BaseDirectory, "Specification", "SpecDispositions.json");

        using var document = JsonDocument.Parse(File.ReadAllText(ledgerPath));

        return document
            .RootElement.GetProperty("activeDispositions")
            .EnumerateArray()
            .Where(disposition => string.Equals(
                disposition
                    .GetProperty("classification")
                    .GetString(),
                "engine-limitation",
                StringComparison.Ordinal))
            .ToDictionary(
                disposition => disposition
                    .GetProperty("id")
                    .GetString()!,
                disposition => disposition
                    .GetProperty("targets")
                    .EnumerateArray()
                    .Select(target => target.GetString()!)
                    .ToArray(),
                StringComparer.Ordinal);
    }
}
