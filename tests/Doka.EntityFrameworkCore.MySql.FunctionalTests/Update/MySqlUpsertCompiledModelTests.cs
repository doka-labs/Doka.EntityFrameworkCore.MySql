namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies UPSERT against actual generated and compiled EF runtime-model source.</summary>
public sealed class MySqlUpsertCompiledModelTests
{
    /// <summary>Generated C# retains exact converted parameters, SQL defaults and computed-column omission.</summary>
    [Fact]
    public void Generated_model_binds_exact_converted_values_and_native_defaults()
    {
        // Arrange
        using var original = CreateContext(null, rejectRebuild: false, concurrency: false);
        var model = CompileModel(original);
        using var target = CreateContext(model, rejectRebuild: true, concurrency: false);
        var input = new ModelRow
        {
            Id = 1,
            Value = "Hallo 'Test' \\ \u4e2d",
            ClientGuid = new Guid("00112233-4455-6677-8899-aabbccddeeff"),
        };

        var shape = MySqlUpsertCommandBuilder.Create(target.Model.FindEntityType(typeof(ModelRow))!,
            new[] { nameof(ModelRow.Value) },
            target
                .GetService<IDbContextOptions>()
                .FindExtension<MySqlOptionsExtension>()!.ServerVersion!.Profile.Engine,
            target.GetService<ISqlGenerationHelper>());

        using var parameterFactory = new MySqlConnection().CreateCommand();
        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var batch = shape.BindNextBatch(target,
            parameterFactory,
            new[] { input },
            ref next,
            ref pending,
            CancellationToken.None);

        var command = shape.BuildCommand(batch,
            target.GetService<IRelationalCommandBuilderFactory>(),
            target.GetService<ISqlGenerationHelper>());

        // Assert
        Assert.Same(model, target.Model);
        Assert.Equal(0, target.ModelBuilds);
        Assert.Contains(batch.Rows.Single()
                .Parameters,
            parameter => Equals(parameter?.Value, "00112233-4455-6677-8899-aabbccddeeff"));

        Assert.Contains(batch.Rows.Single()
                .Parameters,
            parameter => Equals(parameter?.Value, input.Value));

        Assert.Contains("DEFAULT", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("`Computed`", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain(input.Value, command.CommandText, StringComparison.Ordinal);
        Assert.Equal(Guid.Empty, input.GeneratedGuid);
        Assert.Empty(target.ChangeTracker.Entries());
    }

    /// <summary>Unsupported concurrency metadata is still rejected after source generation and compilation.</summary>
    [Fact]
    public void Generated_model_does_not_bypass_concurrency_guards()
    {
        // Arrange
        using var original = CreateContext(null, rejectRebuild: false, concurrency: true);
        var model = CompileModel(original);
        using var target = CreateContext(model, rejectRebuild: true, concurrency: true);

        // Act
        var error = Record.Exception(() => MySqlUpsertCommandBuilder.Create(
            target.Model.FindEntityType(typeof(ModelRow))!,
            new[] { nameof(ModelRow.Value) },
            target
                .GetService<IDbContextOptions>()
                .FindExtension<MySqlOptionsExtension>()!.ServerVersion!.Profile.Engine,
            target.GetService<ISqlGenerationHelper>()));

        // Assert
        Assert.Contains("optimistic-concurrency",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Same(model, target.Model);
        Assert.Equal(0, target.ModelBuilds);
    }

    /// <summary>Creates an isolated model consumer for offline commands or the fixture-owned native schema.</summary>
    internal static ModelContext CreateContext(
        IModel? model,
        bool rejectRebuild,
        bool concurrency,
        string? connectionString = null,
        MySqlServerVersion? serverVersion = null
    )
    {
        var builder = MySqlFunctionalTestOptions
            .CreateTransientBuilder<ModelContext>()
            .UseMySql(connectionString ?? "Server=localhost;Database=unused;User ID=root;Password=unused;",
                serverVersion ?? MySqlServerVersion.MySql(new Version(8, 4, 13)));

        if (model is not null)
        {
            builder.UseModel(model);
        }

        return new ModelContext(builder.Options, rejectRebuild, concurrency);
    }

    /// <summary>Uses EF's existing in-memory compiler for actual generated runtime-model C#.</summary>
    internal static IModel CompileModel(
        ModelContext context
    )
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddEntityFrameworkDokaMySqlDesignTime()
            .BuildServiceProvider(validateScopes: true);

        var options = new CompiledModelCodeGenerationOptions
        {
            ContextType = typeof(ModelContext),
            ModelNamespace = "Doka.GeneratedUpsertModels",
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

    /// <summary>Exposes public CLR metadata without allowing the supplied runtime model to rebuild.</summary>
    public sealed class ModelContext : DbContext
    {
        private readonly bool _concurrency;
        private readonly bool _rejectRebuild;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        /// <param name="options">The isolated transient provider options.</param>
        /// <param name="rejectRebuild">Whether model creation is forbidden.</param>
        /// <param name="concurrency">Whether unsupported concurrency metadata is present.</param>
        public ModelContext(
            DbContextOptions<ModelContext> options,
            bool rejectRebuild,
            bool concurrency
        ) : base(options)
        {
            _rejectRebuild = rejectRebuild;
            _concurrency = concurrency;
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

            var row = modelBuilder.Entity<ModelRow>();
            row.ToTable("CompiledUpsert");
            row.HasKey(value => value.Id);
            row
                .Property(value => value.Id)
                .ValueGeneratedNever();

            row
                .Property(value => value.ClientGuid)
                .HasConversion<string>()
                .HasColumnType("char(36)");

            row
                .Property(value => value.GeneratedGuid)
                .HasColumnType("char(36)")
                .HasDefaultValueSql("UUID()");

            row
                .Property(value => value.Computed)
                .HasComputedColumnSql("`Id`+1", stored: true);

            row
                .Property(value => value.Value)
                .IsConcurrencyToken(_concurrency);
        }
    }

    /// <summary>Supplies public mapped members referenced by the generated runtime-model assembly.</summary>
    public sealed class ModelRow
    {
        /// <summary>Gets or sets the caller-supplied permanent key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the payload that must remain parameterized.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>Gets or sets the GUID converted to canonical textual storage.</summary>
        public Guid ClientGuid { get; set; }

        /// <summary>Gets or sets the sentinel for the server-generated GUID without readback.</summary>
        public Guid GeneratedGuid { get; set; }

        /// <summary>Gets or sets the computed member omitted from input writes.</summary>
        public int Computed { get; set; }
    }
}
