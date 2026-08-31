namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Validates provider metadata against the capabilities and relational constraints of the configured engine.
/// </summary>
internal sealed class MySqlModelValidator : RelationalModelValidator
{
    private const int MaximumInnoDbIndexBytes = 3072;
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
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var key in entityType.GetKeys())
            {
                ValidateIndexKeyWidth(
                    entityType,
                    key.Properties,
                    prefixLengths: null,
                    $"key '{key.GetName()}'",
                    logger);
            }

            foreach (var index in entityType.GetIndexes())
            {
                if (index.GetMySqlFullTextIndex()
                    || index.GetMySqlSpatialIndex())
                {
                    continue;
                }

                ValidateIndexKeyWidth(
                    entityType,
                    index.Properties,
                    index.GetMySqlIndexPrefixLengths(),
                    $"index '{index.GetDatabaseName() ?? index.Name}'",
                    logger);
            }
        }
    }

    private static void ValidateIndexKeyWidth(
        IEntityType entityType,
        IReadOnlyList<IProperty> properties,
        IReadOnlyList<int>? prefixLengths,
        string definition,
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

            if (HasUnboundedStoreType(property))
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

            var prefixLength = prefixLengths?[index] ?? 0;
            var fullLength = GetStoreTypeLength(property);

            if (prefixLength > 0
                && prefixLength > fullLength)
            {
                throw new InvalidOperationException(
                    $"The {definition} on entity type '{entityType.DisplayName()}' declares prefix length "
                    + $"{prefixLength} for '{property.Name}', which exceeds its store length {fullLength}.");
            }

            var propertyBytes = GetIndexedBytes(entityType, property, prefixLength, fullLength);
            if (propertyBytes is null)
            {
                continue;
            }

            knownBytes += propertyBytes.Value;
            if (knownBytes > MaximumInnoDbIndexBytes)
            {
                throw new InvalidOperationException(
                    $"The {definition} on entity type '{entityType.DisplayName()}' requires at least "
                    + $"{knownBytes} bytes and exceeds InnoDB's maximum supported "
                    + $"{MaximumInnoDbIndexBytes}-byte index-key length. Configure a deliberate prefix "
                    + "or reduce the indexed column lengths; Doka does not invent a prefix because that "
                    + "would change index and uniqueness semantics.");
            }
        }
    }

    private static long? GetIndexedBytes(
        IEntityType entityType,
        IProperty property,
        int prefixLength,
        int? fullLength
    )
    {
        var storeType = property.GetRelationalTypeMapping().StoreType;
        var storeTypeName = GetStoreTypeName(storeType);
        var indexedLength = prefixLength > 0 ? prefixLength : fullLength;

        if (storeTypeName is "char" or "varchar")
        {
            if (indexedLength is null)
            {
                return null;
            }

            var bytesPerCharacter = GetBytesPerCharacter(entityType, property);

            // One byte per character is the lower bound for an unknown server
            // character set. It still rejects definitions that cannot fit on
            // any supported InnoDB configuration, while the migration warning
            // guard closes configuration-dependent cases at execution time.
            return checked((long)indexedLength.Value * (bytesPerCharacter ?? 1));
        }

        return storeTypeName is "binary" or "varbinary"
            ? indexedLength
            : GetFixedStoreTypeBytes(storeTypeName, storeType);
    }

    private static int? GetBytesPerCharacter(
        IEntityType entityType,
        IProperty property
    )
    {
        var charSet = GetCharSetFromCollation(property.GetCollation())
            ?? entityType.GetMySqlCharSet()
            ?? GetCharSetFromCollation(entityType.Model.GetCollation())
            ?? entityType.Model.GetMySqlCharSet();

        return charSet?.Trim().ToLowerInvariant() switch
        {
            "utf8mb4" or "utf16" or "utf16le" or "utf32" or "gb18030" => 4,
            "utf8" or "utf8mb3" => 3,
            "big5" or "cp932" or "eucjpms" or "gb2312" or "gbk" or "sjis" or "ucs2" or "ujis" => 2,
            "armscii8"
                or "ascii"
                or "binary"
                or "cp1250"
                or "cp1251"
                or "cp1256"
                or "cp1257"
                or "cp850"
                or "cp852"
                or "cp866"
                or "dec8"
                or "geostd8"
                or "greek"
                or "hebrew"
                or "hp8"
                or "keybcs2"
                or "koi8r"
                or "koi8u"
                or "latin1"
                or "latin2"
                or "latin5"
                or "latin7"
                or "macce"
                or "macroman"
                or "swe7"
                or "tis620" => 1,
            _ => null,
        };
    }

    private static string? GetCharSetFromCollation(
        string? collation
    )
    {
        if (string.IsNullOrWhiteSpace(collation))
        {
            return null;
        }

        var separator = collation.IndexOf('_');

        return separator > 0 ? collation[..separator] : null;
    }

    private static int? GetStoreTypeLength(
        IProperty property
    )
    {
        var storeType = property.GetRelationalTypeMapping().StoreType;
        var facetStart = storeType.IndexOf('(');
        if (facetStart >= 0)
        {
            var facetEnd = storeType.IndexOf(')', facetStart + 1);
            if (facetEnd > facetStart + 1)
            {
                var facet = storeType.AsSpan(facetStart + 1, facetEnd - facetStart - 1);
                var separator = facet.IndexOf(',');
                var lengthFacet = separator >= 0 ? facet[..separator] : facet;

                if (int.TryParse(lengthFacet.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
                {
                    return length;
                }
            }
        }

        return property.GetRelationalTypeMapping().Size
            ?? property.GetMaxLength();
    }

    private static string GetStoreTypeName(
        string storeType
    )
    {
        var facetStart = storeType.IndexOf('(');
        var name = facetStart >= 0 ? storeType[..facetStart] : storeType;
        var modifierStart = name.IndexOf(' ');

        return (modifierStart >= 0 ? name[..modifierStart] : name)
            .Trim()
            .ToLowerInvariant();
    }

    private static int? GetFixedStoreTypeBytes(
        string storeTypeName,
        string storeType
    ) => storeTypeName switch
    {
        "bit" => GetBitBytes(storeType),
        "bool" or "boolean" or "tinyint" or "year" => 1,
        "smallint" => 2,
        "mediumint" or "date" => 3,
        "int" or "integer" or "float" => 4,
        "bigint" or "double" or "real" => 8,
        "decimal" or "numeric" => GetDecimalBytes(storeType),
        "time" => 3 + GetFractionalSecondsBytes(storeType),
        "datetime" => 5 + GetFractionalSecondsBytes(storeType),
        "timestamp" => 4 + GetFractionalSecondsBytes(storeType),
        _ => null,
    };

    private static int GetBitBytes(
        string storeType
    )
    {
        var bits = GetFirstStoreTypeFacet(storeType) ?? 1;

        return checked((bits + 7) / 8);
    }

    private static int? GetDecimalBytes(
        string storeType
    )
    {
        var facets = GetStoreTypeFacets(storeType);
        if (facets is null)
        {
            return null;
        }

        var precision = facets.Value.First;
        var scale = facets.Value.Second;

        if (precision < 1
            || scale < 0
            || scale > precision)
        {
            return null;
        }

        return GetDecimalDigitsBytes(precision - scale)
            + GetDecimalDigitsBytes(scale);
    }

    private static int GetDecimalDigitsBytes(
        int digits
    )
    {
        ReadOnlySpan<int> remainderBytes = [0, 1, 1, 2, 2, 3, 3, 3, 4];

        return ((digits / 9) * 4) + remainderBytes[digits % 9];
    }

    private static int GetFractionalSecondsBytes(
        string storeType
    )
    {
        var precision = GetFirstStoreTypeFacet(storeType) ?? 0;

        return precision switch
        {
            0 => 0,
            <= 2 => 1,
            <= 4 => 2,
            _ => 3,
        };
    }

    private static int? GetFirstStoreTypeFacet(
        string storeType
    ) => GetStoreTypeFacets(storeType)?.First;

    private static (int First, int Second)? GetStoreTypeFacets(
        string storeType
    )
    {
        var facetStart = storeType.IndexOf('(');
        var facetEnd = facetStart < 0 ? -1 : storeType.IndexOf(')', facetStart + 1);
        if (facetStart < 0
            || facetEnd <= facetStart + 1)
        {
            return null;
        }

        var facets = storeType.AsSpan(facetStart + 1, facetEnd - facetStart - 1);
        var separator = facets.IndexOf(',');
        var firstFacet = separator >= 0 ? facets[..separator] : facets;
        var secondFacet = separator >= 0 ? facets[(separator + 1)..] : "0";

        return int.TryParse(firstFacet.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var first)
            && int.TryParse(secondFacet.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var second)
                ? (first, second)
                : null;
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

        return GetStoreTypeName(storeType) is "tinytext"
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
        IReadOnlyList<IReadOnlyProperty> properties,
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

    private sealed record TemporalSharedTableContract(
        string? HistoryTableName,
        string? HistoryTableSchema,
        string PeriodStartColumnName,
        string PeriodEndColumnName
    );

    private sealed record ApplicationTimeSharedTableContract(
        string PeriodName,
        string PeriodStartColumnName,
        string PeriodEndColumnName,
        bool WithoutOverlaps
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
