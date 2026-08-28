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
        AssertExactPropertySet(
            context.GetService<IDesignTimeModel>().Model,
            typeof(BitemporalDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id",
            "RecordedFrom",
            "RecordedTo");
        AssertExactPropertySet(
            generated.SnapshotModel,
            typeof(BitemporalDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id",
            "RecordedFrom",
            "RecordedTo");
        AssertExactPropertySet(
            generated.DesignerModel,
            typeof(BitemporalDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id",
            "RecordedFrom",
            "RecordedTo");
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertSinglePhysicalBitemporalTableCreation(context);
    }

    /// <summary>
    /// Typed application-time endpoints remain the complete property set through
    /// both generated design-time surfaces and the initial migration.
    /// </summary>
    [Fact]
    public void Application_time_snapshot_and_designer_roundtrip_without_default_properties()
    {
        using var context = new ApplicationTimeDesignContext(
            CreateOptions<ApplicationTimeDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(
            ".HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPeriodStart(\"BusinessFrom\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasPeriodEnd(\"BusinessTo\")", generated.SnapshotCode, StringComparison.Ordinal);
        AssertExactPropertySet(
            context.GetService<IDesignTimeModel>().Model,
            typeof(ApplicationTimeDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id");
        AssertExactPropertySet(
            generated.SnapshotModel,
            typeof(ApplicationTimeDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id");
        AssertExactPropertySet(
            generated.DesignerModel,
            typeof(ApplicationTimeDesignRecord),
            "BusinessFrom",
            "BusinessTo",
            "Id");
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertSinglePhysicalApplicationTimeTableCreation(context);
    }

    /// <summary>
    /// Provider-native Char36 keys and foreign keys retain their Guid model CLR type
    /// through both generated design-time model surfaces.
    /// </summary>
    [Fact]
    public void Char36_snapshot_and_designer_roundtrip_guid_relationship_without_pending_operations()
    {
        using var context = new Char36DesignContext(
            CreateOptions<Char36DesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"DocumentId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasMySqlGuidFormat(MySqlGuidFormat.Char36)",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"DocumentId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"DocumentId\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
    }

    /// <summary>
    /// A context-level Char36 default remains a Guid model contract when one
    /// property participates in multiple relationship chains.
    /// </summary>
    [Fact]
    public void Default_char36_branching_relationships_roundtrip_without_string_model_drift()
    {
        using var context = new DefaultChar36RelationshipDesignContext(
            CreateOptions<DefaultChar36RelationshipDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".Property<System.Guid>(\"ReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid?>(\"OptionalReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"ReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"OptionalReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".HasColumnType(\"varchar(36)\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"ReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid?>(\"OptionalReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"ReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"OptionalReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
    }

    /// <summary>
    /// A context-level Char36 default and an explicit Binary16 override retain
    /// Guid model types and byte-order annotations through generated models.
    /// </summary>
    [Fact]
    public void Default_char36_and_binary16_override_roundtrip_through_generated_models()
    {
        using var context = new MixedGuidDesignContext(
            CreateOptions<MixedGuidDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"BinaryReference\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasMySqlGuidFormat(MySqlGuidFormat.Char36)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasMySqlGuidFormat(MySqlGuidFormat.Binary16)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<byte[]>(\"BinaryReference\")", generated.SnapshotCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
    }

    /// <summary>
    /// Explicit invisible and visible column dispositions survive both generated
    /// design-time model surfaces without falling back to raw annotations.
    /// </summary>
    [Fact]
    public void Invisible_column_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        using var context = new InvisibleColumnDesignContext(
            CreateOptions<InvisibleColumnDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var generated = GenerateAndCompile(context);

        Assert.Contains(".IsInvisible()", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsInvisible(false)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsInvisible()", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".IsInvisible(false)", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.Invisible, generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.Invisible, generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
    }

    /// <summary>
    /// Spatial SRID metadata survives generated snapshots on both the native MySQL
    /// route and the MariaDB CHECK-emulation route without a pending migration.
    /// </summary>
    [Fact]
    public void Spatial_srid_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        var serverVersions = new[]
        {
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
        };

        foreach (var serverVersion in serverVersions)
        {
            using var context = new SpatialSridDesignContext(CreateSpatialOptions(serverVersion));
            var generated = GenerateAndCompile(context);

            Assert.Contains(".HasSrid(4326)", generated.SnapshotCode, StringComparison.Ordinal);
            Assert.Contains(".HasSrid(4326)", generated.DesignerCode, StringComparison.Ordinal);
            Assert.DoesNotContain(
                MySqlAnnotationNames.SpatialReferenceSystemId,
                generated.SnapshotCode,
                StringComparison.Ordinal);
            AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
            AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        }
    }

    /// <summary>
    /// Entity-splitting snapshots retain one generated principal key without
    /// turning the secondary shared primary/foreign key into a generator.
    /// </summary>
    [Fact]
    public void Entity_splitting_snapshot_and_designer_preserve_principal_generation_ownership()
    {
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        using var context = new EntitySplitDesignContext(CreateOptions<EntitySplitDesignContext>(serverVersion));
        var generated = GenerateAndCompile(context);

        Assert.Contains(".SplitToTable(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".SplitToTable(", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(context, generated.SnapshotModel);
        AssertRoundTripsWithoutOperations(context, generated.DesignerModel);
        AssertEntitySplitGenerationOwnership(context, context.GetService<IDesignTimeModel>().Model, serverVersion);
        AssertEntitySplitGenerationOwnership(context, generated.SnapshotModel, serverVersion);
        AssertEntitySplitGenerationOwnership(context, generated.DesignerModel, serverVersion);
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

    private static void AssertSinglePhysicalBitemporalTableCreation(
        BitemporalDesignContext targetContext
    )
    {
        using var sourceContext = new EmptyTemporalDesignContext(
            CreateOptions<EmptyTemporalDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

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
        var columnNames = createTable
            .Columns.Select(column => column.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Id", "business_from", "business_to", "recorded_from", "recorded_to"],
            columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName, columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName, columnNames);
    }

    private static void AssertSinglePhysicalApplicationTimeTableCreation(
        ApplicationTimeDesignContext targetContext
    )
    {
        using var sourceContext = new EmptyTemporalDesignContext(
            CreateOptions<EmptyTemporalDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var operations = targetContext
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var columnNames = createTable
            .Columns.Select(column => column.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Id", "business_from", "business_to"], columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName, columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName, columnNames);
    }

    private static void AssertExactPropertySet(
        IModel model,
        Type entityType,
        params string[] expectedProperties
    )
    {
        var properties = model
            .FindEntityType(entityType)!
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProperties, properties);
    }

    private static void AssertEntitySplitGenerationOwnership(
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

        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                source
                    .GetService<IDesignTimeModel>()
                    .Model
                    .GetRelationalModel(),
                initializedModel.GetRelationalModel());

        var tables = operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        var principalId = Assert.Single(tables["EntitySplitDesignRecords"].Columns, column => column.Name == "Id");

        var secondaryId = Assert.Single(tables["EntitySplitDesignDetails"].Columns, column => column.Name == "Id");

        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)?.Value);
        Assert.Null(secondaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy));
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

    private sealed record GeneratedTemporalModels(
        string SnapshotCode,
        string DesignerCode,
        IModel SnapshotModel,
        IModel DesignerModel
    );
}

/// <summary>
/// Test context for generated spatial SRID migration models.
/// </summary>
public sealed class SpatialSridDesignContext : DbContext
{
    /// <summary>
    /// Creates the spatial design-time context.
    /// </summary>
    public SpatialSridDesignContext(
        DbContextOptions<SpatialSridDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<SpatialSridDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Location)
                .HasSrid(4326);
        });
    }
}

/// <summary>
/// Entity used by the generated spatial SRID migration model.
/// </summary>
public sealed class SpatialSridDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the SRID-constrained location.
    /// </summary>
    public Point Location { get; set; } = null!;
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
/// Test context for generated application-time migration models.
/// </summary>
public sealed class ApplicationTimeDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public ApplicationTimeDesignContext(
        DbContextOptions<ApplicationTimeDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<ApplicationTimeDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ToTable(
                "ApplicationTimeRecords",
                table => table.HasApplicationTimePeriod<ApplicationTimeDesignRecord>(applicationTime =>
                {
                    applicationTime.HasPeriodName("BusinessValidity");
                    applicationTime
                        .HasPeriodStart(record => record.BusinessFrom)
                        .HasColumnName("business_from");
                    applicationTime
                        .HasPeriodEnd(record => record.BusinessTo)
                        .HasColumnName("business_to");
                }));
        });
    }
}

/// <summary>
/// Entity used by the generated application-time migration model.
/// </summary>
public sealed class ApplicationTimeDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the inclusive application-time boundary.
    /// </summary>
    public DateTime BusinessFrom { get; set; }

    /// <summary>
    /// Gets or sets the exclusive application-time boundary.
    /// </summary>
    public DateTime BusinessTo { get; set; }
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

/// <summary>
/// Test context for generated provider-native Char36 migration models.
/// </summary>
public sealed class Char36DesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public Char36DesignContext(
        DbContextOptions<Char36DesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Char36DesignDocument>(entity =>
        {
            entity.ToTable("Char36DesignDocuments");
            entity.HasKey(document => document.Id);
            entity
                .Property(document => document.Id)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36)
                .UseMySqlClientGuidValueGeneration();
        });

        modelBuilder.Entity<Char36DesignRevision>(entity =>
        {
            entity.ToTable("Char36DesignRevisions");
            entity.HasKey(revision => revision.Id);
            entity
                .Property(revision => revision.DocumentId)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            entity
                .HasOne(revision => revision.Document)
                .WithMany(document => document.Revisions)
                .HasForeignKey(revision => revision.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>
/// Principal entity used by the generated Char36 migration model.
/// </summary>
public sealed class Char36DesignDocument
{
    /// <summary>
    /// Gets or sets the client-generated key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets the dependent revisions.
    /// </summary>
    public ICollection<Char36DesignRevision> Revisions { get; } = [];
}

/// <summary>
/// Dependent entity used by the generated Char36 migration model.
/// </summary>
public sealed class Char36DesignRevision
{
    /// <summary>
    /// Gets or sets the revision key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the principal Guid key.
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// Gets or sets the principal navigation.
    /// </summary>
    public Char36DesignDocument Document { get; set; } = null!;
}

/// <summary>
/// Test context for generated relationship models using a context-level Char36 default.
/// </summary>
public sealed class DefaultChar36RelationshipDesignContext : DbContext
{
    /// <summary>
    /// Creates the relationship design context.
    /// </summary>
    public DefaultChar36RelationshipDesignContext(
        DbContextOptions<DefaultChar36RelationshipDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<DefaultChar36RelationshipARoot>(entity => entity.HasKey(item => item.Id));

        modelBuilder.Entity<DefaultChar36RelationshipBLeft>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Root)
                .WithOne()
                .HasForeignKey<DefaultChar36RelationshipBLeft>(item => item.Id);
        });

        modelBuilder.Entity<DefaultChar36RelationshipCRight>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Root)
                .WithOne()
                .HasForeignKey<DefaultChar36RelationshipCRight>(item => item.Id);
        });

        modelBuilder.Entity<DefaultChar36RelationshipZLeaf>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Left)
                .WithMany()
                .HasForeignKey(item => item.ReferenceId);
            entity
                .HasOne(item => item.Right)
                .WithMany()
                .HasForeignKey(item => item.ReferenceId);
        });

        modelBuilder.Entity<DefaultChar36RelationshipZNullableLeaf>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity
                .HasOne(item => item.Left)
                .WithMany()
                .HasForeignKey(item => item.OptionalReferenceId);
            entity
                .HasOne(item => item.Right)
                .WithMany()
                .HasForeignKey(item => item.OptionalReferenceId);
        });
    }
}

/// <summary>
/// Root entity for the generated context-level Char36 relationship model.
/// </summary>
public sealed class DefaultChar36RelationshipARoot
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public Guid Id { get; set; }
}

/// <summary>
/// Left relationship branch for the generated context-level Char36 model.
/// </summary>
public sealed class DefaultChar36RelationshipBLeft
{
    /// <summary>
    /// Gets or sets the shared primary and foreign key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the root navigation.
    /// </summary>
    public DefaultChar36RelationshipARoot Root { get; set; } = null!;
}

/// <summary>
/// Right relationship branch for the generated context-level Char36 model.
/// </summary>
public sealed class DefaultChar36RelationshipCRight
{
    /// <summary>
    /// Gets or sets the shared primary and foreign key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the root navigation.
    /// </summary>
    public DefaultChar36RelationshipARoot Root { get; set; } = null!;
}

/// <summary>
/// Leaf entity whose GUID participates in both relationship branches.
/// </summary>
public sealed class DefaultChar36RelationshipZLeaf
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the GUID shared by both foreign keys.
    /// </summary>
    public Guid ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the left branch navigation.
    /// </summary>
    public DefaultChar36RelationshipBLeft Left { get; set; } = null!;

    /// <summary>
    /// Gets or sets the right branch navigation.
    /// </summary>
    public DefaultChar36RelationshipCRight Right { get; set; } = null!;
}

/// <summary>
/// Optional leaf entity whose nullable GUID participates in both relationship branches.
/// </summary>
public sealed class DefaultChar36RelationshipZNullableLeaf
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the optional GUID shared by both foreign keys.
    /// </summary>
    public Guid? OptionalReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the optional left branch navigation.
    /// </summary>
    public DefaultChar36RelationshipBLeft? Left { get; set; }

    /// <summary>
    /// Gets or sets the optional right branch navigation.
    /// </summary>
    public DefaultChar36RelationshipCRight? Right { get; set; }
}

/// <summary>
/// Test context for mixed default and property-level Guid formats.
/// </summary>
public sealed class MixedGuidDesignContext : DbContext
{
    /// <summary>
    /// Creates the mixed Guid design context.
    /// </summary>
    public MixedGuidDesignContext(
        DbContextOptions<MixedGuidDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<MixedGuidDesignRecord>(entity =>
        {
            entity.ToTable("MixedGuidDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.BinaryReference)
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
        });
    }
}

/// <summary>
/// Entity used by the mixed Guid design model.
/// </summary>
public sealed class MixedGuidDesignRecord
{
    /// <summary>
    /// Gets or sets the Char36 default key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the explicitly Binary16 value.
    /// </summary>
    public Guid BinaryReference { get; set; }
}

/// <summary>
/// Test context for generated MySQL-family invisible-column migration models.
/// </summary>
public sealed class InvisibleColumnDesignContext : DbContext
{
    /// <summary>
    /// Creates the test context.
    /// </summary>
    public InvisibleColumnDesignContext(
        DbContextOptions<InvisibleColumnDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<InvisibleColumnDesignRecord>(entity =>
        {
            entity.ToTable("InvisibleColumnDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.HiddenValue)
                .HasDefaultValue(string.Empty)
                .IsInvisible();
            entity
                .Property(record => record.VisibleValue)
                .IsInvisible(false);
        });
    }
}

/// <summary>
/// Entity used by the generated invisible-column migration model.
/// </summary>
public sealed class InvisibleColumnDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the hidden value.
    /// </summary>
    public string HiddenValue { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the explicitly visible value.
    /// </summary>
    public string VisibleValue { get; set; } = string.Empty;
}

/// <summary>
/// Test context for generated entity-splitting migration models.
/// </summary>
public sealed class EntitySplitDesignContext : DbContext
{
    /// <summary>
    /// Creates the entity-splitting design context.
    /// </summary>
    public EntitySplitDesignContext(
        DbContextOptions<EntitySplitDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<EntitySplitDesignRecord>(entity =>
        {
            entity.ToTable("EntitySplitDesignRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).UseMySqlAutoIncrementColumn();
            entity.SplitToTable(
                "EntitySplitDesignDetails",
                split => split.Property(record => record.Description));
        });
    }
}

/// <summary>
/// Entity used by the generated entity-splitting migration model.
/// </summary>
public sealed class EntitySplitDesignRecord
{
    /// <summary>
    /// Gets or sets the generated key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the property held by the principal table.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the property held by the secondary table.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
