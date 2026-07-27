using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Marks an inherited EF Core specification theory that the consumed EF Core version skips
/// because framework-owned translation or query semantics cannot currently produce a
/// provider-solvable SQL tree. The disposition ID links the executable skip to the
/// upstream-issue-backed ledger entry.
/// </summary>
/// <remarks>
/// Setting <c>DOKA_SPEC_TEST_PROBE_FRAMEWORK_LIMITS=true</c> disables the skip so an EF Core
/// update can be checked without editing test source.
/// </remarks>
[XunitTestCaseDiscoverer(
    "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities."
    + "DirectTheoryDiscoverer",
    "Doka.EntityFrameworkCore.MySql.FunctionalTests")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SpecFrameworkLimitationTheoryAttribute : TheoryAttribute
{
    private const string ProbeEnvironmentVariable = "DOKA_SPEC_TEST_PROBE_FRAMEWORK_LIMITS";

    /// <summary>
    /// Creates a framework-limited specification theory linked to a stable ledger entry.
    /// </summary>
    /// <param name="dispositionId">
    /// Stable identifier of the corresponding framework disposition.
    /// </param>
    public SpecFrameworkLimitationTheoryAttribute(
        string dispositionId
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispositionId);
        DispositionId = dispositionId;

        if (!string.Equals(
                Environment.GetEnvironmentVariable(ProbeEnvironmentVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip =
                $"[spec-framework-limit:{dispositionId}] The consumed EF Core version skips "
                + "this shape due to a framework-owned limitation outside provider SQL "
                + "generation. See SpecDispositions.json.";
        }
    }

    /// <summary>
    /// Gets the stable identifier used to reconcile the source annotation with the ledger.
    /// </summary>
    public string DispositionId { get; }
}
