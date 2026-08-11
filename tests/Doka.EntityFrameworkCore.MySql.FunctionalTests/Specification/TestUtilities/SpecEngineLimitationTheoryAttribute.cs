using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Marks an inherited EF Core specification theory as unsupported because the
/// engine cannot express the required relational operation. The
/// <c>dispositionId</c> links discovery directly to the primary-source-backed
/// entry in <c>Specification/SpecDispositions.json</c>.
/// </summary>
/// <remarks>
/// Target selection is evaluated during xUnit discovery from
/// <c>DOKA_SPEC_TEST_TARGET</c>. This produces an actual skipped test case and prevents an
/// engine exception from being hidden behind a successful no-op method body.
/// Data rows may be declared on the provider override or inherited from its nearest base
/// declaration; the custom discoverer consumes exactly one source.
/// Setting <c>DOKA_SPEC_TEST_PROBE_ENGINE_LIMITS=true</c> deliberately disables these skips
/// so the documented failure can be reproduced without editing test source.
/// </remarks>
[XunitTestCaseDiscoverer(
    "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities."
    + "DirectTheoryDiscoverer",
    "Doka.EntityFrameworkCore.MySql.FunctionalTests")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpecEngineLimitationTheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// Creates an engine-limited theory disposition.
    /// </summary>
    /// <param name="dispositionId">
    /// Stable identifier of the corresponding machine-readable disposition.
    /// </param>
    /// <param name="unsupportedTargets">
    /// Targets covered when the source annotation was authored. The ledger may
    /// add later LTS targets, but it cannot silently remove an annotated target.
    /// </param>
    public SpecEngineLimitationTheoryAttribute(
        string dispositionId,
        params string[] unsupportedTargets
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispositionId);
        ArgumentNullException.ThrowIfNull(unsupportedTargets);

        DispositionId = dispositionId;
        UnsupportedTargets = SpecEngineDispositionCatalog.GetTargets(
            dispositionId,
            unsupportedTargets);

        var target = SpecTestTarget.Resolve();
        if (UnsupportedTargets.Contains(target, StringComparer.OrdinalIgnoreCase)
            && !SpecTestTarget.IsEngineLimitationProbeEnabled())
        {
            Skip =
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
