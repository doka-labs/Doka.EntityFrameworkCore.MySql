namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlModelValidator : RelationalModelValidator
{
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlModelValidator(
        ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies, relationalDependencies)
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _singletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    public override void Validate(
        IModel model,
        IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(logger);

        base.Validate(model, logger);

        // EF supplies this scoped logger for the active model-validation
        // operation. A singleton validator must not retain a logger created
        // from another context's options.
        var modelValidationLogger = logger.Logger;

        ValidateSequenceSchema(model, modelValidationLogger);
        ValidateKeyedAndIndexedPropertyLengths(model, modelValidationLogger);
        ValidateDecimalPrecision(model, modelValidationLogger);
        ValidateConstraintNameLengths(model, modelValidationLogger);
        ValidateSpatialIndexes(model, modelValidationLogger);
        ValidateTemporalTables(model, modelValidationLogger);
    }

    private static void ValidateSequenceSchema(
        IModel model,
        ILogger logger
    )
    {
        const string remediation = "Remove the configured schema; MySQL treats schema and database as synonyms.";

        foreach (var sequence in model.GetSequences())
        {
            if (!string.IsNullOrWhiteSpace(sequence.Schema))
            {
                MySqlLoggerMessages.SchemaUnsupported(
                    logger,
                    "Sequence",
                    "sequence schema declared",
                    remediation);

                throw new InvalidOperationException(
                    $"MySQL schema configuration is not supported. Remove the schema from sequence '{sequence.Name}'.");
            }
        }
    }

    private static void ValidateKeyedAndIndexedPropertyLengths(
        IModel model,
        ILogger logger
    )
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (!RequiresExplicitMaxLength(property))
                {
                    continue;
                }

                if (!HasUnboundedStoreType(property))
                {
                    continue;
                }

                var propertyKind = property.ClrType.UnwrapNullableType() == typeof(byte[]) ? "binary" : "text";

                MySqlLoggerMessages.KeyOrIndexMaxLengthRequired(
                    logger,
                    entityType.DisplayName(),
                    property.Name,
                    propertyKind);

                throw new InvalidOperationException(
                    $"The keyed or indexed {propertyKind} property "
                    + $"'{entityType.DisplayName()}.{property.Name}' must map to a bounded store type.");
            }
        }
    }

    private static bool HasUnboundedStoreType(
        IProperty property
    )
    {
        // Size is not populated by every bounded mapping. Guid converters, for
        // example, can expose binary(16) through a GuidTypeMapping whose Size is
        // null. The normalized store-type family is therefore the authoritative
        // signal for whether the engine can build a complete key or index.
        var storeType = property.GetRelationalTypeMapping()
            .StoreType;
        var facetStart = storeType.IndexOf('(');
        var storeTypeName = facetStart >= 0 ? storeType[..facetStart] : storeType;

        return storeTypeName
                .Trim()
                .ToLowerInvariant() is "tinytext"
            or "text"
            or "mediumtext"
            or "longtext"
            or "tinyblob"
            or "blob"
            or "mediumblob"
            or "longblob";
    }

    private static void ValidateDecimalPrecision(
        IModel model,
        ILogger logger
    )
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType.UnwrapNullableType() != typeof(decimal))
                {
                    continue;
                }

                if (HasExplicitDecimalPrecision(property))
                {
                    continue;
                }

                MySqlLoggerMessages.ImplicitDecimalPrecisionDefaulted(
                    logger,
                    entityType.DisplayName(),
                    property.Name,
                    defaultPrecision: 18,
                    defaultScale: 2);
            }
        }
    }

    private static bool RequiresExplicitMaxLength(
        IProperty property
    )
    {
        var clrType = property.ClrType.UnwrapNullableType();

        if (clrType != typeof(string)
            && clrType != typeof(byte[]))
        {
            return false;
        }

        var declaringEntityType = property.DeclaringType as IEntityType;

        return property.FindContainingPrimaryKey() is not null
            || declaringEntityType
                ?.GetKeys()
                .Any(key => key.Properties.Contains(property)) == true
            || property
                .GetContainingIndexes()
                .Any(index => !index.GetMySqlFullTextIndex());
    }

    private static bool HasExplicitDecimalPrecision(
        IProperty property
    )
    {
        if (property.GetPrecision() is not null
            || property.GetScale() is not null)
        {
            return true;
        }

        var columnType = property.GetColumnType();

        return !string.IsNullOrWhiteSpace(columnType) && columnType.Contains('(', StringComparison.Ordinal);
    }

    private static void ValidateConstraintNameLengths(
        IModel model,
        ILogger logger
    )
    {
        const int maxConstraintNameLength = 64;

        foreach (var entityType in model.GetEntityTypes())
        {
            // Owned types mapped to a JSON column collapse into the principal table's JSON
            // column at SQL-emit time; EF Core still attaches a conceptual FK + key index on
            // the owned end to track ownership, but neither name reaches the database. The
            // length check would reject auto-generated names that the engine never sees.
            if (entityType.IsMappedToJson())
            {
                continue;
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                var constraintName = foreignKey.GetConstraintName();

                if (constraintName is not null
                    && constraintName.Length > maxConstraintNameLength)
                {
                    var message =
                        $"The foreign key constraint name '{constraintName}' on entity "
                        + $"'{entityType.DisplayName()}' exceeds MySQL's {maxConstraintNameLength}"
                        + "-character limit and will be rejected at migration time.";

                    MySqlLoggerMessages.InvalidConfiguration(
                        logger,
                        MySqlConfigurationFailureReason.ForeignKeyNameTooLong,
                        "ModelValidation");

                    throw new InvalidOperationException(message);
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName();

                if (indexName is not null
                    && indexName.Length > maxConstraintNameLength)
                {
                    var message =
                        $"The index name '{indexName}' on entity '{entityType.DisplayName()}' "
                        + $"exceeds MySQL's {maxConstraintNameLength}-character limit "
                        + "and will be rejected at migration time.";

                    MySqlLoggerMessages.InvalidConfiguration(
                        logger,
                        MySqlConfigurationFailureReason.IndexNameTooLong,
                        "ModelValidation");

                    throw new InvalidOperationException(message);
                }
            }
        }
    }

    private static void ValidateSpatialIndexes(
        IModel model,
        ILogger logger
    )
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var index in entityType.GetIndexes())
            {
                if (!index.GetMySqlSpatialIndex())
                {
                    continue;
                }

                if (index.Properties.Count != 1)
                {
                    ThrowInvalidSpatialIndexConfiguration(
                        logger,
                        $"{entityType.DisplayName()}.{index.GetDatabaseName() ?? index.Properties[0].Name}",
                        "must target exactly one property");
                }

                var property = index.Properties[0];

                if (!MySqlSpatialTypeSupport.IsSpatialClrType(property.ClrType))
                {
                    ThrowInvalidSpatialIndexConfiguration(
                        logger,
                        $"{entityType.DisplayName()}.{index.GetDatabaseName() ?? property.Name}",
                        "must target a NetTopologySuite geometry property");
                }

                if (property.IsNullable)
                {
                    ThrowInvalidSpatialIndexConfiguration(
                        logger,
                        $"{entityType.DisplayName()}.{index.GetDatabaseName() ?? property.Name}",
                        "must target a non-nullable geometry property");
                }

                if (index.IsUnique)
                {
                    ThrowInvalidSpatialIndexConfiguration(
                        logger,
                        $"{entityType.DisplayName()}.{index.GetDatabaseName() ?? property.Name}",
                        "cannot be unique");
                }
            }
        }
    }

    private static void ThrowInvalidSpatialIndexConfiguration(
        ILogger logger,
        string index,
        string reason
    )
    {
        MySqlLoggerMessages.InvalidSpatialIndexConfiguration(logger, index, reason);
        throw new InvalidOperationException($"The spatial index '{index}' {reason} by this provider.");
    }

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
            ValidateTemporalEntityType(entityType, model, support, logger);
        }

        ValidateSharedTemporalTables(model, logger);
    }

    private static void ValidateTemporalEntityType(
        IReadOnlyEntityType entityType,
        IModel model,
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

        ValidateTemporalHierarchy(entityType, model, tableName!, logger);
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
        foreach (var property in entityType.GetProperties())
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
            ThrowInvalidTemporalTable(
                logger,
                $"Entity type '{entityType.DisplayName()}' has no {role} name.");
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

    private static void ValidateTemporalHierarchy(
        IReadOnlyEntityType entityType,
        IModel model,
        string tableName,
        ILogger logger
    )
    {
        var rootType = FindHierarchyRoot(entityType);

        foreach (var hierarchyType in model.GetEntityTypes()
                     .Where(candidate => ReferenceEquals(FindHierarchyRoot(candidate), rootType)))
        {
            var hierarchyTableName = hierarchyType.GetTableName();

            if (hierarchyTableName is not null
                && !string.Equals(hierarchyTableName, tableName, StringComparison.OrdinalIgnoreCase))
            {
                ThrowInvalidTemporalTable(
                    logger,
                    $"Temporal entity hierarchy rooted at '{rootType.DisplayName()}' must use one table. "
                    + "TPT and TPC temporal mappings are not supported because their history cannot be "
                    + "reconstructed atomically.");
            }
        }
    }

    private static IReadOnlyEntityType FindHierarchyRoot(
        IReadOnlyEntityType entityType
    )
    {
        while (entityType.BaseType is not null)
        {
            entityType = entityType.BaseType;
        }

        return entityType;
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

    private sealed class StringTupleComparer : IEqualityComparer<(string? Schema, string? Name)>
    {
        public static StringTupleComparer OrdinalIgnoreCase { get; } = new();

        public bool Equals(
            (string? Schema, string? Name) x,
            (string? Schema, string? Name) y
        ) => string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(
            (string? Schema, string? Name) value
        ) => HashCode.Combine(
            value.Schema is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value.Schema),
            value.Name is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name));
    }
}
