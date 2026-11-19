namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies real compiled-model generation and the supplied runtime-model contract.</summary>
public sealed partial class MySqlDefaultTextModelTests
{
    /// <summary>A generated, compiled runtime model retains the exact default and override contract.</summary>
    [Fact]
    public void Generated_compiled_model_accepts_matching_defaults_without_rebuilding()
    {
        // Arrange
        using var source = CreateContext("utf8mb4", "utf8mb4_unicode_ci");
        var model = CompileModel(source);
        using var target = CreateContext("utf8mb4", "utf8mb4_unicode_ci", supplied: model);

        // Act
        var sql = target
            .Set<TextRow>()
            .Where(row => row.Name == "sample")
            .ToQueryString();

        // Assert
        Assert.Equal(0, target.ModelBuilds);
        Assert.Contains("`Name`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Compiled models cannot silently preserve defaults from a different application configuration.</summary>
    [Fact]
    public void Generated_compiled_model_rejects_changed_defaults()
    {
        // Arrange
        using var source = CreateContext("utf8mb4", "utf8mb4_unicode_ci");
        var model = CompileModel(source);
        var annotations = model
            .GetAnnotations()
            .Select(annotation => (annotation.Name, annotation.Value))
            .ToArray();

        void act()
        {
            using var target = CreateContext("utf8mb4", "utf8mb4_bin", supplied: model);

            _ = target.Model;
        }

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(annotations,
            model
                .GetAnnotations()
                .Select(annotation => (annotation.Name, annotation.Value))
                .ToArray());
    }

    /// <summary>Explicit design-time model facets remain stronger after compiled-model generation.</summary>
    [Fact]
    public void Generated_compiled_model_keeps_explicit_override_priority()
    {
        // Arrange
        using var source = CreateContext("utf8mb4", "utf8mb4_unicode_ci", "latin1", "latin1_swedish_ci");
        var model = CompileModel(source);
        using var target = CreateContext("utf8mb4", "utf8mb4_bin", supplied: model);

        // Act
        var sql = target
            .Set<LatinRow>()
            .ToQueryString();

        // Assert
        Assert.Equal(0, target.ModelBuilds);
        Assert.Contains("LatinRow", sql, StringComparison.Ordinal);
    }

    /// <summary>Models with no text defaults receive no unrelated runtime receipt annotations.</summary>
    [Fact]
    public void Ordinary_models_without_text_defaults_keep_their_runtime_annotation_shape()
    {
        // Arrange
        using var source = CreateContext(null, null);

        // Act
        var annotations = source
            .Model
            .GetAnnotations()
            .Select(annotation => annotation.Name)
            .ToArray();

        // Assert
        Assert.DoesNotContain(annotations,
            name => name.StartsWith(MySqlAnnotationNames.Prefix + "RuntimeText", StringComparison.Ordinal));

        Assert.DoesNotContain(annotations,
            name => name.StartsWith(MySqlAnnotationNames.Prefix + "RuntimeExplicitText", StringComparison.Ordinal));
    }

    /// <summary>Uses EF's existing BuildSource compiler for generated runtime-model code.</summary>
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
            ModelNamespace = "Doka.GeneratedTextDefaultModels",
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

        var generatedType = build
            .BuildInMemory()
            .GetTypes()
            .Single(type => type.IsSubclassOf(typeof(RuntimeModel)));

        return (IModel)generatedType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
    }
}
