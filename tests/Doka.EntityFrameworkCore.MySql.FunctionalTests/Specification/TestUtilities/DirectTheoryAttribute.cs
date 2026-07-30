using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Marks a provider override whose direct data rows replace, rather than combine with, inherited
/// rows. When no direct rows exist, the nearest base declaration supplies the rows. This keeps
/// metadata-only overrides concise without producing duplicate test IDs.
/// </summary>
[XunitTestCaseDiscoverer(
    "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities."
    + "DirectTheoryDiscoverer",
    "Doka.EntityFrameworkCore.MySql.FunctionalTests")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DirectTheoryAttribute : TheoryAttribute
{
}
