using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Discovers specification theories from one unambiguous data source. Direct provider rows
/// take precedence; an override without rows inherits them from the nearest data-bearing base
/// declaration. The discoverer never combines both sources, which prevents duplicate test IDs.
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
    /// targets it creates exactly one case per direct data row, or per row on the nearest
    /// data-bearing base declaration when the provider override only changes executable
    /// metadata.
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
        var dataMethod = methodInfo;
        var dataAttributes = methodInfo
            .GetCustomAttributes<DataAttribute>(inherit: false)
            .ToArray();

        if (dataAttributes.Length == 0)
        {
            dataMethod = TheoryDataInheritance.FindNearestDataDeclaration(methodInfo)
                ?? methodInfo;
            dataAttributes = dataMethod
                .GetCustomAttributes<DataAttribute>(inherit: false)
                .ToArray();
        }

        var dataRows = dataAttributes
            .SelectMany(attribute => attribute.GetData(dataMethod))
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
                    "Direct-data theories require rows on the provider override "
                    + "or a matching base declaration."),
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
