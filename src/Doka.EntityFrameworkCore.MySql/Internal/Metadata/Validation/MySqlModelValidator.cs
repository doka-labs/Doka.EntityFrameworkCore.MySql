namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlModelValidator : RelationalModelValidator
{
    public MySqlModelValidator(
        ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies
    ) : base(dependencies, relationalDependencies) { }

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
}
