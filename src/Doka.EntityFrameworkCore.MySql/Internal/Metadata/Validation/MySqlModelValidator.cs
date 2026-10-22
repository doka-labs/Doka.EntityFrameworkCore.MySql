namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Validates provider metadata against the capabilities and relational constraints of the configured engine.
/// </summary>
internal sealed partial class MySqlModelValidator : RelationalModelValidator
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
        ValidateApplicationTimeTables(model, modelValidationLogger);
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
        var tableDefaults = MySqlIndexByteBudget.GetPhysicalTableDefaults(model);

        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var key in entityType.GetKeys())
            {
                foreach (var table in MySqlIndexByteBudget.GetPhysicalIndexTables(entityType, key.Properties))
                {
                    ValidateIndexKeyWidth(
                        entityType,
                        key.Properties,
                        prefixLengths: null,
                        $"key '{key.GetName()}'",
                        table,
                        tableDefaults[table],
                        logger);
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                if (index.GetMySqlFullTextIndex()
                    || index.GetMySqlSpatialIndex())
                {
                    continue;
                }

                foreach (var table in MySqlIndexByteBudget.GetPhysicalIndexTables(entityType, index.Properties))
                {
                    ValidateIndexKeyWidth(
                        entityType,
                        index.Properties,
                        index.GetMySqlIndexPrefixLengths(),
                        $"index '{index.GetDatabaseName() ?? index.Name}'",
                        table,
                        tableDefaults[table],
                        logger);
                }
            }
        }
    }

    private static void ValidateIndexKeyWidth(
        IEntityType entityType,
        IReadOnlyList<IProperty> properties,
        IReadOnlyList<int>? prefixLengths,
        string definition,
        StoreObjectIdentifier table,
        MySqlIndexByteBudget.TableDefaults tableDefaults,
        ILogger logger
    )
    {
        if (prefixLengths is not null
            && prefixLengths.Count != properties.Count)
        {
            throw new InvalidOperationException(
                $"The {definition} on entity type '{entityType.DisplayName()}' must declare one prefix length "
                + "per indexed property.");
        }

        if (prefixLengths?.Any(prefixLength => prefixLength < 0) == true)
        {
            throw new InvalidOperationException(
                $"The {definition} on entity type '{entityType.DisplayName()}' contains a negative prefix length.");
        }

        long knownBytes = 0;

        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];

            var prefixLength = prefixLengths?[index] ?? 0;

            if (prefixLength == 0
                && MySqlIndexByteBudget.HasUnboundedStoreType(property))
            {
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

            var fullLength = MySqlIndexByteBudget.GetStoreTypeLength(property);

            if (prefixLength > 0
                && prefixLength > fullLength)
            {
                throw new InvalidOperationException(
                    $"The {definition} on entity type '{entityType.DisplayName()}' declares prefix length "
                    + $"{prefixLength} for '{property.Name}', which exceeds its store length {fullLength}.");
            }

            var characterWidth = MySqlIndexByteBudget.GetBytesPerCharacter(entityType, property, table, tableDefaults);
            var propertyBytes = MySqlIndexByteBudget.GetIndexedBytes(property, prefixLength, fullLength, characterWidth);
            if (propertyBytes is null)
            {
                continue;
            }

            knownBytes += propertyBytes.Value;
            if (knownBytes > MySqlIndexByteBudget.MaximumBytes)
            {
                throw new InvalidOperationException(
                    $"The {definition} on entity type '{entityType.DisplayName()}' requires at least "
                    + $"{knownBytes} bytes and exceeds InnoDB's maximum supported "
                    + $"{MySqlIndexByteBudget.MaximumBytes}-byte index-key length. Configure a deliberate prefix "
                    + "or reduce the indexed column lengths; Doka does not invent a prefix because that "
                    + "would change index and uniqueness semantics.");
            }
        }
    }

    private static void ValidateDecimalPrecision(
        IModel model,
        ILogger logger
    )
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in GetPropertiesIncludingComplexTypes(entityType))
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

    private static IEnumerable<IProperty> GetPropertiesIncludingComplexTypes(
        IReadOnlyTypeBase typeBase
    )
    {
        foreach (var property in typeBase.GetDeclaredProperties())
        {
            yield return (IProperty)property;
        }

        foreach (var complexProperty in typeBase.GetDeclaredComplexProperties())
        {
            if (complexProperty.IsCollection)
            {
                continue;
            }

            foreach (var property in GetPropertiesIncludingComplexTypes(complexProperty.ComplexType))
            {
                yield return property;
            }
        }
    }

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
