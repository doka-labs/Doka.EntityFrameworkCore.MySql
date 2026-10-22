using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlReverseEngineeringBaselineTests
{
    /// <summary>Provides text opt-out, text opt-in, and native UUID default-bearing scaffolding cases.</summary>
    public static IEnumerable<object?[]> GuidSqlDefaultScaffoldingCases()
    {
        foreach (var isKey in new[] { false, true })
        {
            yield return ["char(36)", "8.4.6", false, typeof(string), null, isKey];
            yield return ["char(36)", "8.4.6", true, typeof(Guid), MySqlGuidFormat.Char36, isKey];
            yield return ["uuid", "11.8.3-MariaDB", false, typeof(Guid), MySqlGuidFormat.Uuid, isKey];
        }
    }

    /// <summary>Default-bearing columns retain type opt-in semantics and store generation after compiled replay.</summary>
    [Theory]
    [MemberData(nameof(GuidSqlDefaultScaffoldingCases))]
    public void Scaffolded_guid_sql_default_compiles_and_replays_store_generation(
        string storeType,
        string serverVersion,
        bool textGuidOptIn,
        Type expectedType,
        MySqlGuidFormat? expectedFormat,
        bool isKey
    )
    {
        // Arrange
        var databaseModel = CreateSingleColumnDatabaseModel("guid_default_record", "ExternalId", storeType);
        var table = databaseModel.Tables.Single();
        var column = table.Columns.Single(property => property.Name == "ExternalId");

        column.DefaultValueSql = "UUID()";
        column.ValueGenerated = ValueGenerated.OnAdd;

        if (isKey)
        {
            table.Columns.Remove(table.Columns.Single(property => property.Name == "Id"));
            table.PrimaryKey = new DatabasePrimaryKey
            {
                Table = table,
                Name = "PK_guid_default_record",
                Columns = { column },
            };
        }

        // Act
        var scaffolded = ScaffoldModel(
            databaseModel,
            serverVersion,
            textGuidOptIn ? options => options.ScaffoldTextGuidsAsGuids() : null);

        using var replay = CompileGuidSqlDefaultScaffold(scaffolded);
        var model = replay.GetService<IDesignTimeModel>().Model;
        var property = model.GetEntityTypes().Single().FindProperty("ExternalId")
            ?? throw new InvalidOperationException("The scaffolded model lost its default-bearing column.");

        var selected = replay.GetService<Microsoft.EntityFrameworkCore.ValueGeneration.IValueGeneratorSelector>()
            .TrySelect(property, property.DeclaringType, out var generator);

        // Assert
        Assert.Equal(expectedType, property.ClrType);
        Assert.Equal(expectedFormat, property.GetMySqlGuidFormat());
        Assert.Equal(storeType, property.GetColumnType());
        Assert.Equal("UUID()", property.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
        Assert.Equal(isKey, property.IsPrimaryKey());
        Assert.Null(property.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy));
        Assert.True(selected);
        Assert.NotNull(generator);
        Assert.True(generator.GeneratesTemporaryValues);
        Assert.Contains("HasDefaultValueSql(\"UUID()\")", scaffolded.ContextFile.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientGuid", scaffolded.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains(
            expectedType == typeof(Guid) ? "public Guid ExternalId" : "public string ExternalId",
            scaffolded.AdditionalFiles.Single().Code,
            StringComparison.Ordinal);
    }

    /// <summary>Compiles the actual generated context and entity source before EF finalizes the replayed model.</summary>
    private static DbContext CompileGuidSqlDefaultScaffold(
        ScaffoldedModel scaffolded
    )
    {
        var sources = scaffolded.AdditionalFiles
            .Select(file => file.Code)
            .Prepend(scaffolded.ContextFile.Code)
            .Select(source => CSharpSyntaxTree.ParseText(source));

        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var references = trustedAssemblies
            .Concat(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                    .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            $"Doka.GeneratedGuidDefaultScaffolding.{Guid.NewGuid():N}",
            sources,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var errors = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString());

        Assert.True(result.Success, string.Join(Environment.NewLine, errors));

        var assembly = Assembly.Load(stream.ToArray());
        var contextType = assembly.GetType("Phase2.Scaffolding.ReverseDbContext", throwOnError: true)!;

        return (DbContext)Activator.CreateInstance(contextType)!;
    }
}
