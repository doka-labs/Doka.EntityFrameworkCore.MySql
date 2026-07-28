namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Marks an inherited EF Core specification fact as unsupported on explicitly named
/// database targets because the engine cannot express the required relational operation.
/// The disposition ID links the executable skip to its primary-source-backed ledger entry.
/// </summary>
/// <remarks>
/// Setting <c>DOKA_SPEC_TEST_PROBE_ENGINE_LIMITS=true</c> disables the skip so an engine
/// upgrade can be checked without editing test source.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpecEngineLimitationFactAttribute : FactAttribute
{
    private const string ProbeEnvironmentVariable = "DOKA_SPEC_TEST_PROBE_ENGINE_LIMITS";

    /// <summary>
    /// Creates an engine-limited fact disposition for the supplied test targets.
    /// </summary>
    /// <param name="dispositionId">
    /// Stable identifier of the corresponding machine-readable disposition.
    /// </param>
    /// <param name="unsupportedTargets">
    /// Exact <c>DOKA_SPEC_TEST_TARGET</c> values on which discovery must skip the fact.
    /// </param>
    public SpecEngineLimitationFactAttribute(
        string dispositionId,
        params string[] unsupportedTargets
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispositionId);
        ArgumentNullException.ThrowIfNull(unsupportedTargets);

        DispositionId = dispositionId;
        UnsupportedTargets = unsupportedTargets;

        var target = SpecTestTarget.Resolve();
        if (unsupportedTargets.Contains(target, StringComparer.OrdinalIgnoreCase)
            && !string.Equals(
                Environment.GetEnvironmentVariable(ProbeEnvironmentVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip =
                $"[spec-engine-limit:{dispositionId}] Target '{target}' is covered by "
                + "the primary-source-backed specification disposition ledger.";
        }
    }

    /// <summary>
    /// Gets the stable identifier used to reconcile source annotations with the ledger.
    /// </summary>
    public string DispositionId { get; }

    /// <summary>
    /// Gets the database targets for which the documented engine limitation applies.
    /// </summary>
    public IReadOnlyList<string> UnsupportedTargets { get; }
}
