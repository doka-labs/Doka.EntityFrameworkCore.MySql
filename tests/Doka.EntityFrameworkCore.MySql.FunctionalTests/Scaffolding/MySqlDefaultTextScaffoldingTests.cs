namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies database text defaults and exceptions through actual reverse-engineering model/code replay.</summary>
public sealed class MySqlDefaultTextScaffoldingTests
{
    /// <summary>Database, table and column defaults remain distinct after compiling generated C#.</summary>
    /// <param name="mariaDb">Whether to scaffold MariaDB metadata.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scaffolded_database_table_and_column_collations_survive_generated_model_replay(
        bool mariaDb
    )
    {
        // Arrange
        var database = CreateDatabaseModel();
        using var services = CreateServices();
        using var scope = services.CreateScope();
        var state = services.GetRequiredService<MySqlScaffoldingContext>();
        state.Begin();
        state.SetDetectedServerVersionText(mariaDb
            ? "12.3.3-MariaDB"
            : "8.4.9");

        try
        {
            // Act
            var model = scope
                .ServiceProvider
                .GetRequiredService<IScaffoldingModelFactory>()
                .Create(database, new ModelReverseEngineerOptions());

            var scaffolded = GenerateModel(scope.ServiceProvider, model);
            using var replay = CompileScaffold(scaffolded);
            var replayModel = replay.GetService<IDesignTimeModel>()
                .Model;

            var table = replayModel
                .GetEntityTypes()
                .Single();

            // Assert
            Assert.Equal("utf8mb4_unicode_ci", model.GetCollation());
            Assert.Equal("utf8mb4_unicode_ci", replayModel.GetCollation());
            Assert.Equal("utf8mb4", replayModel.GetMySqlCharSet());
            Assert.Equal("utf8mb4_bin",
                table.FindAnnotation(RelationalAnnotationNames.Collation)
                    ?.Value);

            Assert.Equal("latin1_swedish_ci",
                table
                    .FindProperty("ExternalText")
                    ?.GetCollation());

            Assert.DoesNotContain("DefaultCollation(", scaffolded.ContextFile.Code, StringComparison.Ordinal);
            Assert.DoesNotContain("DefaultCharSet(", scaffolded.ContextFile.Code, StringComparison.Ordinal);
        }
        finally
        {
            state.Abort();
        }
    }

    /// <summary>A property name cannot turn ordinary text into the JSON type during reverse engineering.</summary>
    [Fact]
    public void Scaffolding_preserves_json_type_and_same_named_ordinary_text_as_different_contracts()
    {
        // Arrange
        var database = CreateDatabaseModel();
        using var services = CreateServices();
        using var scope = services.CreateScope();
        var state = services.GetRequiredService<MySqlScaffoldingContext>();
        state.Begin();
        state.SetDetectedServerVersionText("12.3.3-MariaDB");

        try
        {
            // Act
            var model = scope
                .ServiceProvider
                .GetRequiredService<IScaffoldingModelFactory>()
                .Create(database, new ModelReverseEngineerOptions());

            var scaffolded = GenerateModel(scope.ServiceProvider, model);
            using var replay = CompileScaffold(scaffolded);
            var entity = replay
                .GetService<IDesignTimeModel>()
                .Model
                .GetEntityTypes()
                .Single();

            // Assert
            Assert.Equal("json",
                entity
                    .FindProperty("Document")
                    ?.GetColumnType());

            Assert.Equal("utf8mb4_bin",
                entity
                    .FindProperty("Document")
                    ?.GetCollation());

            Assert.Equal("longtext",
                entity
                    .FindProperty("Json")
                    ?.GetColumnType());

            Assert.Equal("utf8mb4_unicode_ci",
                entity
                    .FindProperty("Json")
                    ?.GetCollation());

            Assert.Equal(typeof(string),
                entity.FindProperty("Document")
                    ?.ClrType);

            Assert.Equal(typeof(string),
                entity.FindProperty("Json")
                    ?.ClrType);
        }
        finally
        {
            state.Abort();
        }
    }

    /// <summary>Creates explicit physical facts without any connection or application default inference.</summary>
    private static DatabaseModel CreateDatabaseModel()
    {
        var database = new DatabaseModel
        {
            DatabaseName = "default_text_scaffold",
            Collation = "utf8mb4_unicode_ci",
        };

        database.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        var table = new DatabaseTable
        {
            Database = database,
            Name = "text_records",
        };

        table.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        table.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
        database.Tables.Add(table);
        var id = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            StoreType = "int",
            IsNullable = false,
        };

        table.Columns.Add(id);
        table.PrimaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PRIMARY",
            Columns = { id },
        };

        var external = new DatabaseColumn
        {
            Table = table,
            Name = "ExternalText",
            StoreType = "varchar(64)",
            IsNullable = false,
            Collation = "latin1_swedish_ci",
        };

        table.Columns.Add(external);
        table.Columns.Add(new DatabaseColumn
        {
            Table = table,
            Name = "Document",
            StoreType = "json",
            IsNullable = false,
            Collation = "utf8mb4_bin",
        });

        table.Columns.Add(new DatabaseColumn
        {
            Table = table,
            Name = "Json",
            StoreType = "longtext",
            IsNullable = false,
            Collation = "utf8mb4_unicode_ci",
        });

        return database;
    }

    /// <summary>Uses the existing provider design-time registration and scoped model factory.</summary>
    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkDokaMySqlDesignTime();

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Generates the normal provider context without inserting new application-option defaults.</summary>
    private static ScaffoldedModel GenerateModel(
        IServiceProvider services,
        IModel model
    )
    {
        var options = new ModelCodeGenerationOptions
        {
            ContextName = "TextScaffoldContext",
            ContextNamespace = "Doka.GeneratedTextScaffold",
            ModelNamespace = "Doka.GeneratedTextScaffold.Models",
            RootNamespace = "Doka.GeneratedTextScaffold",
            Language = "C#",
            ContextDir = "Generated",
            ProjectDir = "Generated",
            ConnectionString = "Server=127.0.0.1;Port=1;Database=default_text_offline;User ID=unused;Pooling=false;",
            SuppressConnectionStringWarning = true,
            UseNullableReferenceTypes = true,
        };

        return services
            .GetRequiredService<IModelCodeGeneratorSelector>()
            .Select(options)
            .GenerateModel(model, options);
    }

    /// <summary>Compiles all scaffolded sources and asks the actual generated context to rebuild its model.</summary>
    private static DbContext CompileScaffold(
        ScaffoldedModel scaffolded
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

        var sources = scaffolded
            .AdditionalFiles
            .Select(file => file.Code)
            .Prepend(scaffolded.ContextFile.Code)
            .Select(source => CSharpSyntaxTree.ParseText(source));

        var compilation = CSharpCompilation.Create(
            "Doka.GeneratedTextScaffold."
            + Guid
                .NewGuid()
                .ToString("N", CultureInfo.InvariantCulture),
            sources,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var errors = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(result.Success, string.Join(Environment.NewLine, errors));
        var assembly = Assembly.Load(stream.ToArray());

        return (DbContext)Activator.CreateInstance(
            assembly.GetType("Doka.GeneratedTextScaffold.TextScaffoldContext", throwOnError: true)!)!;
    }
}
