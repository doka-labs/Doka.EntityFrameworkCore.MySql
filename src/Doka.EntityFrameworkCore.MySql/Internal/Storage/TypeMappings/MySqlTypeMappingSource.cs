namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlTypeMappingSource : RelationalTypeMappingSource
{
    // utf8mb4 uses at most four bytes per character. A 255-character key part
    // therefore occupies at most 1,020 bytes and lets three conventional parts
    // fit below the 3,072-byte index limit of every supported engine.
    private const int DefaultKeyOrIndexLength = 255;

    // utf8mb4 may require four bytes per character. MySQL limits a VARCHAR
    // column to 65,535 bytes, so larger implicit strings must use a text type.
    private const int MaximumUtf8Mb4VarCharLength = 16383;

    private const int DefaultDateTimePrecision = 6;
    private const int DefaultTimePrecision = 6;

    // v1.0 default decimal precision/scale changed from the MySQL maximum (65,30) to
    // the practical real-world default (18,2). The previous default reserved the
    // maximum on-disk row footprint for every unattributed decimal column and would
    // silently widen existing schemata on the first post-upgrade migration. See
    // ADR D-006 for the breaking-change rationale, migration recipe, and the
    // ImplicitDecimalPrecisionDefaulted warning that fires on first use per context.
    private const int DefaultDecimalPrecision = 18;
    private const int DefaultDecimalScale = 2;

    private static readonly RelationalTypeMapping s_intMapping = new IntTypeMapping("int", DbType.Int32);
    private static readonly RelationalTypeMapping s_longMapping = new LongTypeMapping("bigint", DbType.Int64);
    private static readonly RelationalTypeMapping s_shortMapping = new ShortTypeMapping("smallint", DbType.Int16);
    private static readonly RelationalTypeMapping s_sbyteMapping = new SByteTypeMapping("tinyint", DbType.SByte);
    private static readonly RelationalTypeMapping s_byteMapping = new ByteTypeMapping("tinyint unsigned", DbType.Byte);

    private static readonly RelationalTypeMapping s_ushortMapping = new UShortTypeMapping(
        "smallint unsigned",
        DbType.UInt16);

    private static readonly RelationalTypeMapping s_uintMapping = new UIntTypeMapping("int unsigned", DbType.UInt32);
    private static readonly RelationalTypeMapping s_ulongMapping = new ULongTypeMapping(
        "bigint unsigned",
        DbType.UInt64);

    private static readonly RelationalTypeMapping s_boolMapping = new BoolTypeMapping("tinyint(1)", DbType.Boolean);
    private static readonly RelationalTypeMapping s_doubleMapping = MySqlDoubleTypeMapping.Default;
    private static readonly RelationalTypeMapping s_floatMapping = MySqlFloatTypeMapping.Default;

    private static readonly RelationalTypeMapping s_decimalMapping = new DecimalTypeMapping(
        $"decimal({DefaultDecimalPrecision},{DefaultDecimalScale})",
        DbType.Decimal,
        DefaultDecimalPrecision,
        DefaultDecimalScale);

    private static readonly RelationalTypeMapping s_dateTimeMapping =
        new MySqlDateTimeTypeMapping("datetime(6)");

    private static readonly RelationalTypeMapping s_timestampMapping =
        new MySqlDateTimeTypeMapping("timestamp(6)");

    private static readonly RelationalTypeMapping s_rowVersionMapping =
        new MySqlRowVersionTypeMapping();

    private static readonly RelationalTypeMapping s_dateOnlyMapping = new DateOnlyTypeMapping("date", DbType.Date);
    private static readonly MySqlTimeOnlyTypeMapping s_timeOnlyMapping = MySqlTimeOnlyTypeMapping.Default;
    private static readonly MySqlTimeSpanTypeMapping s_timeSpanMapping = MySqlTimeSpanTypeMapping.Default;
    private static readonly RelationalTypeMapping s_charClrMapping = MySqlCharTypeMapping.Default;
    private static readonly RelationalTypeMapping s_guidBinaryMapping = new MySqlGuidBinaryTypeMapping();

    // GUID text representations are ASCII-only (32 hex digits plus four hyphens), so
    // the column does not need utf8mb4 storage; Unicode: false keeps the on-disk and
    // wire footprint at one byte per character.
    private static readonly RelationalTypeMapping s_guidChar36Mapping = MySqlGuidStringTypeMapping.Default;

    private static readonly RelationalTypeMapping s_guidVarchar36Mapping =
        new MySqlGuidStringTypeMapping("varchar(36)", DbType.String, 36, useKeyComparison: false);

    private static readonly RelationalTypeMapping s_jsonStringMapping = new MySqlJsonStringTypeMapping("json");

    // The relational model builder asks for FindMapping(typeof(JsonTypePlaceholder), null)
    // when it constructs the JSON container column for ToJson()-mapped owned entities;
    // returning null trips Microsoft.EntityFrameworkCore.Internal.RelationalStrings
    // UnsupportedJsonColumnType ("JSON columns require a provider-specific JSON store
    // type"). MySQL's native JSON column type satisfies the contract.
    // CLR type is MemoryStream (not string) so EF Core's GenerateJsonReader path can hand
    // the JSON column straight to a Utf8JsonReaderManager without a string -> MemoryStream
    // coercion that the base RelationalShapedQueryCompilingExpressionVisitor cannot
    // generate. See MySqlJsonContainerTypeMapping for the GetString -> MemoryStream
    // wrapping that keeps MySqlConnector's default json-as-string read path intact.
    private static readonly RelationalTypeMapping s_jsonContainerColumnMapping =
        new MySqlJsonContainerTypeMapping("json");

    private static readonly RelationalTypeMapping
        s_jsonElementMapping = MySqlJsonTypeMapping.CreateJsonElementMapping();

    private static readonly RelationalTypeMapping s_jsonDocumentMapping =
        MySqlJsonTypeMapping.CreateJsonDocumentMapping();

    private static readonly RelationalTypeMapping s_jsonNodeMapping = MySqlJsonTypeMapping.CreateJsonNodeMapping();
    private static readonly RelationalTypeMapping s_jsonObjectMapping = MySqlJsonTypeMapping.CreateJsonObjectMapping();
    private static readonly RelationalTypeMapping s_jsonArrayMapping = MySqlJsonTypeMapping.CreateJsonArrayMapping();
    private static readonly RelationalTypeMapping s_serverVersionMapping = new MySqlServerVersionTypeMapping();
    private static readonly RelationalTypeMapping s_stringMapping = new MySqlStringTypeMapping(
        "longtext",
        DbType.String,
        unicode: true);

    private static readonly RelationalTypeMapping s_byteArrayMapping =
        new ByteArrayTypeMapping("longblob", DbType.Binary);

    private static readonly RelationalTypeMapping s_mediumIntMapping = new IntTypeMapping("mediumint", DbType.Int32);

    private static readonly RelationalTypeMapping s_mediumIntUnsignedMapping =
        new UIntTypeMapping("mediumint unsigned", DbType.UInt32);

    private static readonly RelationalTypeMapping s_mediumTextMapping =
        new MySqlStringTypeMapping("mediumtext", DbType.String, unicode: true);

    private static readonly RelationalTypeMapping s_tinyTextMapping = new MySqlStringTypeMapping(
        "tinytext",
        DbType.String,
        unicode: true);

    private static readonly RelationalTypeMapping s_textMapping = new MySqlStringTypeMapping(
        "text",
        DbType.String,
        unicode: true);

    private static readonly RelationalTypeMapping s_mediumBlobMapping =
        new ByteArrayTypeMapping("mediumblob", DbType.Binary);

    private static readonly RelationalTypeMapping s_tinyBlobMapping =
        new ByteArrayTypeMapping("tinyblob", DbType.Binary);

    private static readonly RelationalTypeMapping s_blobMapping = new ByteArrayTypeMapping("blob", DbType.Binary);

    private static readonly RelationalTypeMapping s_varBinaryMapping =
        new ByteArrayTypeMapping("varbinary", DbType.Binary);

    private static readonly RelationalTypeMapping s_charMapping = new MySqlStringTypeMapping(
        "char",
        DbType.StringFixedLength,
        unicode: true);

    private static readonly RelationalTypeMapping s_varCharMapping = new MySqlStringTypeMapping(
        "varchar",
        DbType.String,
        unicode: true);

    private static readonly RelationalTypeMapping s_bitMapping = new BoolTypeMapping("bit(1)", DbType.Boolean);

    private static readonly RelationalTypeMapping s_yearMapping = new ShortTypeMapping("year", DbType.Int16);

    // Geometry round-trips as MySQL well-known-binary (WKB). The richer
    // NetTopologySuite-backed mapping ships in the optional spatial package; the
    // bare byte-array mapping here lets reverse engineering scaffold geometry
    // columns without forcing the spatial dependency.
    private static readonly RelationalTypeMapping s_geometryMapping =
        new ByteArrayTypeMapping("geometry", DbType.Binary);

    private static readonly Dictionary<Type, RelationalTypeMapping> s_clrMappings = new()
    {
        [typeof(int)] = s_intMapping,
        [typeof(long)] = s_longMapping,
        [typeof(short)] = s_shortMapping,
        [typeof(sbyte)] = s_sbyteMapping,
        [typeof(byte)] = s_byteMapping,
        [typeof(ushort)] = s_ushortMapping,
        [typeof(uint)] = s_uintMapping,
        [typeof(ulong)] = s_ulongMapping,
        [typeof(bool)] = s_boolMapping,
        [typeof(double)] = s_doubleMapping,
        [typeof(float)] = s_floatMapping,
        [typeof(decimal)] = s_decimalMapping,
        [typeof(DateTime)] = s_dateTimeMapping,
        [typeof(DateOnly)] = s_dateOnlyMapping,
        [typeof(TimeOnly)] = s_timeOnlyMapping,
        [typeof(TimeSpan)] = s_timeSpanMapping,
        [typeof(char)] = s_charClrMapping,
        [typeof(Guid)] = s_guidBinaryMapping,
        [typeof(string)] = s_stringMapping,
        [typeof(byte[])] = s_byteArrayMapping,
        [typeof(JsonElement)] = s_jsonElementMapping,
        [typeof(JsonDocument)] = s_jsonDocumentMapping,
        [typeof(JsonNode)] = s_jsonNodeMapping,
        [typeof(JsonObject)] = s_jsonObjectMapping,
        [typeof(JsonArray)] = s_jsonArrayMapping,
    };

    // FrozenDictionary trades a higher one-time construction cost for the fastest
    // possible TryGetValue path; the store-type lookup runs on every cold-path
    // FindMapping that resolves an unannotated property, so the read-throughput
    // matters here more than the construction cost.
    private static readonly FrozenDictionary<string, RelationalTypeMapping> s_storeTypeMappings =
        new Dictionary<string, RelationalTypeMapping>(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = s_intMapping,
            ["integer"] = s_intMapping,
            ["bigint"] = s_longMapping,
            ["smallint"] = s_shortMapping,
            ["smallint unsigned"] = s_ushortMapping,
            ["mediumint"] = s_mediumIntMapping,
            ["mediumint unsigned"] = s_mediumIntUnsignedMapping,
            ["tinyint"] = s_sbyteMapping,
            ["tinyint unsigned"] = s_byteMapping,
            ["int unsigned"] = s_uintMapping,
            ["bigint unsigned"] = s_ulongMapping,
            ["float"] = s_floatMapping,
            ["double"] = s_doubleMapping,
            ["decimal"] = s_decimalMapping,
            ["datetime"] = s_dateTimeMapping,
            ["timestamp"] = s_timestampMapping,
            ["date"] = s_dateOnlyMapping,
            ["time"] = s_timeOnlyMapping,
            ["year"] = s_yearMapping,
            ["bit"] = s_bitMapping,
            ["binary"] = s_guidBinaryMapping,
            ["guid"] = s_guidBinaryMapping,
            ["varbinary"] = s_varBinaryMapping,
            ["longblob"] = s_byteArrayMapping,
            ["mediumblob"] = s_mediumBlobMapping,
            ["tinyblob"] = s_tinyBlobMapping,
            ["blob"] = s_blobMapping,
            ["json"] = s_jsonStringMapping,
            ["char"] = s_charMapping,
            ["varchar"] = s_varCharMapping,
            ["longtext"] = s_stringMapping,
            ["mediumtext"] = s_mediumTextMapping,
            ["tinytext"] = s_tinyTextMapping,
            ["text"] = s_textMapping,
            ["geometry"] = s_geometryMapping,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly MySqlSingletonOptions _mySqlSingletonOptions;

    public MySqlTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies, relationalDependencies)
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _mySqlSingletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    protected override RelationalTypeMapping? FindMapping(
        in RelationalTypeMappingInfo mappingInfo
    )
    {
        var clrType = mappingInfo.ClrType?.UnwrapNullableType();

        if (clrType == typeof(byte[])
            && mappingInfo.ElementTypeMapping is not null)
        {
            return base.FindMapping(mappingInfo);
        }

        if (clrType == typeof(MySqlServerVersion))
        {
            return s_serverVersionMapping;
        }

        if (clrType == typeof(Guid))
        {
            return CreateGuidMapping(mappingInfo);
        }

        if (clrType == typeof(char))
        {
            return string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName)
                ? s_charClrMapping
                : new MySqlCharTypeMapping(mappingInfo.StoreTypeName);
        }

        if (clrType == typeof(JsonTypePlaceholder))
        {
            return s_jsonContainerColumnMapping;
        }

        if (clrType == typeof(byte[])
            && mappingInfo.IsRowVersion == true)
        {
            return s_rowVersionMapping;
        }

        if (clrType is not null)
        {
            if (clrType == typeof(decimal))
            {
                return CreateDecimalMapping(mappingInfo);
            }

            if (clrType == typeof(DateTime))
            {
                return CreateDateTimeMapping(mappingInfo);
            }

            if (clrType == typeof(DateOnly))
            {
                return s_dateOnlyMapping;
            }

            if (clrType == typeof(TimeOnly))
            {
                return CreateTimeOnlyMapping(mappingInfo);
            }

            if (clrType == typeof(TimeSpan))
            {
                return CreateTimeSpanMapping(mappingInfo);
            }
        }

        // JSON CLR types: prefer the CLR-type-specific mapping even when a store type is specified,
        // so that ValueConverter and ValueComparer are preserved.
        if (clrType is not null
            && (clrType == typeof(JsonElement)
                || clrType == typeof(JsonDocument)
                || clrType == typeof(JsonNode)
                || clrType == typeof(JsonObject)
                || clrType == typeof(JsonArray))
            && s_clrMappings.TryGetValue(clrType, out var jsonClrMapping))
        {
            return jsonClrMapping;
        }

        if (!string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName))
        {
            var normalizedStoreType = NormalizeStoreTypeName(mappingInfo.StoreTypeName);

            if (normalizedStoreType == "tinyint")
            {
                return ResolveTinyIntMapping(mappingInfo);
            }

            if (normalizedStoreType == "bit")
            {
                return ResolveBitMapping(mappingInfo);
            }

            if (normalizedStoreType is "enum" or "set")
            {
                return new MySqlStringTypeMapping(
                    mappingInfo.StoreTypeName,
                    DbType.String,
                    unicode: true);
            }

            if (normalizedStoreType == "double"
                && mappingInfo.StoreTypeName.Contains("unsigned", StringComparison.OrdinalIgnoreCase))
            {
                return new MySqlDoubleTypeMapping(mappingInfo.StoreTypeName, DbType.Double);
            }

            if (normalizedStoreType == "float"
                && mappingInfo.StoreTypeName.Contains("unsigned", StringComparison.OrdinalIgnoreCase))
            {
                return new MySqlFloatTypeMapping(mappingInfo.StoreTypeName, DbType.Single);
            }

            if (clrType == typeof(byte[])
                && normalizedStoreType is "binary" or "varbinary" or "blob" or "longblob")
            {
                return CreateByteArrayMapping(mappingInfo, normalizedStoreType);
            }

            if (s_storeTypeMappings.TryGetValue(normalizedStoreType, out var storeTypeMapping)
                && (clrType is null || clrType == storeTypeMapping.ClrType))
            {
                return AdjustMappingForFacets(storeTypeMapping, mappingInfo, normalizedStoreType);
            }
        }

        if (clrType is not null
            && s_clrMappings.TryGetValue(clrType, out var clrTypeMapping))
        {
            return AdjustMappingForFacets(clrTypeMapping, mappingInfo, null);
        }

        return base.FindMapping(mappingInfo);
    }

    private static RelationalTypeMapping ResolveTinyIntMapping(
        RelationalTypeMappingInfo mappingInfo
    )
    {
        var clrType = mappingInfo.ClrType?.UnwrapNullableType();

        if (clrType == typeof(bool)
            || IsBooleanTinyIntStoreType(mappingInfo.StoreTypeName))
        {
            return s_boolMapping;
        }

        return s_sbyteMapping;
    }

    private static RelationalTypeMapping ResolveBitMapping(
        RelationalTypeMappingInfo mappingInfo
    )
    {
        var storeType = mappingInfo.StoreTypeName;

        if (string.IsNullOrWhiteSpace(storeType)
            || storeType.Trim().Equals("bit", StringComparison.OrdinalIgnoreCase)
            || storeType.Trim().Equals("bit(1)", StringComparison.OrdinalIgnoreCase))
        {
            return s_bitMapping;
        }

        return new ULongTypeMapping(storeType, DbType.UInt64);
    }

    private string NormalizeStoreTypeName(
        string storeTypeName
    )
    {
        bool? unicode = null;
        int? size = null;
        int? precision = null;
        int? scale = null;

        var baseStoreType = ParseStoreTypeName(storeTypeName, ref unicode, ref size, ref precision, ref scale);

        return baseStoreType
            .Trim()
            .ToLowerInvariant();
    }

    private static bool IsBooleanTinyIntStoreType(
        string? storeTypeName
    )
    {
        if (string.IsNullOrWhiteSpace(storeTypeName))
        {
            return false;
        }

        var normalizedStoreType = storeTypeName
            .Trim()
            .ToLowerInvariant();

        return normalizedStoreType == "tinyint(1)" || normalizedStoreType == "tinyint(1) unsigned";
    }

    private RelationalTypeMapping AdjustMappingForFacets(
        RelationalTypeMapping mapping,
        RelationalTypeMappingInfo mappingInfo,
        string? normalizedStoreType
    )
    {
        var clrType = mappingInfo.ClrType?.UnwrapNullableType();

        if (normalizedStoreType is "char" or "varchar" or "text" or "longtext"
            || (clrType == typeof(string) && normalizedStoreType != "json"))
        {
            return CreateStringMapping(mappingInfo, normalizedStoreType);
        }

        if (clrType == typeof(byte[])
            || normalizedStoreType is "binary" or "varbinary" or "blob" or "longblob")
        {
            return CreateByteArrayMapping(mappingInfo, normalizedStoreType);
        }

        if (clrType == typeof(decimal)
            || normalizedStoreType == "decimal")
        {
            return CreateDecimalMapping(mappingInfo);
        }

        if (clrType == typeof(DateTime)
            || normalizedStoreType is "datetime" or "timestamp")
        {
            return CreateDateTimeMapping(mappingInfo, normalizedStoreType);
        }

        if (clrType == typeof(TimeOnly)
            || normalizedStoreType == "time")
        {
            return CreateTimeOnlyMapping(mappingInfo);
        }

        if (clrType == typeof(TimeSpan))
        {
            return CreateTimeSpanMapping(mappingInfo);
        }

        return mapping;
    }

    private RelationalTypeMapping CreateGuidMapping(
        RelationalTypeMappingInfo mappingInfo
    )
    {
        var normalizedStoreType = !string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName)
            ? NormalizeStoreTypeName(mappingInfo.StoreTypeName)
            : null;

        var size = mappingInfo.Size;

        var mapping = normalizedStoreType switch
        {
            "char" => size == 36
                ? s_guidChar36Mapping
                : new StringTypeMapping(
                    $"char({size.GetValueOrDefault(36)})",
                    DbType.StringFixedLength,
                    unicode: true,
                    size),
            "varchar" => size == 36
                ? s_guidVarchar36Mapping
                : new StringTypeMapping($"varchar({size.GetValueOrDefault(36)})", DbType.String, unicode: true, size),
            "binary" => size == 16
                ? s_guidBinaryMapping
                : new MySqlGuidBinaryTypeMapping($"binary({size.GetValueOrDefault(16)})"),
            _ => _mySqlSingletonOptions.DefaultGuidFormat == MySqlGuidFormat.Char36
                ? s_guidChar36Mapping
                : s_guidBinaryMapping
        };

        return mapping.ClrType == typeof(string)
            ? (RelationalTypeMapping)mapping.WithComposedConverter(new GuidToStringConverter())
            : mapping;
    }

    private static StringTypeMapping CreateStringMapping(
        RelationalTypeMappingInfo mappingInfo,
        string? normalizedStoreType
    )
    {
        var explicitTextMapping = normalizedStoreType switch
        {
            "tinytext" => s_tinyTextMapping,
            "text" => s_textMapping,
            "mediumtext" => s_mediumTextMapping,
            "longtext" => s_stringMapping,
            _ => null,
        };

        if (explicitTextMapping is not null)
        {
            return (StringTypeMapping)explicitTextMapping;
        }

        var size = mappingInfo.Size
            ?? (mappingInfo.IsKeyOrIndex ? DefaultKeyOrIndexLength : null);

        var isFixedLength =
            (mappingInfo.IsFixedLength == true || normalizedStoreType == "char")
            && size is > 0;

        if (size == 36
            && normalizedStoreType is "char" or "varchar")
        {
            return new MySqlGuidStringTypeMapping(
                $"{normalizedStoreType}(36)",
                normalizedStoreType == "char" ? DbType.StringFixedLength : DbType.String,
                size.Value,
                useKeyComparison: mappingInfo.IsKeyOrIndex);
        }

        if (isFixedLength)
        {
            return new MySqlStringTypeMapping(
                $"char({size})",
                DbType.StringFixedLength,
                unicode: true,
                size,
                useKeyComparison: mappingInfo.IsKeyOrIndex);
        }

        if (normalizedStoreType is null
            && size > MaximumUtf8Mb4VarCharLength)
        {
            return (StringTypeMapping)s_stringMapping;
        }

        return size is > 0
            ? new MySqlStringTypeMapping(
                $"varchar({size.Value})",
                DbType.String,
                unicode: true,
                size.Value,
                useKeyComparison: mappingInfo.IsKeyOrIndex)
            : new MySqlStringTypeMapping(
                "longtext",
                DbType.String,
                unicode: true,
                useKeyComparison: mappingInfo.IsKeyOrIndex);
    }

    private ByteArrayTypeMapping CreateByteArrayMapping(
        RelationalTypeMappingInfo mappingInfo,
        string? normalizedStoreType
    )
    {
        var explicitBlobMapping = normalizedStoreType switch
        {
            "tinyblob" => s_tinyBlobMapping,
            "blob" => s_blobMapping,
            "mediumblob" => s_mediumBlobMapping,
            "longblob" => s_byteArrayMapping,
            _ => null,
        };

        if (explicitBlobMapping is not null)
        {
            return (ByteArrayTypeMapping)explicitBlobMapping;
        }

        var size = mappingInfo.Size ?? (mappingInfo.IsKeyOrIndex ? DefaultKeyOrIndexLength : null);

        if (normalizedStoreType == "binary"
            && !string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName))
        {
            return new ByteArrayTypeMapping(
                mappingInfo.StoreTypeName,
                DbType.Binary,
                size);
        }

        var storeSize =
            size == 16
            && normalizedStoreType is null
            && _mySqlSingletonOptions.DefaultGuidFormat == MySqlGuidFormat.Binary16
                ? 17
                : size;

        // MySqlConnector's Binary16 mode materializes every implicit 16-byte
        // binary column as Guid. VARBINARY(17) still stores a 16-byte payload
        // without padding or extra data, while keeping ordinary byte[] and
        // Guid-to-byte converters on the driver's binary-reader path.
        var storeType = storeSize is > 0 ? $"varbinary({storeSize.Value})" : "longblob";

        return new ByteArrayTypeMapping(storeType, DbType.Binary, storeSize is > 0 ? storeSize : null);
    }

    private static RelationalTypeMapping CreateEnumMapping(
        Type clrType
    )
    {
        ArgumentNullException.ThrowIfNull(clrType);

        var underlyingType = Enum.GetUnderlyingType(clrType);

        if (s_clrMappings.TryGetValue(underlyingType, out var mapping))
        {
            return mapping;
        }

        throw new InvalidOperationException(
            $"The enum CLR type '{clrType.FullName ?? clrType.Name}' uses the unsupported "
            + $"underlying type '{underlyingType.FullName ?? underlyingType.Name}'.");
    }

    private static DecimalTypeMapping CreateDecimalMapping(
        RelationalTypeMappingInfo mappingInfo
    )
    {
        if (!string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName))
        {
            return new DecimalTypeMapping(
                mappingInfo.StoreTypeName,
                DbType.Decimal,
                mappingInfo.Precision,
                mappingInfo.Scale);
        }

        var precision = mappingInfo.Precision;
        var scale = mappingInfo.Scale;
        var storeType = CreateDecimalStoreType(precision, scale);

        return new DecimalTypeMapping(storeType, DbType.Decimal, precision, scale);
    }

    private static string CreateDecimalStoreType(
        int? precision,
        int? scale
    )
    {
        if (precision is null
            && scale is null)
        {
            return $"decimal({DefaultDecimalPrecision},{DefaultDecimalScale})";
        }

        if (precision is not null
            && scale is not null)
        {
            return $"decimal({precision.Value},{scale.Value})";
        }

        return precision is not null
            ? $"decimal({precision.Value})"
            : $"decimal({DefaultDecimalPrecision},{scale!.Value})";
    }

    private static MySqlDateTimeTypeMapping CreateDateTimeMapping(
        RelationalTypeMappingInfo mappingInfo,
        string? normalizedStoreType = null
    )
    {
        if (!string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName))
        {
            return new MySqlDateTimeTypeMapping(mappingInfo.StoreTypeName);
        }

        var precision = mappingInfo.Precision ?? DefaultDateTimePrecision;
        var storeTypeBase = normalizedStoreType == "timestamp" ? "timestamp" : "datetime";

        return new MySqlDateTimeTypeMapping($"{storeTypeBase}({precision})");
    }

    private static MySqlTimeOnlyTypeMapping CreateTimeOnlyMapping(
        RelationalTypeMappingInfo mappingInfo
    )
    {
        if (!string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName))
        {
            return new MySqlTimeOnlyTypeMapping(
                mappingInfo.StoreTypeName,
                GetExplicitTimePrecision(mappingInfo.StoreTypeName));
        }

        if (mappingInfo.Precision is null)
        {
            return s_timeOnlyMapping;
        }

        var precision = mappingInfo.Precision ?? DefaultTimePrecision;

        return new MySqlTimeOnlyTypeMapping($"time({precision})", precision);
    }

    private static MySqlTimeSpanTypeMapping CreateTimeSpanMapping(
        RelationalTypeMappingInfo mappingInfo
    )
    {
        if (!string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName))
        {
            return new MySqlTimeSpanTypeMapping(
                mappingInfo.StoreTypeName,
                GetExplicitTimePrecision(mappingInfo.StoreTypeName));
        }

        if (mappingInfo.Precision is null)
        {
            return s_timeSpanMapping;
        }

        var precision = mappingInfo.Precision ?? DefaultTimePrecision;

        return new MySqlTimeSpanTypeMapping($"time({precision})", precision);
    }

    private static int GetExplicitTimePrecision(
        string storeType
    )
    {
        var openParenthesis = storeType.IndexOf('(');
        if (openParenthesis < 0)
        {
            return 0;
        }

        var closeParenthesis = storeType.IndexOf(')', openParenthesis + 1);
        if (closeParenthesis <= openParenthesis + 1
            || !int.TryParse(
                storeType.AsSpan(openParenthesis + 1, closeParenthesis - openParenthesis - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var precision))
        {
            throw new InvalidOperationException(
                $"The MySQL-family time store type '{storeType}' has an invalid fractional-seconds precision.");
        }

        return precision;
    }
}
