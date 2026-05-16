namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlModelValidator : RelationalModelValidator
{
    private readonly MySqlSingletonOptions _mySqlSingletonOptions;

    public MySqlModelValidator(
        ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies, relationalDependencies)
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);
        _mySqlSingletonOptions = singletonOptions
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
        var providerLogger = _mySqlSingletonOptions.ProviderLogger ?? logger.Logger;

        ValidateNoSchemas(model, providerLogger);
        ValidateKeyedAndIndexedPropertyLengths(model, providerLogger);
        ValidateDecimalPrecision(model, providerLogger);
        ValidateConstraintNameLengths(model, providerLogger);
        ValidateSpatialIndexes(model, providerLogger);
    }

    private static void ValidateNoSchemas(
        IModel model,
        ILogger logger
    )
    {
        const string remediation = "Remove the configured schema; MySQL treats schema and database as synonyms.";

        if (!string.IsNullOrWhiteSpace(model.GetDefaultSchema()))
        {
            MySqlLoggerMessages.SchemaUnsupported(logger, "Model", "<default>", "default schema declared", remediation);
            throw new InvalidOperationException(
                "MySQL schema configuration is not supported. Remove the configured default schema.");
        }

        foreach (var entityType in model.GetEntityTypes())
        {
            if (!string.IsNullOrWhiteSpace(entityType.GetSchema()))
            {
                MySqlLoggerMessages.SchemaUnsupported(
                    logger,
                    "Entity",
                    entityType.DisplayName(),
                    "table schema declared",
                    remediation);
                throw new InvalidOperationException(
                    "MySQL schema configuration is not supported. Remove the schema from entity "
                    + $"'{entityType.DisplayName()}'.");
            }

            if (!string.IsNullOrWhiteSpace(entityType.GetViewSchema()))
            {
                MySqlLoggerMessages.SchemaUnsupported(
                    logger,
                    "View",
                    entityType.DisplayName(),
                    "view schema declared",
                    remediation);
                throw new InvalidOperationException(
                    "MySQL schema configuration is not supported. Remove the view schema from entity "
                    + $"'{entityType.DisplayName()}'.");
            }
        }

        foreach (var sequence in model.GetSequences())
        {
            if (!string.IsNullOrWhiteSpace(sequence.Schema))
            {
                MySqlLoggerMessages.SchemaUnsupported(
                    logger,
                    "Sequence",
                    sequence.Name,
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

                if (property.GetMaxLength() is not null)
                {
                    continue;
                }

                var propertyKind = UnwrapNullableType(property.ClrType) == typeof(byte[]) ? "binary" : "text";

                MySqlLoggerMessages.KeyOrIndexMaxLengthRequired(
                    logger,
                    entityType.DisplayName(),
                    property.Name,
                    propertyKind);
                throw new InvalidOperationException(
                    $"The keyed or indexed {propertyKind} property "
                    + $"'{entityType.DisplayName()}.{property.Name}' must declare an explicit max length.");
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
            foreach (var property in entityType.GetProperties())
            {
                if (UnwrapNullableType(property.ClrType) != typeof(decimal))
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
        var clrType = UnwrapNullableType(property.ClrType);

        if (clrType != typeof(string)
            && clrType != typeof(byte[]))
        {
            return false;
        }

        var declaringEntityType = property.DeclaringType as IEntityType;

        return property.FindContainingPrimaryKey() is not null
            || declaringEntityType
                ?.GetKeys()
                .Any(key => key.Properties.Contains(property))
            == true
            || property
                .GetContainingIndexes()
                .Any();
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

                    MySqlLoggerMessages.InvalidConfiguration(logger, message, "ModelValidation", string.Empty);

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

                    MySqlLoggerMessages.InvalidConfiguration(logger, message, "ModelValidation", string.Empty);

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

    private static Type UnwrapNullableType(
        Type type
    )
    {
        ArgumentNullException.ThrowIfNull(type);

        return Nullable.GetUnderlyingType(type) ?? type;
    }
}
