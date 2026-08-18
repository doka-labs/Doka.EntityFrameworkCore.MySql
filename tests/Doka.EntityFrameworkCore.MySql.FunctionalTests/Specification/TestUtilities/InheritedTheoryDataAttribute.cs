using System.Reflection;
using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Supplies a provider override with the theory rows declared by its nearest data-bearing
/// base method.
/// </summary>
/// <remarks>
/// A provider override sometimes changes only test metadata, such as an engine disposition.
/// xUnit does not inherit data attributes from the base declaration, so this attribute makes
/// that inheritance explicit to both the analyzer and the custom theory discoverer.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class InheritedTheoryDataAttribute : DataAttribute
{
    /// <summary>
    /// Resolves and returns every data row from the nearest matching base declaration
    /// that declares theory data.
    /// </summary>
    /// <param name="testMethod">Provider override requesting inherited theory data.</param>
    /// <returns>The exact data rows declared by the nearest data-bearing base method.</returns>
    public override IEnumerable<object[]> GetData(
        MethodInfo testMethod
    )
    {
        ArgumentNullException.ThrowIfNull(testMethod);

        var baseMethod = TheoryDataInheritance.FindNearestDataDeclaration(testMethod)
            ?? throw new InvalidOperationException(
                $"Method '{testMethod.DeclaringType?.FullName}.{testMethod.Name}' has no "
                + "matching base declaration with theory data.");

        var dataAttributes = baseMethod
            .GetCustomAttributes<DataAttribute>(inherit: false)
            .ToArray();

        return dataAttributes
            .SelectMany(attribute => attribute.GetData(baseMethod))
            .ToArray();
    }
}

internal static class TheoryDataInheritance
{
    internal static MethodInfo? FindNearestDataDeclaration(
        MethodInfo methodInfo
    )
    {
        var parameterTypes = methodInfo
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        for (var type = methodInfo.DeclaringType?.BaseType; type is not null; type = type.BaseType)
        {
            var candidate = type.GetMethod(
                methodInfo.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                parameterTypes,
                modifiers: null);

            if (candidate is not null
                && candidate
                    .GetCustomAttributes<DataAttribute>(inherit: false)
                    .Any())
            {
                return candidate;
            }
        }

        return null;
    }
}
