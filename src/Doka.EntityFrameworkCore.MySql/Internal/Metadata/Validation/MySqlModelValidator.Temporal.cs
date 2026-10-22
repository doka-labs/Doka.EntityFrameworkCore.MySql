namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Validates temporal-table mappings, shared-table contracts, and engine restrictions.
/// </summary>
internal sealed partial class MySqlModelValidator
{
    private void ValidateTemporalTables(
        IModel model,
        ILogger logger
    )
    {
        var temporalEntityTypes = model
            .GetEntityTypes()
            .Where(entityType => entityType.IsMySqlTemporal())
            .ToArray();

        if (temporalEntityTypes.Length == 0)
        {
            return;
        }

        var profile = _singletonOptions.Profile
            ?? throw new InvalidOperationException("The MySQL provider profile has not been initialized.");

        var support = profile.GetSupport(ProviderCapability.TemporalTables);

        if (!profile.Supports(ProviderCapability.TemporalTables))
        {
            ThrowInvalidTemporalTable(
                logger,
                $"Engine '{profile.Engine.Family} {profile.Engine.Version}' cannot supply temporal tables.");
        }

        foreach (var entityType in temporalEntityTypes)
        {
            ValidateTemporalEntityType(entityType, support, logger);
        }

        ValidateSharedTemporalTables(model, logger);
    }

    private static void ValidateTemporalEntityType(
        IReadOnlyEntityType entityType,
        ProviderSupportStatus support,
        ILogger logger
    )
    {
        var tableName = entityType.GetTableName();

        if (string.IsNullOrWhiteSpace(tableName))
        {
            ThrowInvalidTemporalTable(logger, $"Entity type '{entityType.DisplayName()}' is not mapped to a table.");
        }

        if (entityType.GetViewName() is not null)
        {
            ThrowInvalidTemporalTable(logger, $"Entity type '{entityType.DisplayName()}' is mapped to a view.");
        }

        var historyTableName = entityType.GetMySqlTemporalHistoryTableName();
        var historyTableSchema = entityType.GetMySqlTemporalHistoryTableSchema();
        var periodStartPropertyName = entityType.GetMySqlTemporalPeriodStartPropertyName();
        var periodEndPropertyName = entityType.GetMySqlTemporalPeriodEndPropertyName();

        if (support == ProviderSupportStatus.Emulated)
        {
            ValidateTemporalName(historyTableName, "history table", entityType, logger);
        }

        ValidateTemporalName(periodStartPropertyName, "period-start property", entityType, logger);
        ValidateTemporalName(periodEndPropertyName, "period-end property", entityType, logger);

        if (support == ProviderSupportStatus.Emulated
            && string.Equals(tableName, historyTableName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entityType.GetSchema(), historyTableSchema, StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalidTemporalTable(
                logger,
                $"Temporal table '{tableName}' must not use itself as its history table.");
        }

        if (string.Equals(periodStartPropertyName, periodEndPropertyName, StringComparison.Ordinal))
        {
            ThrowInvalidTemporalTable(
                logger,
                $"Entity type '{entityType.DisplayName()}' must use distinct temporal period properties.");
        }

        var storeObject = StoreObjectIdentifier.Table(tableName!, entityType.GetSchema());
        var periodStartProperty = ValidatePeriodProperty(entityType, periodStartPropertyName!, "start", logger);
        var periodEndProperty = ValidatePeriodProperty(entityType, periodEndPropertyName!, "end", logger);
        var periodStartColumnName = periodStartProperty.GetColumnName(storeObject);
        var periodEndColumnName = periodEndProperty.GetColumnName(storeObject);

        if (string.Equals(periodStartColumnName, periodEndColumnName, StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalidTemporalTable(
                logger,
                $"Entity type '{entityType.DisplayName()}' must use distinct temporal period columns.");
        }

        if (support == ProviderSupportStatus.Emulated)
        {
            ValidateEmulatedTemporalStorageEngine(entityType, logger);
            ValidateEmulatedTemporalForeignKeys(entityType, logger);
        }
        else
        {
            ValidateNativeTemporalGeneratedColumns(entityType, logger);
        }
    }

    private static void ValidateEmulatedTemporalStorageEngine(
        IReadOnlyEntityType entityType,
        ILogger logger
    )
    {
        var storageEngine = entityType.GetMySqlStorageEngine();

        if (storageEngine is not null
            && !string.Equals(storageEngine, "InnoDB", StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalidTemporalTable(
                logger,
                $"MySQL temporal table '{entityType.GetTableName()}' must use InnoDB. "
                + $"Storage engine '{storageEngine}' cannot make the current-row change and history-trigger "
                + "write one atomic transaction.");
        }
    }

    private static void ValidateEmulatedTemporalForeignKeys(
        IReadOnlyEntityType entityType,
        ILogger logger
    )
    {
        foreach (var foreignKey in entityType.GetForeignKeys())
        {
            if (foreignKey.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.SetNull))
            {
                continue;
            }

            if (foreignKey.IsOwnership
                && string.Equals(
                    entityType.GetTableName(),
                    foreignKey.PrincipalEntityType.GetTableName(),
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    entityType.GetSchema(),
                    foreignKey.PrincipalEntityType.GetSchema(),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ThrowInvalidTemporalTable(
                logger,
                $"MySQL temporal entity type '{entityType.DisplayName()}' cannot use database delete behavior "
                + $"'{foreignKey.DeleteBehavior}'. MySQL cascaded foreign-key actions do not activate triggers, "
                + "so the affected temporal row could be changed without a corresponding history record. "
                + "Use an explicit application-side change or a non-cascading database constraint.");
        }
    }

    private static void ValidateNativeTemporalGeneratedColumns(
        IReadOnlyEntityType entityType,
        ILogger logger
    )
    {
        foreach (var property in GetPropertiesIncludingComplexTypes(entityType))
        {
            if (property.GetComputedColumnSql() is null)
            {
                continue;
            }

            ThrowInvalidTemporalTable(
                logger,
                $"MariaDB temporal property '{entityType.DisplayName()}.{property.Name}' maps to a generated "
                + "column. MariaDB generated columns cannot be system-versioned.");
        }
    }

    private static void ValidateTemporalName(
        string? name,
        string role,
        IReadOnlyEntityType entityType,
        ILogger logger
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ThrowInvalidTemporalTable(logger, $"Entity type '{entityType.DisplayName()}' has no {role} name.");
        }

        if (name!.Length > MySqlConventionSetBuilder.MaxIdentifierLength)
        {
            ThrowInvalidTemporalTable(
                logger,
                $"The {role} name '{name}' on entity type '{entityType.DisplayName()}' exceeds the "
                + $"{MySqlConventionSetBuilder.MaxIdentifierLength}-character engine limit.");
        }
    }

    private static IReadOnlyProperty ValidatePeriodProperty(
        IReadOnlyEntityType entityType,
        string propertyName,
        string boundary,
        ILogger logger
    )
    {
        var property = entityType.FindProperty(propertyName);

        if (property is null)
        {
            ThrowInvalidTemporalTable(
                logger,
                $"Entity type '{entityType.DisplayName()}' has no temporal period-{boundary} "
                + $"property named '{propertyName}'.");
        }

        if (property!.ClrType != typeof(DateTime)
            || property.IsNullable)
        {
            ThrowInvalidTemporalTable(
                logger,
                $"Temporal period-{boundary} property '{entityType.DisplayName()}.{propertyName}' "
                + "must be a non-nullable DateTime property.");
        }

        if (property.ValueGenerated != ValueGenerated.OnAddOrUpdate)
        {
            ThrowInvalidTemporalTable(
                logger,
                $"Temporal period-{boundary} property '{entityType.DisplayName()}.{propertyName}' "
                + "must be generated on add or update.");
        }

        return property;
    }

    private static void ValidateSharedTemporalTables(
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

            if (entityTypes.Length < 2)
            {
                continue;
            }

            var temporalEntityTypes = entityTypes
                .Where(entityType => entityType.IsMySqlTemporal())
                .ToArray();

            if (temporalEntityTypes.Length == 0)
            {
                continue;
            }

            if (temporalEntityTypes.Length != entityTypes.Length)
            {
                ThrowInvalidTemporalTable(
                    logger,
                    $"Every entity type sharing table '{tableGroup.Key.Item2}' must use the same temporal mapping.");
            }

            var referenceEntityType = temporalEntityTypes[0];
            var referenceContract = CreateSharedTableContract(referenceEntityType);

            foreach (var entityType in temporalEntityTypes.Skip(1))
            {
                var contract = CreateSharedTableContract(entityType);

                if (!TemporalSharedTableContractEquals(referenceContract, contract))
                {
                    ThrowInvalidTemporalTable(
                        logger,
                        $"Entity types sharing temporal table '{tableGroup.Key.Item2}' must use the same "
                        + "history table and period columns. "
                        + $"'{referenceEntityType.DisplayName()}' uses {FormatTemporalSharedTableContract(referenceContract)}; "
                        + $"'{entityType.DisplayName()}' uses {FormatTemporalSharedTableContract(contract)}.");
                }
            }
        }
    }

    private static TemporalSharedTableContract CreateSharedTableContract(
        IReadOnlyEntityType entityType
    )
    {
        var tableName = entityType.GetTableName()!;
        var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        var periodStartProperty = entityType.FindProperty(entityType.GetMySqlTemporalPeriodStartPropertyName()!);
        var periodEndProperty = entityType.FindProperty(entityType.GetMySqlTemporalPeriodEndPropertyName()!);

        return new TemporalSharedTableContract(
            entityType.GetMySqlTemporalHistoryTableName(),
            entityType.GetMySqlTemporalHistoryTableSchema(),
            periodStartProperty!.GetColumnName(storeObject)!,
            periodEndProperty!.GetColumnName(storeObject)!);
    }

    private static bool TemporalSharedTableContractEquals(
        TemporalSharedTableContract left,
        TemporalSharedTableContract right
    ) => string.Equals(left.HistoryTableName, right.HistoryTableName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.HistoryTableSchema, right.HistoryTableSchema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.PeriodStartColumnName, right.PeriodStartColumnName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.PeriodEndColumnName, right.PeriodEndColumnName, StringComparison.OrdinalIgnoreCase);

    private static string FormatTemporalSharedTableContract(
        TemporalSharedTableContract contract
    ) => $"history '{contract.HistoryTableSchema ?? "<default>"}.{contract.HistoryTableName ?? "<native>"}', "
        + $"period start '{contract.PeriodStartColumnName}', period end '{contract.PeriodEndColumnName}'";

    private static void ThrowInvalidTemporalTable(
        ILogger logger,
        string reason
    )
    {
        MySqlLoggerMessages.InvalidConfiguration(
            logger,
            MySqlConfigurationFailureReason.TemporalTableInvalid,
            "ModelValidation");

        throw new InvalidOperationException("Invalid MySQL temporal-table mapping: " + reason);
    }

    private sealed record TemporalSharedTableContract(
        string? HistoryTableName,
        string? HistoryTableSchema,
        string PeriodStartColumnName,
        string PeriodEndColumnName
    );
}
