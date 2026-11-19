namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDefaultTextMigrationTests
{
    /// <summary>Actual compiled snapshots and designers retain option defaults and independent overrides.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB model conventions.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generated_snapshot_and_designer_roundtrip_canonical_text_defaults(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(new TextShape
            {
                TableCollation = BinaryCollation,
                ColumnCollation = true,
            },
            mariaDb: mariaDb);

        // Act
        var generated = GenerateAndCompileModels(context);
        var snapshotDifference = CompareGenerated(context, generated.Snapshot.Model);
        var designerDifference = CompareGenerated(context, generated.Migration.TargetModel);

        // Assert
        Assert.False(snapshotDifference.HasDifferences);
        Assert.Empty(snapshotDifference.Operations);
        Assert.False(designerDifference.HasDifferences);
        Assert.Empty(designerDifference.Operations);
        Assert.Contains("UseCollation(\"utf8mb4_unicode_ci\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("HasCharSet(\"utf8mb4\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(BinaryCollation, generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultCollation(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultCharSet(", generated.DesignerCode, StringComparison.Ordinal);
    }

    /// <summary>The generated Up and Down source compiles with the exact old and new canonical defaults.</summary>
    [Fact]
    public void Generated_migration_preserves_default_transition_in_both_directions()
    {
        // Arrange
        using var source = CreateContext();
        using var target = CreateContext(defaultCollation: BinaryCollation);
        var up = Compare(source, target)
            .Operations;

        var down = Compare(target, source)
            .Operations;

        using var services = CreateCodeGenerationServices();
        var generator = services
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        // Act
        var code = generator.GenerateMigration("Doka.GeneratedTextDefaults", "TextDefaultTransition", up, down);
        var assembly = CompileSources(code);
        var migration = Assert.IsAssignableFrom<Migration>(Activator.CreateInstance(
            assembly.GetType("Doka.GeneratedTextDefaults.TextDefaultTransition", throwOnError: true)!));

        // Assert
        var upDatabase = Assert.Single(migration.UpOperations.OfType<AlterDatabaseOperation>());
        var downDatabase = Assert.Single(migration.DownOperations.OfType<AlterDatabaseOperation>());
        Assert.Equal(BinaryCollation, upDatabase.Collation);
        Assert.Equal(UnicodeCollation, upDatabase.OldDatabase.Collation);
        Assert.Equal(UnicodeCollation, downDatabase.Collation);
        Assert.Equal(BinaryCollation, downDatabase.OldDatabase.Collation);
        Assert.Contains("oldCollation: \"utf8mb4_unicode_ci\"", code, StringComparison.Ordinal);
    }

    /// <summary>The real pending-model check agrees with differ results for centralizing column metadata.</summary>
    /// <param name="guidColumn">Whether the changed property is a Char36 Guid.</param>
    /// <param name="reverse">Whether the transition makes the property explicit rather than inherited.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Pending_model_check_agrees_with_real_explicit_inherited_transition(
        bool guidColumn,
        bool reverse
    )
    {
        // Arrange
        var inherited = new TextShape { GuidColumn = guidColumn };
        var explicitColumn = inherited with { ColumnCollation = true };
        using var source = CreateContext(reverse
            ? inherited
            : explicitColumn);

        var sourceSnapshot = GenerateAndCompileModels(source);
        using var target = CreateContext(
            reverse
                ? explicitColumn
                : inherited,
            migrationsAssembly: sourceSnapshot.Assembly);

        // Act
        var difference = CompareGenerated(target, sourceSnapshot.Snapshot.Model);
        var pending = target.Database.HasPendingModelChanges();

        // Assert
        Assert.True(pending);
        Assert.True(difference.HasDifferences);
        Assert.Single(difference.Operations.OfType<AlterColumnOperation>());
    }

    /// <summary>Regenerating through normal snapshot tooling records the new intent without a permanent rest-diff.</summary>
    /// <param name="guidColumn">Whether the selected property is a Char36 Guid.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Normally_generated_target_snapshot_clears_pending_model_changes(
        bool guidColumn
    )
    {
        // Arrange
        var targetShape = new TextShape { GuidColumn = guidColumn };
        using var generationContext = CreateContext(targetShape);
        var targetSnapshot = GenerateAndCompileModels(generationContext);
        using var runtime = CreateContext(targetShape, migrationsAssembly: targetSnapshot.Assembly);

        // Act
        var difference = CompareGenerated(runtime, targetSnapshot.Snapshot.Model);
        var pending = runtime.Database.HasPendingModelChanges();

        // Assert
        Assert.False(pending);
        Assert.False(difference.HasDifferences);
        Assert.Empty(difference.Operations);
    }

    /// <summary>Unchanged explicit column intent is also a true no-op for EF's public pending-model check.</summary>
    /// <param name="guidColumn">Whether the explicitly configured property is a Char36 Guid.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unchanged_explicit_column_snapshot_has_no_pending_model_changes(
        bool guidColumn
    )
    {
        // Arrange
        var shape = new TextShape
        {
            GuidColumn = guidColumn,
            ColumnCollation = true,
        };

        using var generation = CreateContext(shape);
        var snapshot = GenerateAndCompileModels(generation);
        using var runtime = CreateContext(shape, migrationsAssembly: snapshot.Assembly);

        // Act
        var difference = CompareGenerated(runtime, snapshot.Snapshot.Model);
        var pending = runtime.Database.HasPendingModelChanges();

        // Assert
        Assert.False(pending);
        Assert.False(difference.HasDifferences);
        Assert.Empty(difference.Operations);
    }

    /// <summary>A changed runtime option remains a real model mismatch against a design-time snapshot.</summary>
    [Fact]
    public void Different_runtime_and_design_time_defaults_are_not_declared_equivalent()
    {
        // Arrange
        using var design = CreateContext();
        var snapshot = GenerateAndCompileModels(design);
        using var runtime = CreateContext(
            defaultCollation: BinaryCollation,
            migrationsAssembly: snapshot.Assembly);

        // Act
        var pending = runtime.Database.HasPendingModelChanges();
        var difference = CompareGenerated(runtime, snapshot.Snapshot.Model);

        // Assert
        Assert.True(pending);
        var alter = Assert.Single(difference.Operations.OfType<AlterDatabaseOperation>());
        Assert.Equal(UnicodeCollation, alter.OldDatabase.Collation);
        Assert.Equal(BinaryCollation, alter.Collation);
    }

    /// <summary>Compares a compiled generated model after the real EF runtime initializer attaches its relational model.</summary>
    private static TextDifference CompareGenerated(
        DbContext context,
        IModel generated
    )
    {
        var initialized = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(
                generated,
                designTime: true,
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>());

        var source = initialized.GetRelationalModel();
        var target = DesignModel(context)
            .GetRelationalModel();

        var differ = context.GetService<IMigrationsModelDiffer>();
        var operations = differ.GetDifferences(source, target);

        return new TextDifference(differ.HasDifferences(source, target), operations, GenerateSql(context, operations));
    }

    /// <summary>Uses the production generator for the snapshot, migration metadata and executable C# sources.</summary>
    private static GeneratedTextModels GenerateAndCompileModels(
        DefaultTextMigrationContext context
    )
    {
        var suffix = Guid
            .NewGuid()
            .ToString("N", CultureInfo.InvariantCulture);

        var snapshotName = "TextDefaultsSnapshot" + suffix;
        var migrationName = "TextDefaultsMigration" + suffix;
        const string generatedNamespace = "Doka.GeneratedTextDefaults";
        using var services = CreateCodeGenerationServices();
        var generator = services
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        var sourceModel = DesignModel(context);
        var snapshotCode = generator.GenerateSnapshot(generatedNamespace, context.GetType(), snapshotName, sourceModel);
        var migrationCode = generator.GenerateMigration(generatedNamespace, migrationName, [], []);
        var designerCode = generator.GenerateMetadata(
            generatedNamespace,
            context.GetType(),
            migrationName,
            "20261117000000_" + migrationName,
            sourceModel);

        var assembly = CompileSources(snapshotCode, migrationCode, designerCode);
        var snapshot = (ModelSnapshot)Activator.CreateInstance(
            assembly.GetType(generatedNamespace + "." + snapshotName, throwOnError: true)!)!;

        var migration = (Migration)Activator.CreateInstance(
            assembly.GetType(generatedNamespace + "." + migrationName, throwOnError: true)!)!;

        return new GeneratedTextModels(assembly, snapshotCode, designerCode, snapshot, migration);
    }

    /// <summary>Creates the existing provider design-time graph rather than a test-only generator.</summary>
    private static ServiceProvider CreateCodeGenerationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkDokaMySqlDesignTime();

        return services.BuildServiceProvider();
    }

    /// <summary>Compiles generated code against the exact runtime references already used by this test process.</summary>
    private static Assembly CompileSources(
        params string[] sources
    )
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var references = trusted
            .Concat(AppDomain
                .CurrentDomain
                .GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "Doka.GeneratedTextDefaults."
            + Guid
                .NewGuid()
                .ToString("N", CultureInfo.InvariantCulture),
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var errors = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(result.Success, string.Join(Environment.NewLine, errors));

        return Assembly.Load(stream.ToArray());
    }

    private sealed record GeneratedTextModels(
        Assembly Assembly,
        string SnapshotCode,
        string DesignerCode,
        ModelSnapshot Snapshot,
        Migration Migration
    );
}
