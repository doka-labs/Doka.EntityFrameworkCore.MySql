namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Validates application-time periods, constraints, and shared-table contracts.</summary>
internal sealed partial class MySqlModelValidator
{
    private void ValidateApplicationTimeTables(
        IModel model,
        ILogger logger
    )
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            ValidateApplicationTimeConstraintOwnership(entityType, logger);
        }

        var applicationTimeEntityTypes = model
            .GetEntityTypes()
            .Where(entityType => entityType.IsMySqlApplicationTime())
            .ToArray();

        if (applicationTimeEntityTypes.Length == 0)
        {
            return;
        }

        var profile = _singletonOptions.Profile
            ?? throw new InvalidOperationException("The MySQL provider profile has not been initialized.");

        if (!profile.Supports(ProviderCapability.ApplicationTimePeriods))
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Engine '{profile.Engine.Family} {profile.Engine.Version}' does not support application-time periods.");
        }

        foreach (var entityType in applicationTimeEntityTypes)
        {
            ValidateApplicationTimeEntityType(entityType, profile, logger);
        }

        ValidateSharedApplicationTimeTables(model, logger);
    }

    private static void ValidateApplicationTimeEntityType(
        IReadOnlyEntityType entityType,
        ProviderProfile profile,
        ILogger logger
    )
    {
        var tableName = entityType.GetTableName();

        if (string.IsNullOrWhiteSpace(tableName))
        {
            ThrowInvalidApplicationTime(logger, $"Entity type '{entityType.DisplayName()}' is not mapped to a table.");
        }

        if (entityType.GetViewName() is not null)
        {
            ThrowInvalidApplicationTime(logger, $"Entity type '{entityType.DisplayName()}' is mapped to a view.");
        }

        var periodName = entityType.GetMySqlApplicationTimePeriodName();
        var periodStartPropertyName = entityType.GetMySqlApplicationTimePeriodStartPropertyName();
        var periodEndPropertyName = entityType.GetMySqlApplicationTimePeriodEndPropertyName();

        ValidateApplicationTimeName(periodName, "period", entityType, logger);
        ValidateApplicationTimeName(periodStartPropertyName, "period-start property", entityType, logger);
        ValidateApplicationTimeName(periodEndPropertyName, "period-end property", entityType, logger);

        if (string.Equals(periodStartPropertyName, periodEndPropertyName, StringComparison.Ordinal))
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Entity type '{entityType.DisplayName()}' must use distinct application-time period properties.");
        }

        var storeObject = StoreObjectIdentifier.Table(tableName!, entityType.GetSchema());
        var periodStartProperty = ValidateApplicationTimePeriodProperty(
            entityType,
            periodStartPropertyName!,
            "start",
            logger);

        var periodEndProperty = ValidateApplicationTimePeriodProperty(
            entityType,
            periodEndPropertyName!,
            "end",
            logger);

        var periodStartColumnName = periodStartProperty.GetColumnName(storeObject);
        var periodEndColumnName = periodEndProperty.GetColumnName(storeObject);

        if (string.IsNullOrWhiteSpace(periodStartColumnName)
            || string.IsNullOrWhiteSpace(periodEndColumnName))
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Entity type '{entityType.DisplayName()}' must map both application-time boundaries to its table.");
        }

        if (string.Equals(periodStartColumnName, periodEndColumnName, StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Entity type '{entityType.DisplayName()}' must use distinct application-time period columns.");
        }

        if (entityType.GetMySqlApplicationTimeWithoutOverlaps())
        {
            if (!profile.Engine.Has(EngineCapability.ApplicationTimeWithoutOverlaps))
            {
                ThrowInvalidApplicationTime(
                    logger,
                    $"Engine '{profile.Engine.Family} {profile.Engine.Version}' does not support WITHOUT OVERLAPS.");
            }

            if (entityType.FindPrimaryKey() is null)
            {
                ThrowInvalidApplicationTime(
                    logger,
                    $"Entity type '{entityType.DisplayName()}' requires a primary key before WITHOUT OVERLAPS can be used.");
            }
        }

        ValidateApplicationTimeConstraints(
            entityType,
            periodStartPropertyName!,
            periodEndPropertyName!,
            profile,
            logger);

        if (entityType.IsMySqlTemporal()
            && !profile.Supports(ProviderCapability.BitemporalTables))
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Engine '{profile.Engine.Family} {profile.Engine.Version}' cannot supply a bitemporal table.");
        }
    }

    private static void ValidateApplicationTimeConstraintOwnership(
        IReadOnlyEntityType entityType,
        ILogger logger
    )
    {
        var hasApplicationTimeConstraint = entityType
                .GetKeys()
                .Any(key => key.FindAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps)?.Value is true)
            || entityType
                .GetIndexes()
                .Any(index => index.GetMySqlApplicationTimeWithoutOverlaps());

        if (hasApplicationTimeConstraint && !entityType.IsMySqlApplicationTime())
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Entity type '{entityType.DisplayName()}' configures WITHOUT OVERLAPS without an "
                + "application-time period.");
        }
    }

    private static void ValidateApplicationTimeConstraints(
        IReadOnlyEntityType entityType,
        string periodStartPropertyName,
        string periodEndPropertyName,
        ProviderProfile profile,
        ILogger logger
    )
    {
        var keys = entityType
            .GetKeys()
            .Where(key => key.FindAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps)
                ?.Value is true)
            .ToArray();

        var indexes = entityType
            .GetIndexes()
            .Where(index => index.GetMySqlApplicationTimeWithoutOverlaps())
            .ToArray();

        if (keys.Length + indexes.Length == 0)
        {
            return;
        }

        if (!profile.Engine.Has(EngineCapability.ApplicationTimeWithoutOverlaps))
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Engine '{profile.Engine.Family} {profile.Engine.Version}' does not support WITHOUT OVERLAPS.");
        }

        foreach (var key in keys)
        {
            ValidateApplicationTimeConstraintProperties(
                entityType,
                key.Properties,
                periodStartPropertyName,
                periodEndPropertyName,
                "key",
                logger);
        }

        foreach (var index in indexes)
        {
            if (!index.IsUnique)
            {
                ThrowInvalidApplicationTime(
                    logger,
                    $"Application-time index '{index.Name}' on entity type '{entityType.DisplayName()}' "
                    + "must be unique before WITHOUT OVERLAPS can be used.");
            }

            if (index.GetMySqlSpatialIndex()
                || index.GetMySqlFullTextIndex()
                || index.GetMySqlIndexPrefixLengths()?.Any(prefixLength => prefixLength > 0) == true)
            {
                ThrowInvalidApplicationTime(
                    logger,
                    $"Application-time index '{index.Name}' on entity type '{entityType.DisplayName()}' cannot "
                    + "combine WITHOUT OVERLAPS with a spatial, full-text, or prefix index.");
            }

            ValidateApplicationTimeConstraintProperties(
                entityType,
                index.Properties,
                periodStartPropertyName,
                periodEndPropertyName,
                "index",
                logger);
        }
    }

    private static void ValidateApplicationTimeConstraintProperties(
        IReadOnlyEntityType entityType,
        IReadOnlyList<IReadOnlyPropertyBase> properties,
        string periodStartPropertyName,
        string periodEndPropertyName,
        string constraintKind,
        ILogger logger
    )
    {
        if (properties.Any(property => property.Name == periodStartPropertyName
                || property.Name == periodEndPropertyName))
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Application-time {constraintKind} on entity type '{entityType.DisplayName()}' must not list "
                + "a period boundary explicitly because MariaDB appends the period through WITHOUT OVERLAPS.");
        }
    }

    private static IReadOnlyProperty ValidateApplicationTimePeriodProperty(
        IReadOnlyEntityType entityType,
        string propertyName,
        string boundary,
        ILogger logger
    )
    {
        var property = entityType.FindProperty(propertyName);

        if (property is null)
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Entity type '{entityType.DisplayName()}' has no application-time period-{boundary} "
                + $"property named '{propertyName}'.");
        }

        if (property!.ClrType != typeof(DateTime)
            || property.IsNullable)
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Application-time period-{boundary} property "
                + $"'{entityType.DisplayName()}.{propertyName}' must be a non-nullable DateTime property.");
        }

        if (property.ValueGenerated != ValueGenerated.Never)
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Application-time period-{boundary} property "
                + $"'{entityType.DisplayName()}.{propertyName}' must be supplied by the application.");
        }

        if (property.GetComputedColumnSql() is not null)
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Application-time period-{boundary} property "
                + $"'{entityType.DisplayName()}.{propertyName}' cannot map to a generated column.");
        }

        return property;
    }

    private static void ValidateSharedApplicationTimeTables(
        IModel model,
        ILogger logger
    )
    {
        foreach (var tableGroup in model
                     .GetEntityTypes()
                     .Where(entityType => entityType.GetTableName() is not null)
                     .GroupBy(
                         entityType => (entityType.GetSchema(), entityType.GetTableName()),
                         StringTupleComparer.OrdinalIgnoreCase))
        {
            var entityTypes = tableGroup.ToArray();
            var applicationTimeEntityTypes = entityTypes
                .Where(entityType => entityType.IsMySqlApplicationTime())
                .ToArray();

            if (applicationTimeEntityTypes.Length == 0)
            {
                continue;
            }

            if (applicationTimeEntityTypes.Length != entityTypes.Length)
            {
                ThrowInvalidApplicationTime(
                    logger,
                    $"Every entity type sharing table '{tableGroup.Key.Item2}' must use the same "
                    + "application-time mapping.");
            }

            var referenceEntityType = applicationTimeEntityTypes[0];
            var referenceContract = CreateApplicationTimeSharedTableContract(referenceEntityType);

            foreach (var entityType in applicationTimeEntityTypes.Skip(1))
            {
                var contract = CreateApplicationTimeSharedTableContract(entityType);

                if (!ApplicationTimeSharedTableContractEquals(referenceContract, contract))
                {
                    ThrowInvalidApplicationTime(
                        logger,
                        $"Entity types sharing application-time table '{tableGroup.Key.Item2}' must use the same "
                        + "period name, boundary columns, and WITHOUT OVERLAPS setting.");
                }
            }
        }
    }

    private static ApplicationTimeSharedTableContract CreateApplicationTimeSharedTableContract(
        IReadOnlyEntityType entityType
    )
    {
        var storeObject = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());

        return new ApplicationTimeSharedTableContract(
            entityType.GetMySqlApplicationTimePeriodName()!,
            entityType.FindProperty(entityType.GetMySqlApplicationTimePeriodStartPropertyName()!)!.GetColumnName(
                storeObject)!,
            entityType.FindProperty(entityType.GetMySqlApplicationTimePeriodEndPropertyName()!)!.GetColumnName(
                storeObject)!,
            entityType.GetMySqlApplicationTimeWithoutOverlaps()
            || entityType
                .FindPrimaryKey()
                ?.FindAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps)
                ?.Value is true);
    }

    private static bool ApplicationTimeSharedTableContractEquals(
        ApplicationTimeSharedTableContract left,
        ApplicationTimeSharedTableContract right
    ) => string.Equals(left.PeriodName, right.PeriodName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.PeriodStartColumnName, right.PeriodStartColumnName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.PeriodEndColumnName, right.PeriodEndColumnName, StringComparison.OrdinalIgnoreCase)
        && left.WithoutOverlaps == right.WithoutOverlaps;

    private static void ValidateApplicationTimeName(
        string? name,
        string role,
        IReadOnlyEntityType entityType,
        ILogger logger
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ThrowInvalidApplicationTime(
                logger,
                $"Entity type '{entityType.DisplayName()}' has no application-time {role} name.");
        }

        if (name!.Length > MySqlConventionSetBuilder.MaxIdentifierLength)
        {
            ThrowInvalidApplicationTime(
                logger,
                $"The application-time {role} name '{name}' on entity type "
                + $"'{entityType.DisplayName()}' exceeds the "
                + $"{MySqlConventionSetBuilder.MaxIdentifierLength}-character engine limit.");
        }
    }

    private static void ThrowInvalidApplicationTime(
        ILogger logger,
        string reason
    )
    {
        MySqlLoggerMessages.InvalidConfiguration(
            logger,
            MySqlConfigurationFailureReason.ApplicationTimeInvalid,
            "ModelValidation");

        throw new InvalidOperationException("Invalid MariaDB application-time mapping: " + reason);
    }

    private sealed record ApplicationTimeSharedTableContract(
        string PeriodName,
        string PeriodStartColumnName,
        string PeriodEndColumnName,
        bool WithoutOverlaps
    );
}
