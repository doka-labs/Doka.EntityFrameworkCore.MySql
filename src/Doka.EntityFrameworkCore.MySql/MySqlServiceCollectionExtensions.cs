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
        var existingMigrationsModelDifferDescriptor =
            serviceCollection.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IMigrationsModelDiffer));

        if (existingMigrationsModelDifferDescriptor is not null)
        {
            serviceCollection.Replace(
                ServiceDescriptor.Describe(
                    typeof(IMigrationsModelDiffer),
                    serviceProvider => new MySqlMigrationsModelDiffer(
                        CreateInnerService<IMigrationsModelDiffer>(
                            existingMigrationsModelDifferDescriptor,
                            serviceProvider)),
                    existingMigrationsModelDifferDescriptor.Lifetime));
        }

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
        serviceCollection.TryAddSingleton<MySqlScaffoldingState>();

#pragma warning disable EF1001
        var builder = new EntityFrameworkRelationalDesignServicesBuilder(serviceCollection)
            .TryAdd<IDatabaseModelFactory, MySqlDatabaseModelFactory>()
            .TryAdd<IAnnotationCodeGenerator, MySqlAnnotationCodeGenerator>()
            .TryAdd<ICSharpRuntimeAnnotationCodeGenerator, MySqlCSharpRuntimeAnnotationCodeGenerator>()
            .TryAdd<IProviderConfigurationCodeGenerator, MySqlProviderConfigurationCodeGenerator>();
#pragma warning restore EF1001

        builder.TryAddCoreServices();

        serviceCollection.Replace(ServiceDescriptor.Scoped<IScaffoldingModelFactory, MySqlScaffoldingModelFactory>());

        var existingModelCodeGeneratorDescriptor =
            serviceCollection.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IModelCodeGenerator));

        if (existingModelCodeGeneratorDescriptor is not null)
        {
            serviceCollection.Replace(
                ServiceDescriptor.Singleton<IModelCodeGenerator>(serviceProvider => new MySqlModelCodeGenerator(
                    CreateInnerService<IModelCodeGenerator>(existingModelCodeGeneratorDescriptor, serviceProvider))));
        }

        return serviceCollection;
    }

    private static TService CreateInnerService<TService>(
        ServiceDescriptor descriptor,
        IServiceProvider serviceProvider
    )
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (descriptor.ImplementationInstance is TService implementationInstance)
        {
            return implementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (TService)descriptor.ImplementationFactory(serviceProvider);
        }

        if (descriptor.ImplementationType is not null)
        {
            return (TService)ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType);
        }

        throw new InvalidOperationException(
            $"The existing {typeof(TService).Name} registration did not expose an instantiable implementation.");
    }
}
