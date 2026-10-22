namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Projects store types, column facets, and optional spatial mappings into EF properties.
/// </summary>
internal sealed partial class MySqlScaffoldingModelFactory
{
    private static void ApplyColumnConfiguration(
        PropertyBuilder propertyBuilder,
        DatabaseColumn column,
        ColumnMapping mapping
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        ArgumentNullException.ThrowIfNull(column);

        propertyBuilder.HasColumnName(column.Name);
        propertyBuilder.HasColumnType(column.StoreType);
        propertyBuilder.Metadata.SetColumnOrder(column.Table.Columns.IndexOf(column));

        if (!column.IsNullable)
        {
            propertyBuilder.IsRequired();
        }

        if (mapping.MaxLength is not null)
        {
            propertyBuilder.HasMaxLength(mapping.MaxLength.Value);
        }

        if (mapping.IsFixedLength is not null)
        {
            propertyBuilder.IsFixedLength(mapping.IsFixedLength.Value);
        }

        if (mapping.Precision is not null
            && mapping.Scale is not null)
        {
            propertyBuilder.HasPrecision(mapping.Precision.Value, mapping.Scale.Value);
        }
        else if (mapping.Precision is not null)
        {
            propertyBuilder.HasPrecision(mapping.Precision.Value);
        }

        if (!string.IsNullOrWhiteSpace(column.Collation))
        {
            propertyBuilder.UseCollation(column.Collation);
        }

        if (!string.IsNullOrWhiteSpace(column.Comment))
        {
            propertyBuilder.HasComment(column.Comment);
        }

        if (!string.IsNullOrWhiteSpace(column.ComputedColumnSql))
        {
            propertyBuilder.HasComputedColumnSql(column.ComputedColumnSql, column.IsStored);
        }
        else if (column.DefaultValueSql is not null)
        {
            propertyBuilder.HasDefaultValueSql(column.DefaultValueSql);
        }

        if (column.ValueGenerated == ValueGenerated.OnAdd)
        {
            propertyBuilder.ValueGeneratedOnAdd();
        }

        if (mapping.GuidFormat is not null)
        {
            propertyBuilder.Metadata.SetMySqlGuidFormat(mapping.GuidFormat.Value);
        }

        if (column.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
                ?.Value is int spatialReferenceSystemId)
        {
            propertyBuilder.Metadata.SetMySqlSpatialReferenceSystemId(spatialReferenceSystemId);
        }
    }

    /// <summary>Applies column nullability to the closed set of scaffolded scalar value types.</summary>
    /// <param name="clrType">The CLR type selected for the store column.</param>
    /// <param name="isNullable">Whether the store column permits null.</param>
    /// <returns>The nullable value type, or the original type when no wrapping is needed.</returns>
    /// <exception cref="InvalidOperationException">
    /// A nullable column requires a value type without an explicit nullable mapping.
    /// </exception>
    internal static Type GetPropertyClrType(
        Type clrType,
        bool isNullable
    )
    {
        if (!isNullable
            || !clrType.IsValueType
            || Nullable.GetUnderlyingType(clrType) is not null)
        {
            return clrType;
        }

        // WHY: Explicit nullable types keep the scaffolded scalar set statically visible
        // to AOT analysis; MakeGenericType would require dynamic generic construction.
        if (clrType == typeof(bool))
        {
            return typeof(bool?);
        }

        if (clrType == typeof(byte))
        {
            return typeof(byte?);
        }

        if (clrType == typeof(sbyte))
        {
            return typeof(sbyte?);
        }

        if (clrType == typeof(short))
        {
            return typeof(short?);
        }

        if (clrType == typeof(ushort))
        {
            return typeof(ushort?);
        }

        if (clrType == typeof(int))
        {
            return typeof(int?);
        }

        if (clrType == typeof(uint))
        {
            return typeof(uint?);
        }

        if (clrType == typeof(long))
        {
            return typeof(long?);
        }

        if (clrType == typeof(ulong))
        {
            return typeof(ulong?);
        }

        if (clrType == typeof(float))
        {
            return typeof(float?);
        }

        if (clrType == typeof(double))
        {
            return typeof(double?);
        }

        if (clrType == typeof(decimal))
        {
            return typeof(decimal?);
        }

        if (clrType == typeof(Guid))
        {
            return typeof(Guid?);
        }

        if (clrType == typeof(DateTime))
        {
            return typeof(DateTime?);
        }

        if (clrType == typeof(DateOnly))
        {
            return typeof(DateOnly?);
        }

        if (clrType == typeof(TimeOnly))
        {
            return typeof(TimeOnly?);
        }

        throw new InvalidOperationException(
            $"The scaffolded value type '{clrType}' has no AOT-safe nullable mapping.");
    }

    private bool TryHandleSpatialColumn(
        DatabaseTable table,
        DatabaseColumn column,
        EntityTypeBuilder entityBuilder,
        HashSet<string> usedPropertyNames,
        Dictionary<(DatabaseTable Table, DatabaseColumn Column), PropertyBuilder> entityPropertyBuilders,
        HashSet<(DatabaseTable Table, DatabaseColumn Column)> skippedColumns
    )
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(entityBuilder);
        ArgumentNullException.ThrowIfNull(usedPropertyNames);
        ArgumentNullException.ThrowIfNull(entityPropertyBuilders);
        ArgumentNullException.ThrowIfNull(skippedColumns);

        if (!MySqlSpatialTypeSupport.IsSpatialStoreType(column.StoreType))
        {
            return false;
        }

        if (_spatialTypeProvider is null
            || !_spatialTypeProvider.TryResolveClrType(column.StoreType, out var spatialClrType)
            || spatialClrType is null)
        {
            skippedColumns.Add((table, column));
            MySqlLoggerMessages.MissingSpatialPackageDuringScaffolding(_logger, table.Name, column.Name);

            return true;
        }

        var propertyName = CreateUniqueClrIdentifier(column.Name, usedPropertyNames, "Geometry");
        var propertyBuilder = entityBuilder.Property(spatialClrType, propertyName);

        ApplyColumnConfiguration(propertyBuilder, column, ColumnMapping.Scalar(spatialClrType));

        entityPropertyBuilders[(table, column)] = propertyBuilder;
        _scaffoldingContext.MarkUsesNetTopologySuiteScaffolding();

        return true;
    }

    private ColumnMapping ResolveColumnMapping(
        DatabaseColumn column
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column.StoreType);

        var storeType = column.StoreType;
        var normalizedStoreType = storeType.ToLowerInvariant();

        if (normalizedStoreType == "uuid")
        {
            // Reverse engineering does not finalize a runtime model, so reject
            // an impossible engine/type pairing before generating unusable code.
            var detectedVersion = _scaffoldingContext.DetectedServerVersionText
                ?? throw new InvalidOperationException(
                    "The detected server version is unavailable for UUID scaffolding.");

            MySqlMariaDbUuidSupport.EnsureSupported(
                MySqlServerVersion.Parse(detectedVersion)
                    .Profile);

            return new ColumnMapping(
                typeof(Guid),
                MaxLength: null,
                IsFixedLength: null,
                Precision: null,
                Scale: null,
                GuidFormat: MySqlGuidFormat.Uuid);
        }

        if (normalizedStoreType is "char(36)" or "varchar(36)")
        {
            if (_reverseEngineeringOptions.ScaffoldTextGuidsAsGuids)
            {
                return new ColumnMapping(
                    typeof(Guid),
                    MaxLength: 36,
                    IsFixedLength: normalizedStoreType == "char(36)",
                    Precision: null,
                    Scale: null,
                    GuidFormat: normalizedStoreType == "char(36)" ? MySqlGuidFormat.Char36 : null);
            }

            return new ColumnMapping(
                typeof(string),
                MaxLength: 36,
                IsFixedLength: normalizedStoreType == "char(36)",
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType == "binary(16)")
        {
            return new ColumnMapping(
                typeof(Guid),
                MaxLength: 16,
                IsFixedLength: true,
                Precision: null,
                Scale: null,
                GuidFormat: MySqlGuidFormat.Binary16);
        }

        if (normalizedStoreType.StartsWith("binary(", StringComparison.Ordinal)
            || normalizedStoreType == "binary")
        {
            return new ColumnMapping(
                typeof(byte[]),
                MaxLength: ParseSingleFacet(storeType),
                IsFixedLength: true,
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("varchar(", StringComparison.Ordinal)
            || normalizedStoreType == "varchar")
        {
            return new ColumnMapping(
                typeof(string),
                MaxLength: ParseSingleFacet(storeType),
                IsFixedLength: false,
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("char(", StringComparison.Ordinal)
            || normalizedStoreType == "char")
        {
            return new ColumnMapping(
                typeof(string),
                MaxLength: ParseSingleFacet(storeType),
                IsFixedLength: true,
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("varbinary(", StringComparison.Ordinal)
            || normalizedStoreType == "varbinary")
        {
            return new ColumnMapping(
                typeof(byte[]),
                MaxLength: ParseSingleFacet(storeType),
                IsFixedLength: false,
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("decimal(", StringComparison.Ordinal))
        {
            var (precision, scale) = ParsePrecisionAndScale(storeType);

            return new ColumnMapping(
                typeof(decimal),
                MaxLength: null,
                IsFixedLength: null,
                Precision: precision,
                Scale: scale,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("enum(", StringComparison.Ordinal)
            || normalizedStoreType.StartsWith("set(", StringComparison.Ordinal))
        {
            return ColumnMapping.Scalar(typeof(string));
        }

        if (normalizedStoreType.StartsWith("bit(", StringComparison.Ordinal))
        {
            return normalizedStoreType == "bit(1)"
                ? ColumnMapping.Scalar(typeof(bool))
                : ColumnMapping.Scalar(typeof(ulong));
        }

        if (normalizedStoreType.StartsWith("datetime(", StringComparison.Ordinal)
            || normalizedStoreType.StartsWith("timestamp(", StringComparison.Ordinal))
        {
            return ColumnMapping.Scalar(typeof(DateTime));
        }

        if (normalizedStoreType.StartsWith("time(", StringComparison.Ordinal))
        {
            return ColumnMapping.Scalar(typeof(TimeOnly));
        }

        return normalizedStoreType switch
        {
            "int" or "integer" => ColumnMapping.Scalar(typeof(int)),
            "bigint" => ColumnMapping.Scalar(typeof(long)),
            "smallint" => ColumnMapping.Scalar(typeof(short)),
            "mediumint" => ColumnMapping.Scalar(typeof(int)),
            "tinyint" => ColumnMapping.Scalar(typeof(sbyte)),
            "tinyint(1)" => ColumnMapping.Scalar(typeof(bool)),
            "tinyint unsigned" => ColumnMapping.Scalar(typeof(byte)),
            "smallint unsigned" => ColumnMapping.Scalar(typeof(ushort)),
            "mediumint unsigned" => ColumnMapping.Scalar(typeof(uint)),
            "int unsigned" => ColumnMapping.Scalar(typeof(uint)),
            "bigint unsigned" => ColumnMapping.Scalar(typeof(ulong)),
            "double" or "double unsigned" => ColumnMapping.Scalar(typeof(double)),
            "float" or "float unsigned" => ColumnMapping.Scalar(typeof(float)),
            "year" => ColumnMapping.Scalar(typeof(short)),
            "bit" => ColumnMapping.Scalar(typeof(bool)),
            "datetime" or "datetime(6)" or "timestamp" or "timestamp(6)" => ColumnMapping.Scalar(typeof(DateTime)),
            "date" => ColumnMapping.Scalar(typeof(DateOnly)),
            "time" or "time(6)" => ColumnMapping.Scalar(typeof(TimeOnly)),
            "json" or "longtext" or "mediumtext" or "text" or "tinytext" => ColumnMapping.Scalar(typeof(string)),
            "longblob" or "mediumblob" or "blob" or "tinyblob" => ColumnMapping.Scalar(typeof(byte[])),
            _ => ColumnMapping.Scalar(typeof(string)),
        };
    }

    private static int? ParseSingleFacet(
        string storeType
    )
    {
        var startIndex = storeType.IndexOf('(');
        var endIndex = storeType.IndexOf(')');

        if (startIndex < 0
            || endIndex <= startIndex + 1)
        {
            return null;
        }

        return int.TryParse(
            storeType[(startIndex + 1)..endIndex],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var size)
            ? size
            : null;
    }

    private static (int? Precision, int? Scale) ParsePrecisionAndScale(
        string storeType
    )
    {
        var startIndex = storeType.IndexOf('(');
        var endIndex = storeType.IndexOf(')');

        if (startIndex < 0
            || endIndex <= startIndex + 1)
        {
            return (null, null);
        }

        var parts = storeType[(startIndex + 1)..endIndex]
            .Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length == 0
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var precision))
        {
            return (null, null);
        }

        if (parts.Length == 1
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
        {
            return (precision, null);
        }

        return (precision, scale);
    }

    private readonly record struct ColumnMapping(
        Type ClrType,
        int? MaxLength,
        bool? IsFixedLength,
        int? Precision,
        int? Scale,
        MySqlGuidFormat? GuidFormat
    )
    {
        public static ColumnMapping Scalar(
            Type clrType
        ) => new(clrType, null, null, null, null, null);
    }
}
