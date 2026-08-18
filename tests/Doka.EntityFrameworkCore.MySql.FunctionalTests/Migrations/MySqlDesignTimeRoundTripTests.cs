using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies provider-specific migrations snapshot and designer model round trips.
/// </summary>
public sealed class MySqlDesignTimeRoundTripTests
{
    private const string GeneratedNamespace = "Doka.GeneratedTemporalMigrations";

    /// <summary>
    /// System-time metadata, including convention-inherited owned mapping, survives
    /// both generated design-time model surfaces without creating a pending migration.
    /// </summary>
    [Fact]
    public void System_time_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        using var context = new SystemTimeDesignContext(
            CreateOptions<SystemTimeDesignContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".IsTemporal(temporalTableBuilder =>", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".UseHistoryTable(\"TemporalRecordsHistory\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPeriodStart(\"ValidFrom\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"valid_from\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsTemporal(temporalTableBuilder =>", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsTemporal, generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsTemporal, generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertSinglePhysicalTemporalTableCreation(context);
    }

    /// <summary>
    /// Application-time and system-time metadata share one generated table closure and
    /// both survive snapshot and designer compilation for a bitemporal MariaDB model.
    /// </summary>
    [Fact]
    public void Bitemporal_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        using var context = new BitemporalDesignContext(
            CreateOptions<BitemporalDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".IsTemporal(temporalTableBuilder =>", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPeriodName(\"BusinessValidity\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".UseWithoutOverlaps()", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            generated.DesignerCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsApplicationTime, generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.IsApplicationTime, generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
    }

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

    private static void AssertRoundTripsWithoutOperations(
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

        var differences = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(initializedGeneratedModel.GetRelationalModel(), sourceModel.GetRelationalModel());

        Assert.Empty(differences);
    }

    private static void AssertSinglePhysicalTemporalTableCreation(
        SystemTimeDesignContext targetContext
    )
    {
        using var sourceContext = new EmptyTemporalDesignContext(
            CreateOptions<EmptyTemporalDesignContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var operations = targetContext
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                sourceContext
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel(),
                targetContext
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel());

        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());

        Assert.Equal("TemporalRecords", createTable.Name);
        Assert.True(
            createTable.FindAnnotation(MySqlAnnotationNames.IsTemporal)
                ?.Value as bool?);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>().UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion)
        .Options;

    private sealed record GeneratedTemporalModels(
        string SnapshotCode,
        string DesignerCode,
        IModel SnapshotModel,
        IModel DesignerModel
    );
}

/// <summary>
/// Test context for generated system-time migration models.
/// </summary>
public sealed class SystemTimeDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public SystemTimeDesignContext(
        DbContextOptions<SystemTimeDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<SystemTimeDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ToTable(
                "TemporalRecords",
                table => table.IsTemporal(temporal =>
                {
                    temporal.UseHistoryTable("TemporalRecordsHistory");
                    temporal
                        .HasPeriodStart("ValidFrom")
                        .HasColumnName("valid_from");
                    temporal
                        .HasPeriodEnd("ValidTo")
                        .HasColumnName("valid_to");
                }));
            entity.OwnsOne(record => record.Details);
        });
    }
}

/// <summary>
/// Empty model used to verify initial temporal migration operations.
/// </summary>
public sealed class EmptyTemporalDesignContext : DbContext
{
    /// <summary>
    /// Creates the empty test context.
    /// </summary>
    public EmptyTemporalDesignContext(
        DbContextOptions<EmptyTemporalDesignContext> options
    ) : base(options) { }
}

/// <summary>
/// Entity used by the generated system-time migration model.
/// </summary>
public sealed class SystemTimeDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the table-split details.
    /// </summary>
    public SystemTimeDesignDetails Details { get; set; } = new();
}

/// <summary>
/// Owned details used by the generated system-time migration model.
/// </summary>
public sealed class SystemTimeDesignDetails
{
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = null!;
}

/// <summary>
/// Test context for generated bitemporal migration models.
/// </summary>
public sealed class BitemporalDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public BitemporalDesignContext(
        DbContextOptions<BitemporalDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<BitemporalDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ToTable(
                "BitemporalRecords",
                table =>
                {
                    table.IsTemporal(temporal =>
                    {
                        temporal
                            .HasPeriodStart("RecordedFrom")
                            .HasColumnName("recorded_from");
                        temporal
                            .HasPeriodEnd("RecordedTo")
                            .HasColumnName("recorded_to");
                    });
                    table.HasApplicationTimePeriod(applicationTime =>
                    {
                        applicationTime.HasPeriodName("BusinessValidity");
                        applicationTime
                            .HasPeriodStart("BusinessFrom")
                            .HasColumnName("business_from");
                        applicationTime
                            .HasPeriodEnd("BusinessTo")
                            .HasColumnName("business_to");
                        applicationTime.UseWithoutOverlaps();
                    });
                });
        });
    }
}

/// <summary>
/// Entity used by the generated bitemporal migration model.
/// </summary>
public sealed class BitemporalDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }
}
