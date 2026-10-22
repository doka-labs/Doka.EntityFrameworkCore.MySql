using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGuidSqlDefaultDdlIntegrationTests
{
    private static Assembly CompileScaffoldedModel(
        ScaffoldedModel scaffoldedModel
    )
    {
        var sources = new[] { scaffoldedModel.ContextFile.Code }
            .Concat(scaffoldedModel.AdditionalFiles.Select(file => file.Code));
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];
        var references = trustedPlatformAssemblies
            .Concat(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                    .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            $"Doka.GeneratedGuidSqlDefaults.{Guid.NewGuid():N}",
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var errors = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString());

        Assert.True(result.Success, string.Join(Environment.NewLine, errors));

        return Assembly.Load(stream.ToArray());
    }

    private static DbContext CreateCompiledContext(
        Assembly compiledAssembly,
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        var contextType = compiledAssembly.GetType(
            "Doka.Scaffolding.GuidSqlDefaultScaffoldContext",
            throwOnError: true)!;
        var factory = typeof(MySqlGuidSqlDefaultDdlIntegrationTests)
            .GetMethod(nameof(CreateCompiledContextOptions), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(contextType);
        var options = factory.Invoke(null, [connection, target]);

        return (DbContext)Activator.CreateInstance(contextType, options)!;
    }

    private static DbContextOptions<TContext> CreateCompiledContextOptions<TContext>(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
        where TContext : DbContext => IntegrationTestDbContextOptions.Create<TContext>()
        .UseMySql(connection, IntegrationTestEnvironment.GetServerVersion(target))
        .Options;
}
