namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Completes provider temporal metadata before EF Core finalizes the relational model.
/// </summary>
/// <remarks>
/// Temporal state belongs to the physical table contract. The convention therefore
/// propagates it across inheritance mappings and convention-owned many-to-many join
/// entities, while leaving explicit conflicting configuration for the validator to reject.
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

        PropagateTemporalMappingToSharedOwnedEntityTypes(modelBuilder.Metadata);
        PropagateTemporalMappingToHierarchies(modelBuilder.Metadata);
        NormalizeTemporalTptBaseLinks(modelBuilder.Metadata);
        PropagateTemporalMappingToImplicitJoinEntities(modelBuilder.Metadata);
        NormalizeApplicationTimePrimaryKeys(modelBuilder.Metadata);

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

    private static void NormalizeApplicationTimePrimaryKeys(
        IConventionModel model
    )
    {
        foreach (var entityType in model
                     .GetEntityTypes()
                     .Where(entityType => entityType.IsMySqlApplicationTime()
                         && entityType.GetMySqlApplicationTimeWithoutOverlaps()))
        {
            entityType
                .FindPrimaryKey()
                ?.SetAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps, true);
        }
    }

    private static void PropagateTemporalMappingToSharedOwnedEntityTypes(
        IConventionModel model
    )
    {
        foreach (var ownedEntityType in model
                     .GetEntityTypes()
                     .Where(entityType => entityType.IsOwned()
                         && !entityType.IsMySqlTemporal()
                         && entityType.FindAnnotation(MySqlAnnotationNames.IsTemporal) is null))
        {
            var temporalOwner = FindTemporalOwnerSharingTable(ownedEntityType);

            if (temporalOwner is null)
            {
                continue;
            }

            CopySharedOwnedTemporalMapping(temporalOwner, ownedEntityType);
        }
    }

    private static IConventionEntityType? FindTemporalOwnerSharingTable(
        IConventionEntityType ownedEntityType
    )
    {
        var owner = ownedEntityType.FindOwnership()?.PrincipalEntityType;

        while (owner is not null
               && SharesTable(owner, ownedEntityType))
        {
            if (owner.IsMySqlTemporal())
            {
                return owner;
            }

            owner = owner.FindOwnership()?.PrincipalEntityType;
        }

        return null;
    }

    private static bool SharesTable(
        IReadOnlyEntityType left,
        IReadOnlyEntityType right
    ) => left.GetTableName() is { } tableName
        && string.Equals(tableName, right.GetTableName(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.GetSchema(), right.GetSchema(), StringComparison.OrdinalIgnoreCase);

    private static void CopySharedOwnedTemporalMapping(
        IConventionEntityType source,
        IConventionEntityType target
    )
    {
        var targetBuilder = target.Builder;
        var periodStartPropertyName = source.GetMySqlTemporalPeriodStartPropertyName()
            ?? MySqlTemporalMetadata.DefaultPeriodStartPropertyName;

        var periodEndPropertyName = source.GetMySqlTemporalPeriodEndPropertyName()
            ?? MySqlTemporalMetadata.DefaultPeriodEndPropertyName;

        targetBuilder.HasAnnotation(MySqlAnnotationNames.IsTemporal, true, fromDataAnnotation: false);
        CopyConventionAnnotation(source, targetBuilder, MySqlAnnotationNames.TemporalHistoryTableName);
        CopyConventionAnnotation(source, targetBuilder, MySqlAnnotationNames.TemporalHistoryTableSchema);

        targetBuilder.HasAnnotation(
            MySqlAnnotationNames.TemporalPeriodStartPropertyName,
            periodStartPropertyName,
            fromDataAnnotation: false);
        targetBuilder.HasAnnotation(
            MySqlAnnotationNames.TemporalPeriodEndPropertyName,
            periodEndPropertyName,
            fromDataAnnotation: false);

        CopySharedPeriodColumn(source, targetBuilder, periodStartPropertyName);
        CopySharedPeriodColumn(source, targetBuilder, periodEndPropertyName);
    }

    private static void CopyConventionAnnotation(
        IConventionEntityType source,
        IConventionEntityTypeBuilder targetBuilder,
        string annotationName
    )
    {
        if (source.FindAnnotation(annotationName)?.Value is { } value)
        {
            targetBuilder.HasAnnotation(annotationName, value, fromDataAnnotation: false);
        }
    }

    private static void CopySharedPeriodColumn(
        IConventionEntityType source,
        IConventionEntityTypeBuilder targetBuilder,
        string propertyName
    )
    {
        var tableName = source.GetTableName()!;
        var storeObject = StoreObjectIdentifier.Table(tableName, source.GetSchema());
        var columnName = source
                .FindProperty(propertyName)
                ?.GetColumnName(storeObject)
            ?? propertyName;

        var property = targetBuilder.Property(typeof(DateTime), propertyName)
            ?.Metadata;

        property?.SetColumnName(columnName, storeObject, fromDataAnnotation: false);
    }

    private static void NormalizeTemporalTptBaseLinks(
        IConventionModel model
    )
    {
        foreach (var entityType in model
                     .GetEntityTypes()
                     .Where(entityType => entityType.IsMySqlTemporal()))
        {
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                var sharesPrincipalTable = string.Equals(
                        entityType.GetTableName(),
                        foreignKey.PrincipalEntityType.GetTableName(),
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        entityType.GetSchema(),
                        foreignKey.PrincipalEntityType.GetSchema(),
                        StringComparison.OrdinalIgnoreCase);

                if (!foreignKey.IsBaseLinking()
                    || sharesPrincipalTable
                    || foreignKey.GetDeleteBehaviorConfigurationSource() != ConfigurationSource.Convention)
                {
                    continue;
                }

                // EF's conventional TPT base link normally cascades in the database. InnoDB
                // does not activate table triggers for cascaded foreign-key actions. That
                // default can therefore bypass emulated history and makes native history
                // depend on an engine-specific cascade. NoAction preserves EF's explicit
                // derived-before-base ordering across both engines. An explicit cascade
                // remains visible to model validation instead of being silently rewritten.
                foreignKey.SetDeleteBehavior(DeleteBehavior.NoAction, fromDataAnnotation: false);
            }
        }
    }

    private static void PropagateTemporalMappingToHierarchies(
        IConventionModel model
    )
    {
        foreach (var rootEntityType in model
                     .GetEntityTypes()
                     .Where(entityType => entityType.BaseType is null))
        {
            var hierarchy = rootEntityType
                .GetDerivedTypesInclusive()
                .ToArray();

            var temporalSource = hierarchy.FirstOrDefault(entityType => entityType.IsMySqlTemporal());

            if (temporalSource is null)
            {
                continue;
            }

            foreach (var hierarchyEntityType in hierarchy)
            {
                var tableName = hierarchyEntityType.GetTableName();

                if (tableName is null
                    || hierarchyEntityType.FindAnnotation(MySqlAnnotationNames.IsTemporal) is not null)
                {
                    continue;
                }

                var sharesSourceTable =
                    string.Equals(tableName, temporalSource.GetTableName(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        hierarchyEntityType.GetSchema(),
                        temporalSource.GetSchema(),
                        StringComparison.OrdinalIgnoreCase);

                CopyTemporalMapping(temporalSource, (IMutableEntityType)hierarchyEntityType, sharesSourceTable);
            }
        }
    }

    private static void CopyTemporalMapping(
        IConventionEntityType source,
        IMutableEntityType target,
        bool sharesSourceTable
    )
    {
        target.SetMySqlTemporal(true);

        if (sharesSourceTable && target.GetMySqlTemporalHistoryTableName() is null)
        {
            target.SetMySqlTemporalHistoryTableName(source.GetMySqlTemporalHistoryTableName());
        }

        if (sharesSourceTable && target.GetMySqlTemporalHistoryTableSchema() is null)
        {
            target.SetMySqlTemporalHistoryTableSchema(source.GetMySqlTemporalHistoryTableSchema());
        }

        if (sharesSourceTable)
        {
            if (target.GetMySqlTemporalPeriodStartPropertyName() is null)
            {
                target.SetMySqlTemporalPeriodStartPropertyName(source.GetMySqlTemporalPeriodStartPropertyName());
            }

            if (target.GetMySqlTemporalPeriodEndPropertyName() is null)
            {
                target.SetMySqlTemporalPeriodEndPropertyName(source.GetMySqlTemporalPeriodEndPropertyName());
            }

            return;
        }

        // A TPT row spans multiple physical tables. Each table must therefore own a
        // separate period contract even though EF represents it as one entity hierarchy.
        // TPC siblings also receive local metadata so every union branch is independently
        // valid and can be queried without depending on another concrete table.
        var tableName = target.GetTableName()!;
        target.SetMySqlTemporalPeriodStartPropertyName(
            MySqlTemporalMetadata.CreateHierarchyPeriodPropertyName(tableName, isStart: true));
        target.SetMySqlTemporalPeriodEndPropertyName(
            MySqlTemporalMetadata.CreateHierarchyPeriodPropertyName(tableName, isStart: false));
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

        if (entityType.GetTableName() is { } tableName)
        {
            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var configuredColumnName = property.GetColumnName(storeObject);

            if (configuredColumnName is null
                || string.Equals(configuredColumnName, propertyName, StringComparison.Ordinal))
            {
                var columnName = propertyName.StartsWith(
                        MySqlTemporalMetadata.DefaultPeriodStartPropertyName + "_",
                        StringComparison.Ordinal)
                    ? MySqlTemporalMetadata.DefaultPeriodStartPropertyName
                    : propertyName.StartsWith(
                        MySqlTemporalMetadata.DefaultPeriodEndPropertyName + "_",
                        StringComparison.Ordinal)
                        ? MySqlTemporalMetadata.DefaultPeriodEndPropertyName
                        : propertyName;

                property.SetColumnName(columnName, storeObject);
            }
        }

        // Native MariaDB period columns require TIMESTAMP, whereas the MySQL
        // history-trigger contract uses DATETIME to avoid session time-zone
        // conversion when copying the generated UTC boundaries.
        property.SetColumnType(support == ProviderSupportStatus.Native ? "timestamp(6)" : "datetime(6)");

        property.ValueGenerated = ValueGenerated.OnAddOrUpdate;
    }
}
