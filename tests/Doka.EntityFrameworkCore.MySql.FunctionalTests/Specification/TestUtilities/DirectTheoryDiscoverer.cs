using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Discovers specification theories from data attributes declared directly on the provider
/// override. EF Core specification base classes use inheritable data attributes; consuming
/// both inherited and direct attributes would create duplicate test IDs.
/// </summary>
public sealed class DirectTheoryDiscoverer : TheoryDiscoverer
{
    /// <summary>
    /// Creates a discoverer that reports diagnostics through the xUnit discovery sink.
    /// </summary>
    /// <param name="diagnosticMessageSink">
    /// Sink used by xUnit to report discovery diagnostics.
    /// </param>
    public DirectTheoryDiscoverer(
        IMessageSink diagnosticMessageSink
    ) : base(diagnosticMessageSink)
    {
    }

    /// <summary>
    /// Produces one skipped case when the current engine target is unsupported. On supported
    /// targets it creates exactly one case per directly declared <see cref="InlineDataAttribute"/>
    /// row and intentionally excludes inherited EF Core data attributes.
    /// </summary>
    /// <param name="discoveryOptions">Current xUnit discovery options.</param>
    /// <param name="testMethod">Provider override being discovered.</param>
    /// <param name="theoryAttribute">Direct-data theory annotation.</param>
    /// <returns>The exact xUnit cases represented by the provider override.</returns>
    public override IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo theoryAttribute
    )
    {
        var skipReason = theoryAttribute.GetNamedArgument<string>(nameof(FactAttribute.Skip));
        if (skipReason is not null)
        {
            return CreateTestCasesForSkip(
                discoveryOptions,
                testMethod,
                theoryAttribute,
                skipReason);
        }

        if (testMethod.Method is not IReflectionMethodInfo reflectionMethod)
        {
            return
            [
                new ExecutionErrorTestCase(
                    DiagnosticMessageSink,
                    discoveryOptions.MethodDisplayOrDefault(),
                    discoveryOptions.MethodDisplayOptionsOrDefault(),
                    testMethod,
                    "Direct-data theories require reflection-backed discovery."),
            ];
        }

        var methodInfo = reflectionMethod.MethodInfo;
        var dataRows = methodInfo
            .GetCustomAttributes<InlineDataAttribute>(inherit: false)
            .SelectMany(attribute => attribute.GetData(methodInfo))
            .ToArray();
        if (dataRows.Length == 0)
        {
            return
            [
                new ExecutionErrorTestCase(
                    DiagnosticMessageSink,
                    discoveryOptions.MethodDisplayOrDefault(),
                    discoveryOptions.MethodDisplayOptionsOrDefault(),
                    testMethod,
                    "Direct-data theories require directly declared InlineData rows."),
            ];
        }

        return dataRows.SelectMany(dataRow =>
            CreateTestCasesForDataRow(
                discoveryOptions,
                testMethod,
                theoryAttribute,
                dataRow));
    }
}
