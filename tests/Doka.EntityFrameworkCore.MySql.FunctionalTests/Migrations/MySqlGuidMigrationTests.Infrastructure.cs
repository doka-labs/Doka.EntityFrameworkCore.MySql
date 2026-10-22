using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the infrastructure responsibility for MySqlGuidMigrationTests.</summary>
public sealed partial class MySqlGuidMigrationTests
{
    /// <summary>Captures one model-to-operation-to-source/SQL pipeline before its contract is asserted.</summary>
    private static GuidMigrationProbe ProbeTransition(
        DbContext source,
        DbContext target
    )
    {
        var operations = GetDifferences(source, target);
        var code = GenerateMigrationCode(operations);
        var compilation = CompileMigrationCode(code);
        var failure = Record.Exception(() => target.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, target.GetService<IDesignTimeModel>().Model));

        return new GuidMigrationProbe(operations, code, compilation, failure);
    }

    /// <summary>
    /// Keeps transition observations together without repeating a provider operation in the assertion phase.
    /// </summary>
    private sealed record GuidMigrationProbe(
        List<MigrationOperation> Operations,
        string Code,
        (bool Success, string[] Errors) Compilation,
        Exception? SqlFailure
    );

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext => CreateOptions<TContext>(MySqlGuidFormat.Binary16);

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlGuidFormat defaultGuidFormat
    )
        where TContext : DbContext => MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>().UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
            options => options.DefaultGuidFormat(defaultGuidFormat))
        .Options;

    private static List<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            target.GetService<IDesignTimeModel>().Model.GetRelationalModel())
        .ToList();

    private static List<MigrationOperation> GetDifferences(
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            null,
            target.GetService<IDesignTimeModel>().Model.GetRelationalModel())
        .ToList();

    private static string GenerateMigrationCode(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddEntityFrameworkDokaMySqlDesignTime();

        using var serviceProvider = services.BuildServiceProvider();
        var generator = serviceProvider
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        return generator.GenerateMigration(
            "Doka.GeneratedGuidMigrations",
            "GuidMigration",
            operations,
            []);
    }

    /// <summary>Compiles generated migration source before the test asserts its pipeline observations.</summary>
    private static (bool Success, string[] Errors) CompileMigrationCode(
        string source
    )
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var references = trustedPlatformAssemblies
            .Concat(
                AppDomain
                    .CurrentDomain
                    .GetAssemblies()
                    .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                    .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            $"Doka.GeneratedGuidMigrations.{Guid.NewGuid():N}",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var errors = result
            .Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        return (result.Success, errors);
    }
}
