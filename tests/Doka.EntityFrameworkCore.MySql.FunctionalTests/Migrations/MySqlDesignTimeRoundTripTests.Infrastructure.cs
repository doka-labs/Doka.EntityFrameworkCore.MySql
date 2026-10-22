using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    private const string GeneratedNamespace = "Doka.GeneratedTemporalMigrations";

    private static readonly DateTime s_providerRowVersion =
        new(2026, 8, 31, 12, 34, 56, DateTimeKind.Utc);

    /// <summary>
    /// Generates and compiles a snapshot and designer together so both reconstructed models share the production code-generation pipeline.
    /// </summary>
    private static GeneratedTemporalModels GenerateAndCompile(
        DbContext context
    )
    {
        var sourceModel = context.GetService<IDesignTimeModel>().Model;
        var suffix = Guid
            .NewGuid()
            .ToString("N", CultureInfo.InvariantCulture);

        var snapshotName = $"TemporalSnapshot{suffix}";
        var migrationName = $"TemporalMigration{suffix}";
        var migrationId = $"20260818000000_{migrationName}";
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddEntityFrameworkDokaMySqlDesignTime();
        services.AddEntityFrameworkDokaMySqlNetTopologySuite();

        using var serviceProvider = services.BuildServiceProvider();
        var generator = serviceProvider
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        var snapshotCode = generator.GenerateSnapshot(GeneratedNamespace, context.GetType(), snapshotName, sourceModel);
        var migrationCode = generator.GenerateMigration(GeneratedNamespace, migrationName, [], []);
        var designerCode = generator.GenerateMetadata(
            GeneratedNamespace,
            context.GetType(),
            migrationName,
            migrationId,
            sourceModel);

        var assembly = Compile(snapshotCode, migrationCode, designerCode);
        var snapshot = (ModelSnapshot)Activator.CreateInstance(
            assembly.GetType($"{GeneratedNamespace}.{snapshotName}", throwOnError: true)!)!;

        var migration = (Migration)Activator.CreateInstance(
            assembly.GetType($"{GeneratedNamespace}.{migrationName}", throwOnError: true)!)!;

        return new GeneratedTemporalModels(snapshotCode, designerCode, snapshot.Model, migration.TargetModel);
    }

    /// <summary>
    /// Generates executable C# migration source for the supplied operations.
    /// </summary>
    private static string GenerateMigrationCode(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddEntityFrameworkDokaMySqlDesignTime();
        services.AddEntityFrameworkDokaMySqlNetTopologySuite();

        using var serviceProvider = services.BuildServiceProvider();
        var generator = serviceProvider
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        return generator.GenerateMigration(
            GeneratedNamespace,
            $"ProviderMigration{Guid.NewGuid():N}",
            operations,
            []);
    }

    /// <summary>
    /// Compiles generated migration sources against the same runtime references used by the test process.
    /// </summary>
    private static Assembly Compile(
        params string[] sources
    )
    {
        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source))
            .ToArray();

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
            $"Doka.GeneratedTemporalMigrations.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var errors = result
            .Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.True(result.Success, string.Join(Environment.NewLine, errors));

        return Assembly.Load(stream.ToArray());
    }

    /// <summary>
    /// Initializes one generated model and captures both EF model-difference observations during the test action.
    /// </summary>
    private static RoundTripResult CaptureRoundTrip(
        DbContext context,
        IModel generatedModel
    )
    {
        var sourceModel = context.GetService<IDesignTimeModel>().Model;
        var initializedGeneratedModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(
                generatedModel,
                designTime: true,
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>());

        var generatedRelationalModel = initializedGeneratedModel.GetRelationalModel();
        var sourceRelationalModel = sourceModel.GetRelationalModel();
        var modelDiffer = context.GetService<IMigrationsModelDiffer>();

        var hasDifferences = modelDiffer.HasDifferences(generatedRelationalModel, sourceRelationalModel);
        var operations = modelDiffer.GetDifferences(generatedRelationalModel, sourceRelationalModel);

        return new RoundTripResult(hasDifferences, operations);
    }

    private static void AssertRoundTripsWithoutOperations(
        RoundTripResult result
    )
    {
        Assert.False(result.HasDifferences);
        Assert.Empty(result.Operations);
    }

    /// <summary>
    /// Captures initial migration operations for a source or reconstructed design-time model.
    /// </summary>
    private static IReadOnlyList<MigrationOperation> CaptureInitialOperations(
        DbContext context,
        IModel model
    )
    {
        var initializedModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(
                model,
                designTime: true,
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>());

        return context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, initializedModel.GetRelationalModel());
    }

    /// <summary>
    /// Captures table-creation operations against the same empty provider model used by the temporal and split-table contracts.
    /// </summary>
    private static IReadOnlyList<MigrationOperation> CaptureTableCreation(
        DbContext context,
        IModel model,
        MySqlServerVersion serverVersion
    )
    {
        using var source = new EmptyTemporalDesignContext(CreateOptions<EmptyTemporalDesignContext>(serverVersion));
        var initializedModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(
                model,
                designTime: true,
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>());

        return context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                initializedModel.GetRelationalModel());
    }

    /// <summary>
    /// Generates and compiles the initial migration before its captured operations and source are asserted.
    /// </summary>
    private static GeneratedInitialMigration CaptureInitialMigration(
        DbContext context,
        IModel model
    )
    {
        var operations = CaptureInitialOperations(context, model);
        var code = GenerateMigrationCode(operations);

        _ = Compile(code);

        return new GeneratedInitialMigration(operations, code);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion serverVersion,
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
        where TContext : DbContext => MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>().UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.DefaultGuidFormat(defaultGuidFormat))
        .Options;

    private static DbContextOptions<SpatialSridDesignContext> CreateSpatialOptions(
        MySqlServerVersion serverVersion
    ) => MySqlFunctionalTestOptions
        .CreateTransientBuilder<SpatialSridDesignContext>()
        .UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options => options.UseNetTopologySuite())
        .Options;

    /// <summary>
    /// Keeps generated source and both reconstructed model surfaces together for round-trip assertions.
    /// </summary>
    private sealed record GeneratedTemporalModels(
        string SnapshotCode,
        string DesignerCode,
        IModel SnapshotModel,
        IModel DesignerModel
    );

    /// <summary>Stores both observations of one reconstructed model without rerunning the differ during assertions.</summary>
    private readonly record struct RoundTripResult(
        bool HasDifferences,
        IReadOnlyList<MigrationOperation> Operations
    );

    /// <summary>Stores initial operations together with their successfully compiled migration source.</summary>
    private readonly record struct GeneratedInitialMigration(
        IReadOnlyList<MigrationOperation> Operations,
        string Code
    );
}
