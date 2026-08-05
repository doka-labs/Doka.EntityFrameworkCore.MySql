namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Completes provider temporal metadata before EF Core finalizes the relational model.
/// </summary>
/// <remarks>
/// Temporal state belongs to the physical table contract. The convention therefore
/// propagates it across TPH mappings and convention-owned many-to-many join entities,
/// while leaving explicit conflicting configuration for the validator to reject.
/// </remarks>
internal sealed class MySqlTemporalConvention
    : IEntityTypeAnnotationChangedConvention,
        ISkipNavigationForeignKeyChangedConvention,
        IModelFinalizingConvention
{
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlTemporalConvention(
        IEnumerable<ISingletonOptions> singletonOptions
    )
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _singletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    public void ProcessEntityTypeAnnotationChanged(
        IConventionEntityTypeBuilder entityTypeBuilder,
        string name,
        IConventionAnnotation? annotation,
        IConventionAnnotation? oldAnnotation,
        IConventionContext<IConventionAnnotation> context
    )
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(context);

        if (name == MySqlAnnotationNames.IsTemporal)
        {
            if (annotation?.Value as bool? == true)
            {
                if (entityTypeBuilder.Metadata.GetMySqlTemporalPeriodStartPropertyName() is null)
                {
                    ((IMutableEntityType)entityTypeBuilder.Metadata).SetMySqlTemporalPeriodStartPropertyName(
                        MySqlTemporalMetadata.DefaultPeriodStartPropertyName);
                }

                if (entityTypeBuilder.Metadata.GetMySqlTemporalPeriodEndPropertyName() is null)
                {
                    ((IMutableEntityType)entityTypeBuilder.Metadata).SetMySqlTemporalPeriodEndPropertyName(
                        MySqlTemporalMetadata.DefaultPeriodEndPropertyName);
                }
            }
            else
            {
                ((IMutableEntityType)entityTypeBuilder.Metadata).SetMySqlTemporalPeriodStartPropertyName(null);
                ((IMutableEntityType)entityTypeBuilder.Metadata).SetMySqlTemporalPeriodEndPropertyName(null);
            }
        }

        if (name is MySqlAnnotationNames.TemporalPeriodStartPropertyName
            or MySqlAnnotationNames.TemporalPeriodEndPropertyName)
        {
            if (oldAnnotation?.Value is string oldPropertyName)
            {
                var oldProperty = entityTypeBuilder.Metadata.FindProperty(oldPropertyName);

                if (oldProperty is not null)
                {
                    entityTypeBuilder.RemoveUnusedImplicitProperties([oldProperty]);
                }
            }

            if (annotation?.Value is string propertyName)
            {
                // Set the conventional column name as soon as the period property is
                // declared. Relational model finalization can then share that physical
                // column without uniquifying it, while explicit user configuration keeps
                // its higher configuration-source priority.
                entityTypeBuilder
                    .Property(typeof(DateTime), propertyName)
                    ?.HasColumnName(propertyName);
            }
        }
    }

    public void ProcessSkipNavigationForeignKeyChanged(
        IConventionSkipNavigationBuilder skipNavigationBuilder,
        IConventionForeignKey? foreignKey,
        IConventionForeignKey? oldForeignKey,
        IConventionContext<IConventionForeignKey> context
    )
    {
        ArgumentNullException.ThrowIfNull(skipNavigationBuilder);
        ArgumentNullException.ThrowIfNull(context);

        var skipNavigation = skipNavigationBuilder.Metadata;
        var joinEntityType = skipNavigation.JoinEntityType;

        if (!skipNavigation.DeclaringEntityType.IsMySqlTemporal()
            || skipNavigation.Inverse is not { } inverse
            || !inverse.DeclaringEntityType.IsMySqlTemporal()
            || joinEntityType is null
            || joinEntityType.IsMySqlTemporal()
            || !joinEntityType.HasSharedClrType
            || joinEntityType.GetConfigurationSource() != ConfigurationSource.Convention)
        {
            return;
        }

        ((IMutableEntityType)joinEntityType).SetMySqlTemporal(true);
    }

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        var profile = _singletonOptions.Profile
            ?? throw new InvalidOperationException("The MySQL provider profile has not been initialized.");
        var support = profile.GetSupport(ProviderCapability.TemporalTables);

        PropagateTemporalMappingToSingleTableHierarchies(modelBuilder.Metadata);
        PropagateTemporalMappingToImplicitJoinEntities(modelBuilder.Metadata);

        foreach (var entityType in modelBuilder
                     .Metadata.GetEntityTypes()
                     .ToArray())
        {
            if (!entityType.IsMySqlTemporal())
            {
                continue;
            }

            ConfigureTemporalEntityType((IMutableEntityType)entityType, support);
        }
    }

    private static void PropagateTemporalMappingToSingleTableHierarchies(
        IConventionModel model
    )
    {
        foreach (var rootEntityType in model
                     .GetEntityTypes()
                     .Where(entityType => entityType.BaseType is null && entityType.IsMySqlTemporal()))
        {
            var tableName = rootEntityType.GetTableName();
            var schema = rootEntityType.GetSchema();

            if (tableName is null)
            {
                continue;
            }

            foreach (var derivedEntityType in rootEntityType.GetDerivedTypes())
            {
                if (!string.Equals(derivedEntityType.GetTableName(), tableName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(derivedEntityType.GetSchema(), schema, StringComparison.OrdinalIgnoreCase)
                    || derivedEntityType.FindAnnotation(MySqlAnnotationNames.IsTemporal) is not null)
                {
                    continue;
                }

                // A TPH hierarchy represents one physical row and therefore one temporal
                // contract. An explicit annotation on a derived type is never overwritten,
                // so conflicting user configuration still reaches the model validator.
                CopyTemporalMapping(rootEntityType, (IMutableEntityType)derivedEntityType);
            }
        }
    }

    private static void CopyTemporalMapping(
        IConventionEntityType source,
        IMutableEntityType target
    )
    {
        target.SetMySqlTemporal(true);

        if (target.GetMySqlTemporalHistoryTableName() is null)
        {
            target.SetMySqlTemporalHistoryTableName(source.GetMySqlTemporalHistoryTableName());
        }

        if (target.GetMySqlTemporalHistoryTableSchema() is null)
        {
            target.SetMySqlTemporalHistoryTableSchema(source.GetMySqlTemporalHistoryTableSchema());
        }

        if (target.GetMySqlTemporalPeriodStartPropertyName() is null)
        {
            target.SetMySqlTemporalPeriodStartPropertyName(source.GetMySqlTemporalPeriodStartPropertyName());
        }

        if (target.GetMySqlTemporalPeriodEndPropertyName() is null)
        {
            target.SetMySqlTemporalPeriodEndPropertyName(source.GetMySqlTemporalPeriodEndPropertyName());
        }
    }

    private static void PropagateTemporalMappingToImplicitJoinEntities(
        IConventionModel model
    )
    {
        foreach (var skipNavigation in model
                     .GetEntityTypes()
                     .SelectMany(entityType => entityType.GetSkipNavigations()))
        {
            var inverse = skipNavigation.Inverse;
            var joinEntityType = skipNavigation.JoinEntityType;

            if (!skipNavigation.DeclaringEntityType.IsMySqlTemporal()
                || inverse is null
                || !inverse.DeclaringEntityType.IsMySqlTemporal()
                || joinEntityType is null
                || joinEntityType.IsMySqlTemporal()
                || !joinEntityType.HasSharedClrType
                || joinEntityType.GetConfigurationSource() != ConfigurationSource.Convention)
            {
                continue;
            }

            // The convention-created many-to-many row belongs to the relationship between
            // two temporal entities. Keeping it current-only would make historical graph
            // traversal incomplete even though both public endpoints are temporal.
            ((IMutableEntityType)joinEntityType).SetMySqlTemporal(true);
        }
    }

    private static void ConfigureTemporalEntityType(
        IMutableEntityType entityType,
        ProviderSupportStatus support
    )
    {
        var tableName = entityType.GetTableName();

        if (support == ProviderSupportStatus.Emulated
            && !string.IsNullOrWhiteSpace(tableName)
            && entityType.GetMySqlTemporalHistoryTableName() is null)
        {
            entityType.SetMySqlTemporalHistoryTableName(MySqlTemporalMetadata.CreateDefaultHistoryTableName(tableName));
        }

        if (support == ProviderSupportStatus.Emulated
            && entityType.GetMySqlTemporalHistoryTableSchema() is null
            && entityType.GetSchema() is { } schema)
        {
            entityType.SetMySqlTemporalHistoryTableSchema(schema);
        }

        var periodStartPropertyName = entityType.GetMySqlTemporalPeriodStartPropertyName()
            ?? MySqlTemporalMetadata.DefaultPeriodStartPropertyName;
        var periodEndPropertyName = entityType.GetMySqlTemporalPeriodEndPropertyName()
            ?? MySqlTemporalMetadata.DefaultPeriodEndPropertyName;

        entityType.SetMySqlTemporalPeriodStartPropertyName(periodStartPropertyName);
        entityType.SetMySqlTemporalPeriodEndPropertyName(periodEndPropertyName);

        ConfigurePeriodProperty(entityType, periodStartPropertyName, support);
        ConfigurePeriodProperty(entityType, periodEndPropertyName, support);
    }

    private static void ConfigurePeriodProperty(
        IMutableEntityType entityType,
        string propertyName,
        ProviderSupportStatus support
    )
    {
        var property = entityType.FindProperty(propertyName) ?? entityType.AddProperty(propertyName, typeof(DateTime));

        // Native MariaDB period columns require TIMESTAMP, whereas the MySQL
        // history-trigger contract uses DATETIME to avoid session time-zone
        // conversion when copying the generated UTC boundaries.
        property.SetColumnType(support == ProviderSupportStatus.Native ? "timestamp(6)" : "datetime(6)");

        property.ValueGenerated = ValueGenerated.OnAddOrUpdate;
    }
}
