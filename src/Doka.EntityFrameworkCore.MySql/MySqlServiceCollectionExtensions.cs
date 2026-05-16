// File-local using: ICSharpRuntimeAnnotationCodeGenerator is registered via the Design.Internal
// service-pair below; the rest of this file's surface stays on the public EF Core API.
using Microsoft.EntityFrameworkCore.Design.Internal;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Registers the Doka MySQL provider runtime services.
/// </summary>
public static class MySqlServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Doka MySQL runtime provider services to the supplied collection.
    /// </summary>
    /// <param name="serviceCollection">The target service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddEntityFrameworkDokaMySql(
        this IServiceCollection serviceCollection
    )
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<IDatabaseProvider, DatabaseProvider<MySqlOptionsExtension>>()
            .TryAdd<ISingletonOptions, MySqlSingletonOptions>()
            .TryAdd<LoggingDefinitions, MySqlLoggingDefinitions>()
            .TryAdd<IRelationalConnection, MySqlRelationalConnection>()
            .TryAdd<IRelationalTransactionFactory, MySqlRelationalTransactionFactory>()
            .TryAdd<IRelationalDatabaseCreator, MySqlRelationalDatabaseCreator>()
            .TryAdd<IDatabaseCreator>(p => p.GetRequiredService<IRelationalDatabaseCreator>())
            .TryAdd<IExecutionStrategyFactory, MySqlExecutionStrategyFactory>()
            .TryAdd<IRelationalTypeMappingSource, MySqlTypeMappingSource>()
            .TryAdd<ITypeMappingSource>(p => p.GetRequiredService<IRelationalTypeMappingSource>())
            .TryAdd<IRelationalAnnotationProvider, MySqlRelationalAnnotationProvider>()
            .TryAdd<IMethodCallTranslatorProvider, MySqlMethodCallTranslatorProvider>()
            .TryAdd<IMemberTranslatorProvider, MySqlMemberTranslatorProvider>()
            .TryAdd<IAggregateMethodCallTranslatorPlugin, MySqlAggregateMethodCallTranslatorPlugin>()
            .TryAdd<IProviderConventionSetBuilder, MySqlConventionSetBuilder>()
            .TryAdd<IQuerySqlGeneratorFactory, MySqlQuerySqlGeneratorFactory>()
            .TryAdd<ISqlGenerationHelper, MySqlSqlGenerationHelper>()
            .TryAdd<IModelValidator, MySqlModelValidator>()
            .TryAdd<IMigrationsAnnotationProvider, MySqlMigrationsAnnotationProvider>()
            .TryAdd<IMigrationsSqlGenerator, MySqlMigrationsSqlGenerator>()
            .TryAdd<IHistoryRepository, MySqlHistoryRepository>()
            .TryAdd<IUpdateSqlGenerator, MySqlUpdateSqlGenerator>()
            .TryAdd<IModificationCommandBatchFactory, MySqlModificationCommandBatchFactory>()
            .TryAdd<IValueGeneratorSelector, MySqlValueGeneratorSelector>()
            .TryAddProviderSpecificServices(serviceCollectionMap => serviceCollectionMap
                .TryAddSingleton<IMySqlDriverFacade, MySqlConnectorDriverFacade>()
                .TryAddSingleton<IMySqlTransientExceptionDetector, MySqlTransientExceptionDetector>());

        builder.TryAddCoreServices();
#pragma warning disable EF1001 // IMigrationsModelDiffer is EF Core internal; wrapping is documented in ADR D-001.
        EfCoreServiceDecorator.Decorate<IMigrationsModelDiffer, MySqlMigrationsModelDiffer>(
            serviceCollection,
            (inner, _) => new MySqlMigrationsModelDiffer(inner));
#pragma warning restore EF1001

        return serviceCollection;
    }

    /// <summary>
    /// Adds the explicit design-time provider registration path.
    /// </summary>
    /// <param name="serviceCollection">The target design-time service collection.</param>
    /// <param name="configure">An optional reverse-engineering configuration callback.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddEntityFrameworkDokaMySqlDesignTime(
        this IServiceCollection serviceCollection,
        Action<MySqlReverseEngineeringOptionsBuilder>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        var reverseEngineeringOptions = new MySqlReverseEngineeringOptions();

        configure?.Invoke(new MySqlReverseEngineeringOptionsBuilder(reverseEngineeringOptions));

        // Design-time tooling intentionally composes the runtime graph instead of making runtime code
        // depend on tooling-only services or reflection-based registration paths.
        serviceCollection.AddEntityFrameworkDokaMySql();
        serviceCollection.TryAddSingleton(reverseEngineeringOptions);
        serviceCollection.TryAddSingleton<MySqlScaffoldingContext>();

        // EF Core 10's EntityFrameworkRelationalDesignServicesBuilder.TryAddCoreServices only
        // registers IAnnotationCodeGenerator + ICSharpRuntimeAnnotationCodeGenerator;
        // IModelCodeGenerator + the rest of the design-time core (CSharpModelGenerator,
        // ModelCodeGeneratorSelector, CSharpMigrationsGenerator, ...) live in
        // AddEntityFrameworkDesignTimeServices. The dotnet-ef tooling calls that helper
        // through DesignTimeServicesBuilder before invoking the provider; standalone
        // consumers (integration tests, custom scaffolders) skip the tooling path, so we
        // run the helper here to make this method self-contained.
        serviceCollection.AddEntityFrameworkDesignTimeServices();

#pragma warning disable EF1001
        var builder = new EntityFrameworkRelationalDesignServicesBuilder(serviceCollection)
            .TryAdd<IDatabaseModelFactory, MySqlDatabaseModelFactory>()
            .TryAdd<IAnnotationCodeGenerator, MySqlAnnotationCodeGenerator>()
            .TryAdd<ICSharpRuntimeAnnotationCodeGenerator, MySqlCSharpRuntimeAnnotationCodeGenerator>()
            .TryAdd<IProviderConfigurationCodeGenerator, MySqlProviderConfigurationCodeGenerator>();
#pragma warning restore EF1001

        builder.TryAddCoreServices();

        serviceCollection.Replace(ServiceDescriptor.Scoped<IScaffoldingModelFactory, MySqlScaffoldingModelFactory>());

#pragma warning disable EF1001 // IModelCodeGenerator is EF Core internal; wrapping is documented in ADR D-001.
        EfCoreServiceDecorator.Decorate<IModelCodeGenerator, MySqlModelCodeGenerator>(
            serviceCollection,
            (inner, _) => new MySqlModelCodeGenerator(inner));
#pragma warning restore EF1001

        return serviceCollection;
    }
}
