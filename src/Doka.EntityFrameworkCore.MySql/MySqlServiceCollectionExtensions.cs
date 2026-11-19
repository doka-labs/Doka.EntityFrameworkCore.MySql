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
    /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>

    // WHY: Preserve the public extension metadata and XML member layout; C#14 blocks add exported marker types.
    // ReSharper disable once ConvertToExtensionBlock
    public static IServiceCollection AddEntityFrameworkDokaMySql(
        this IServiceCollection serviceCollection
    )
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEvaluatableExpressionFilterPlugin, MySqlEvaluatableExpressionFilterPlugin>());

        var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<IDatabaseProvider, DatabaseProvider<MySqlOptionsExtension>>()
            .TryAdd<ISingletonOptions, MySqlSingletonOptions>()
            .TryAdd<LoggingDefinitions, MySqlLoggingDefinitions>()
            .TryAdd<IRelationalConnection, MySqlRelationalConnection>()
            .TryAdd<IRelationalCommandBuilderFactory, MySqlRelationalCommandBuilderFactory>()
            .TryAdd<IRelationalTransactionFactory, MySqlRelationalTransactionFactory>()
            .TryAdd<IRelationalDatabaseCreator, MySqlRelationalDatabaseCreator>()
            .TryAdd<IDatabaseCreator>(p => p.GetRequiredService<IRelationalDatabaseCreator>())
            .TryAdd<IExecutionStrategyFactory, MySqlExecutionStrategyFactory>()
            .TryAdd<IInterceptor, MySqlParameterSizeCommandInterceptor>()
            .TryAdd<IInterceptor, MySqlMigrationWarningCommandInterceptor>()
            .TryAdd<IRelationalTypeMappingSource, MySqlTypeMappingSource>()
            .TryAdd<ITypeMappingSource>(p => p.GetRequiredService<IRelationalTypeMappingSource>())
            .TryAdd<IRelationalAnnotationProvider, MySqlRelationalAnnotationProvider>()
            .TryAdd<IMethodCallTranslatorProvider, MySqlMethodCallTranslatorProvider>()
            .TryAdd<IMemberTranslatorProvider, MySqlMemberTranslatorProvider>()
            .TryAdd<IAggregateMethodCallTranslatorPlugin, MySqlAggregateMethodCallTranslatorPlugin>()
            .TryAdd<IProviderConventionSetBuilder, MySqlConventionSetBuilder>()
            .TryAdd<IQueryTranslationPreprocessorFactory, MySqlQueryTranslationPreprocessorFactory>()
            .TryAdd<IQueryCompilationContextFactory, MySqlQueryCompilationContextFactory>()
            .TryAdd<IShapedQueryCompilingExpressionVisitorFactory, MySqlShapedQueryCompilingExpressionVisitorFactory>()
            .TryAdd<INavigationExpansionExtensibilityHelper,
                MySqlNavigationExpansionExtensibilityHelper>()
            .TryAdd<IQuerySqlGeneratorFactory, MySqlQuerySqlGeneratorFactory>()
            .TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory,
                MySqlQueryableMethodTranslatingExpressionVisitorFactory>()
            .TryAdd<IRelationalSqlTranslatingExpressionVisitorFactory,
                MySqlSqlTranslatingExpressionVisitorFactory>()
            .TryAdd<IQueryTranslationPostprocessorFactory, MySqlQueryTranslationPostprocessorFactory>()
            .TryAdd<IRelationalParameterBasedSqlProcessorFactory, MySqlParameterBasedSqlProcessorFactory>()
            .TryAdd<ISqlGenerationHelper, MySqlSqlGenerationHelper>()
            .TryAdd<IModelValidator, MySqlModelValidator>()
            .TryAdd<IMigrationsAnnotationProvider, MySqlMigrationsAnnotationProvider>()
            .TryAdd<IMigrationsSqlGenerator, MySqlMigrationsSqlGenerator>()
            .TryAdd<IHistoryRepository, MySqlHistoryRepository>()
            .TryAdd<IUpdateSqlGenerator, MySqlUpdateSqlGenerator>()
            .TryAdd<IModificationCommandFactory, MySqlModificationCommandFactory>()
            .TryAdd<IModificationCommandBatchFactory, MySqlModificationCommandBatchFactory>()
            .TryAdd<IValueGeneratorSelector, MySqlValueGeneratorSelector>()
            .TryAddProviderSpecificServices(serviceCollectionMap => serviceCollectionMap
                .TryAddSingleton<IMySqlDriverFacade, MySqlConnectorDriverFacade>()
                .TryAddSingleton<IMySqlTransientExceptionDetector, MySqlTransientExceptionDetector>());

#pragma warning disable EF1001 // EF10 rewrites DbSet arguments before provider preprocessing; this adapter only prepares the compiled INSERT root.
        builder.TryAdd<IQueryCompiler, MySqlQueryCompiler>();
#pragma warning restore EF1001
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
    /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
    /// <remarks>
    /// The root provider owns the design-time logger factory across operation scopes.
    /// Register a custom <see cref="ILoggerFactory" /> afterward to override that factory;
    /// externally supplied instances retain caller ownership under normal DI rules.
    /// </remarks>
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
        // WHY: EF10 tooling registers its reporter instance before provider services.
        // Reuse that exact reporter so the repeated helper does not capture a new,
        // silent default reporter in its logger-factory closure.
#pragma warning disable EF1001 // EF10 exposes the configured design-time reporter only through this tooling contract.
        IOperationReporter? designTimeReporter = null;

        // WHY: Only the last unkeyed registration determines ordinary DI resolution;
        // a keyed service or an earlier shadowed reporter must not change that result.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = serviceCollection.Count - 1; index >= 0; index--)
        {
            var descriptor = serviceCollection[index];

            if (!descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(IOperationReporter))
            {
                designTimeReporter = descriptor.ImplementationInstance as IOperationReporter;

                break;
            }
        }

        var designTimeServiceStart = serviceCollection.Count;
        serviceCollection.AddEntityFrameworkDesignTimeServices(designTimeReporter);
#pragma warning restore EF1001

        // WHY: EF10 creates one LoggerFactory and returns it from a scoped factory.
        // A completed scope would dispose the shared instance while another scaffold
        // operation still needs it. Preserve EF's exact factory, but let the root own it.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = designTimeServiceStart; index < serviceCollection.Count; index++)
        {
            var descriptor = serviceCollection[index];

            if (!descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(ILoggerFactory)
                && descriptor is { Lifetime: ServiceLifetime.Scoped, ImplementationFactory: { } factory })
            {
                serviceCollection[index] = ServiceDescriptor.Singleton(typeof(ILoggerFactory), factory);
            }
        }

        serviceCollection.Replace(
            ServiceDescriptor.Singleton<ICSharpSnapshotGenerator, MySqlCSharpSnapshotGenerator>());

#pragma warning disable EF1001
        var builder = new EntityFrameworkRelationalDesignServicesBuilder(serviceCollection)
            .TryAdd<IDatabaseModelFactory, MySqlDatabaseModelFactory>()
            .TryAdd<IAnnotationCodeGenerator, MySqlAnnotationCodeGenerator>()
            .TryAdd<ICSharpRuntimeAnnotationCodeGenerator, MySqlCSharpRuntimeAnnotationCodeGenerator>()
            .TryAdd<IProviderConfigurationCodeGenerator, MySqlProviderConfigurationCodeGenerator>();
#pragma warning restore EF1001

        builder.TryAddCoreServices();

        serviceCollection.Replace(
            ServiceDescriptor.Singleton<IMigrationsCodeGenerator, MySqlCSharpMigrationsGenerator>());

        serviceCollection.Replace(ServiceDescriptor.Scoped<IScaffoldingModelFactory, MySqlScaffoldingModelFactory>());

#pragma warning disable EF1001 // IModelCodeGenerator is EF Core internal; wrapping is documented in ADR D-001.
        EfCoreServiceDecorator.Decorate<IModelCodeGenerator, MySqlModelCodeGenerator>(
            serviceCollection,
            (inner, serviceProvider) => new MySqlModelCodeGenerator(
                inner,
                serviceProvider.GetRequiredService<ICSharpHelper>()));

        EfCoreServiceDecorator.Decorate<IReverseEngineerScaffolder, MySqlReverseEngineerScaffolder>(
            serviceCollection,
            (inner, serviceProvider) => new MySqlReverseEngineerScaffolder(
                inner,
                serviceProvider.GetRequiredService<MySqlScaffoldingContext>()));
#pragma warning restore EF1001

        return serviceCollection;
    }
}
