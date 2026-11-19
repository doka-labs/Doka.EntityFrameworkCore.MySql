namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies physical CTE text evidence in actual generated and compiled runtime-model C#.</summary>
public sealed class MySqlCteCompiledModelTests
{
    /// <summary>Generated models preserve physical collation without allowing model rebuilding or design annotation leakage.</summary>
    [Fact]
    public void Generated_model_preserves_compatible_recursive_copy_and_rejects_incompatible_copy()
    {
        // Arrange
        using var original = CreateContext(null, rejectRebuild: false);
        var model = CompileModel(original);
        using var target = CreateContext(model, rejectRebuild: true);
        var entity = model.FindEntityType(typeof(TextRow))!;
        var source = target
            .Set<TextRow>()
            .Where(row => row.Id == 1)
            .Select(row => row.First);

        var positive = source.WithRecursiveCte(frontier => frontier.SelectMany(_ => target
                .Set<TextRow>()
                .Select(row => row.Second)),
            rows => rows,
            1);

        var negative = source.WithRecursiveCte(frontier => frontier.SelectMany(_ => target
                .Set<TextRow>()
                .Select(row => row.Latin)),
            rows => rows,
            1);

        // Act
        var sql = positive.ToQueryString();
        var failure = Record.Exception(() => negative.ToQueryString());

        // Assert
        Assert.Contains("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Same(model, target.Model);
        Assert.Equal(0, target.ModelBuilds);
        Assert.Equal("utf8mb4_unicode_ci",
            entity.FindProperty(nameof(TextRow.First))!
                .FindAnnotation(MySqlRuntimeColumnTextFacets.RuntimeCollation)
                ?.Value);

        Assert.Equal(string.Empty,
            entity.FindProperty(nameof(TextRow.Second))!
                .FindAnnotation(MySqlRuntimeColumnTextFacets.RuntimeCollation)
                ?.Value);

        Assert.Equal("utf8mb4_bin",
            entity.FindProperty(nameof(TextRow.Override))!
                .FindOverrides(StoreObjectIdentifier.Table("CompiledCteText"))!
                .FindAnnotation(MySqlRuntimeColumnTextFacets.RuntimeCollation)
                ?.Value);

        Assert.Null(original
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(TextRow))!
            .FindProperty(nameof(TextRow.First))!.FindAnnotation(MySqlRuntimeColumnTextFacets.RuntimeCollation));
    }

    private static TextContext CreateContext(
        IModel? model,
        bool rejectRebuild
    )
    {
        var builder = MySqlFunctionalTestOptions
            .CreateTransientBuilder<TextContext>()
            .UseMySql("Server=localhost;Database=unused;User ID=unused;",
                MySqlServerVersion.MySql(new Version(8, 4, 13)));

        if (model is not null)
        {
            builder.UseModel(model);
        }

        return new TextContext(builder.Options, rejectRebuild);
    }

    /// <summary>Executes EF's existing model generator and in-memory C# compiler rather than simulating generated annotations.</summary>
    private static IModel CompileModel(
        TextContext context
    )
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddEntityFrameworkDokaMySqlDesignTime()
            .BuildServiceProvider(validateScopes: true);

        var options = new CompiledModelCodeGenerationOptions
        {
            ContextType = typeof(TextContext),
            ModelNamespace = "Doka.GeneratedCteTextModel",
            Language = "C#",
            UseNullableReferenceTypes = true,
        };

        var files = services
            .GetRequiredService<ICompiledModelCodeGeneratorSelector>()
            .Select(options)
            .GenerateModel(context.GetService<IDesignTimeModel>()
                    .Model,
                options);

        var build = new BuildSource
        {
            NullableReferenceTypes = true,
            Sources = files.ToDictionary(file => file.Path, file => file.Code, StringComparer.Ordinal),
        };

        var platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var assemblyPaths = platformAssemblies
            .Concat(AppDomain
                .CurrentDomain
                .GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in assemblyPaths)
        {
            build.References.Add(BuildReference.ByPath(path));
        }

        var generated = build
            .BuildInMemory()
            .GetTypes()
            .Single(type => type.IsSubclassOf(typeof(RuntimeModel)));

        return (IModel)generated.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    }

    /// <summary>Exposes only public mapped CLR metadata to the actual generated assembly.</summary>
    public sealed class TextContext : DbContext
    {
        private readonly bool _rejectRebuild;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public TextContext(
            DbContextOptions<TextContext> options,
            bool rejectRebuild
        ) : base(options)
        {
            _rejectRebuild = rejectRebuild;
        }

        internal int ModelBuilds { get; private set; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ModelBuilds++;

            if (_rejectRebuild)
            {
                throw new InvalidOperationException("The supplied compiled model must not be rebuilt.");
            }

            modelBuilder.HasCharSet("utf8mb4");
            modelBuilder.UseCollation("utf8mb4_unicode_ci");
            var row = modelBuilder.Entity<TextRow>();
            row.ToTable("CompiledCteText");
            row
                .Property(value => value.First)
                .HasMaxLength(80)
                .UseCollation("utf8mb4_unicode_ci");

            row
                .Property(value => value.Second)
                .HasMaxLength(80);

            row
                .Property(value => value.Latin)
                .HasMaxLength(80)
                .UseCollation("latin1_swedish_ci");

            row
                .Property(value => value.Override)
                .HasMaxLength(80)
                .Metadata
                .GetOrCreateOverrides(StoreObjectIdentifier.Table("CompiledCteText"))
                .SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
        }
    }

    /// <summary>Supplies public text members referenced by generated model source.</summary>
    public sealed class TextRow
    {
        /// <summary>Gets or sets the physical key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets an explicitly collated anchor.</summary>
        public string First { get; set; } = string.Empty;

        /// <summary>Gets or sets a column inheriting compatible model facets.</summary>
        public string Second { get; set; } = string.Empty;

        /// <summary>Gets or sets an incompatible physical text column.</summary>
        public string Latin { get; set; } = string.Empty;

        /// <summary>Gets or sets the table-specific explicit collation fixture.</summary>
        public string Override { get; set; } = string.Empty;
    }
}
