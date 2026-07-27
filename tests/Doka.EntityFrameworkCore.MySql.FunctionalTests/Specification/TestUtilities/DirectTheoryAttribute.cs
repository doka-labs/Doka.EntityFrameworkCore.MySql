using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Marks a provider override whose data rows must come only from attributes declared directly
/// on that override. This prevents inheritable EF Core data attributes from producing duplicate
/// test IDs while retaining the original relational assertion body.
/// </summary>
[XunitTestCaseDiscoverer(
    "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities."
    + "DirectTheoryDiscoverer",
    "Doka.EntityFrameworkCore.MySql.FunctionalTests")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DirectTheoryAttribute : TheoryAttribute
{
}
