namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Creates the design-time service graph and code-generation options shared by
/// live reverse-engineering contracts.
/// </summary>
internal static class ScaffoldingTestServices
{
    /// <summary>
    /// Creates an isolated provider service graph for a live scaffolding pass.
    /// </summary>
    public static ServiceProvider CreateDesignTimeServiceProvider(
        bool includeSpatialServices = false
    )
    {
        var services = new ServiceCollection();

        services.AddEntityFrameworkDokaMySqlDesignTime();

        if (includeSpatialServices)
        {
            services.AddEntityFrameworkDokaMySqlNetTopologySuite();
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// Creates deterministic options for generated scaffolding source files.
    /// </summary>
    public static ModelCodeGenerationOptions CreateCodeGenerationOptions(
        string connectionString,
        string contextName = "CoreSchemaContext",
        bool suppressOnConfiguring = false
    ) => new()
    {
        ContextName = contextName,
        ContextNamespace = "Doka.Scaffolding",
        ModelNamespace = "Doka.Scaffolding.Models",
        RootNamespace = "Doka.Scaffolding",
        Language = "C#",
        ContextDir = "Generated",
        ProjectDir = "Generated",
        ConnectionString = connectionString,
        SuppressConnectionStringWarning = true,
        SuppressOnConfiguring = suppressOnConfiguring,
        UseNullableReferenceTypes = true,
    };
}
