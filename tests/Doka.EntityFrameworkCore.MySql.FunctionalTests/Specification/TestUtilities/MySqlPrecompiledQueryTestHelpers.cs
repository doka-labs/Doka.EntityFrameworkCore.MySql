using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Supplies the provider assemblies required when EF Core compiles a query test
/// into a temporary application.
/// </summary>
public sealed class MySqlPrecompiledQueryTestHelpers : PrecompiledQueryTestHelpers
{
    public static MySqlPrecompiledQueryTestHelpers Instance { get; } = new();

    private MySqlPrecompiledQueryTestHelpers() { }

    protected override IEnumerable<MetadataReference> BuildProviderMetadataReferences()
    {
        yield return MetadataReference.CreateFromFile(typeof(MySqlOptionsExtension).Assembly.Location);

        yield return MetadataReference.CreateFromFile(
            Assembly.GetExecutingAssembly().Location);
    }
}
