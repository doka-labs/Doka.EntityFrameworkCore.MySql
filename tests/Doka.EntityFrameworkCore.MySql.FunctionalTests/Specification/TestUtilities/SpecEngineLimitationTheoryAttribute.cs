namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Marks an inherited EF Core specification theory as unsupported because the
/// engine cannot express the required relational operation. The
/// <c>dispositionId</c> links discovery directly to the primary-source-backed
/// entry in <c>Specification/SpecDispositions.json</c>.
/// </summary>
/// <remarks>
/// Target selection is evaluated by the xUnit v3 execution pipeline from
/// <c>DOKA_SPEC_TEST_TARGET</c>. This produces an actual skipped test case without adding a
/// second theory attribute to the inherited EF Core test method.
/// Data rows may be declared on the provider override or inherited from its nearest base
/// declaration through <see cref="InheritedTheoryDataAttribute"/>.
/// Setting <c>DOKA_SPEC_TEST_PROBE_ENGINE_LIMITS=true</c> deliberately disables these skips
/// so the documented failure can be reproduced without editing test source.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpecEngineLimitationTheoryAttribute : SpecDispositionAttribute
{
    /// <summary>
    /// Creates an engine-limited theory disposition.
    /// </summary>
    /// <param name="dispositionId">
    /// Stable identifier of the corresponding machine-readable disposition.
    /// </param>
    /// <param name="firstUnsupportedTarget">First target covered by the disposition.</param>
    /// <param name="secondUnsupportedTarget">Optional second covered target.</param>
    /// <param name="thirdUnsupportedTarget">Optional third covered target.</param>
    public SpecEngineLimitationTheoryAttribute(
        string dispositionId,
        string firstUnsupportedTarget,
        string? secondUnsupportedTarget = null,
        string? thirdUnsupportedTarget = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispositionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstUnsupportedTarget);

        var unsupportedTargets = new List<string> { firstUnsupportedTarget };

        if (secondUnsupportedTarget is not null)
        {
            unsupportedTargets.Add(secondUnsupportedTarget);
        }

        if (thirdUnsupportedTarget is not null)
        {
            unsupportedTargets.Add(thirdUnsupportedTarget);
        }

        DispositionId = dispositionId;
        UnsupportedTargets = SpecEngineDispositionCatalog.GetTargets(
            dispositionId,
            unsupportedTargets);

        var target = SpecTestTarget.Resolve();
        if (UnsupportedTargets.Contains(target, StringComparer.OrdinalIgnoreCase)
            && !SpecTestTarget.IsEngineLimitationProbeEnabled())
        {
            SkipReason =
                $"[spec-engine-limit:{dispositionId}] Target '{target}' is covered by "
                + "the primary-source-backed specification disposition ledger.";
        }
    }

    /// <summary>
    /// Gets the stable identifier used to reconcile source annotations with the disposition
    /// ledger.
    /// </summary>
    public string DispositionId { get; }

    /// <summary>
    /// Gets the database targets for which the documented engine limitation applies.
    /// </summary>
    public IReadOnlyList<string> UnsupportedTargets { get; }
}
