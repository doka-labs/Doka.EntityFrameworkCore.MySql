namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlTypeMappingSource : RelationalTypeMappingSource
{
    private const int DefaultDateTimePrecision = 6;
    private const int DefaultTimePrecision = 6;
    private const int DefaultDecimalPrecision = 65;
    private const int DefaultDecimalScale = 30;

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
    private static readonly RelationalTypeMapping s_doubleMapping = new DoubleTypeMapping("double", DbType.Double);
    private static readonly RelationalTypeMapping s_floatMapping = new FloatTypeMapping("float", DbType.Single);

    private static readonly RelationalTypeMapping s_decimalMapping = new DecimalTypeMapping(
        "decimal(65,30)",
        DbType.Decimal,
        DefaultDecimalPrecision,
        DefaultDecimalScale);

    private static readonly RelationalTypeMapping s_dateTimeMapping =
        new DateTimeTypeMapping("datetime(6)", DbType.DateTime);

    private static readonly RelationalTypeMapping s_timestampMapping =
        new DateTimeTypeMapping("timestamp(6)", DbType.DateTime);

    private static readonly RelationalTypeMapping s_dateOnlyMapping = new DateOnlyTypeMapping("date", DbType.Date);
    private static readonly RelationalTypeMapping s_timeOnlyMapping = new TimeOnlyTypeMapping("time(6)", DbType.Time);
    private static readonly RelationalTypeMapping s_timeSpanMapping = new TimeSpanTypeMapping("time(6)", DbType.Time);
    private static readonly RelationalTypeMapping s_guidBinaryMapping = new GuidTypeMapping("binary(16)", DbType.Guid);

    private static readonly RelationalTypeMapping s_guidChar36Mapping =
        new StringTypeMapping("char(36)", DbType.StringFixedLength, unicode: true, size: 36);

    private static readonly RelationalTypeMapping s_guidVarchar36Mapping =
        new StringTypeMapping("varchar(36)", DbType.String, unicode: true, size: 36);

    private static readonly RelationalTypeMapping s_jsonStringMapping = new MySqlJsonStringTypeMapping("json");

    private static readonly RelationalTypeMapping
        s_jsonElementMapping = MySqlJsonTypeMapping.CreateJsonElementMapping();

    private static readonly RelationalTypeMapping s_jsonDocumentMapping =
        MySqlJsonTypeMapping.CreateJsonDocumentMapping();

    private static readonly RelationalTypeMapping s_jsonNodeMapping = MySqlJsonTypeMapping.CreateJsonNodeMapping();
    private static readonly RelationalTypeMapping s_jsonObjectMapping = MySqlJsonTypeMapping.CreateJsonObjectMapping();
    private static readonly RelationalTypeMapping s_jsonArrayMapping = MySqlJsonTypeMapping.CreateJsonArrayMapping();
    private static readonly RelationalTypeMapping s_serverVersionMapping = new MySqlServerVersionTypeMapping();

    private static readonly RelationalTypeMapping s_stringMapping = new StringTypeMapping(
        "longtext",
        DbType.String,
        unicode: true);

    private static readonly RelationalTypeMapping s_byteArrayMapping =
        new ByteArrayTypeMapping("longblob", DbType.Binary);

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
        [typeof(Guid)] = s_guidBinaryMapping,
        [typeof(string)] = s_stringMapping,
        [typeof(byte[])] = s_byteArrayMapping,
        [typeof(JsonElement)] = s_jsonElementMapping,
        [typeof(JsonDocument)] = s_jsonDocumentMapping,
        [typeof(JsonNode)] = s_jsonNodeMapping,
        [typeof(JsonObject)] = s_jsonObjectMapping,
        [typeof(JsonArray)] = s_jsonArrayMapping,
    };

    private static readonly Dictionary<string, RelationalTypeMapping> s_storeTypeMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = s_intMapping,
            ["integer"] = s_intMapping,
            ["bigint"] = s_longMapping,
            ["smallint"] = s_shortMapping,
            ["smallint unsigned"] = s_ushortMapping,
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
            ["binary"] = s_guidBinaryMapping,
            ["guid"] = s_guidBinaryMapping,
            ["varbinary"] = new ByteArrayTypeMapping("varbinary", DbType.Binary),
            ["longblob"] = new ByteArrayTypeMapping("longblob", DbType.Binary),
            ["blob"] = new ByteArrayTypeMapping("blob", DbType.Binary),
            ["json"] = s_jsonStringMapping,
            ["char"] = new StringTypeMapping("char", DbType.StringFixedLength, unicode: true),
            ["varchar"] = new StringTypeMapping("varchar", DbType.String, unicode: true),
            ["longtext"] = new StringTypeMapping("longtext", DbType.String, unicode: true),
            ["text"] = new StringTypeMapping("text", DbType.String, unicode: true),
        };

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
        var clrType = mappingInfo.ClrType is null ? null : UnwrapNullableType(mappingInfo.ClrType);

        if (clrType == typeof(MySqlServerVersion))
        {
            return s_serverVersionMapping;
        }

        if (clrType == typeof(Guid))
        {
            return CreateGuidMapping(mappingInfo);
        }

        if (clrType is not null)
        {
            if (clrType.IsEnum)
            {
                return CreateEnumMapping(clrType);
            }

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

            if (s_storeTypeMappings.TryGetValue(normalizedStoreType, out var storeTypeMapping))
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
        var clrType = mappingInfo.ClrType is null ? null : UnwrapNullableType(mappingInfo.ClrType);

        if (clrType == typeof(bool)
            || IsBooleanTinyIntStoreType(mappingInfo.StoreTypeName))
        {
            return s_boolMapping;
        }

        return s_sbyteMapping;
    }

    protected override RelationalTypeMapping? FindCollectionMapping(
        RelationalTypeMappingInfo info,
        Type modelType,
        Type? providerType,
        CoreTypeMapping? elementMapping
    ) => null;

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

    private static RelationalTypeMapping AdjustMappingForFacets(
        RelationalTypeMapping mapping,
        RelationalTypeMappingInfo mappingInfo,
        string? normalizedStoreType
    )
    {
        var clrType = mappingInfo.ClrType is null ? null : UnwrapNullableType(mappingInfo.ClrType);

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

        return normalizedStoreType switch
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
                : new GuidTypeMapping($"binary({size.GetValueOrDefault(16)})", DbType.Guid),
            _ => _mySqlSingletonOptions.DefaultGuidFormat == MySqlGuidFormat.Char36
                ? s_guidChar36Mapping
                : s_guidBinaryMapping
        };
    }

    private static StringTypeMapping CreateStringMapping(
        RelationalTypeMappingInfo mappingInfo,
        string? normalizedStoreType
    )
    {
        var isFixedLength = mappingInfo.IsFixedLength == true || normalizedStoreType == "char";
        var size = mappingInfo.Size;

        if (isFixedLength)
        {
            var resolvedSize = size.GetValueOrDefault(1);

            return new StringTypeMapping(
                $"char({resolvedSize})",
                DbType.StringFixedLength,
                unicode: true,
                resolvedSize);
        }

        return size is > 0
            ? new StringTypeMapping($"varchar({size.Value})", DbType.String, unicode: true, size.Value)
            : new StringTypeMapping("longtext", DbType.String, unicode: true);
    }

    private static ByteArrayTypeMapping CreateByteArrayMapping(
        RelationalTypeMappingInfo mappingInfo,
        string? normalizedStoreType
    )
    {
        var size = mappingInfo.Size;
        var storeType = normalizedStoreType == "binary" && size == 16 ? "binary(16)" :
            size is > 0 ? $"varbinary({size.Value})" : "longblob";

        return new ByteArrayTypeMapping(storeType, DbType.Binary, size);
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
            $"The enum CLR type '{clrType.FullName ?? clrType.Name}' uses the unsupported underlying type '{underlyingType.FullName ?? underlyingType.Name}'.");
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

    private static DateTimeTypeMapping CreateDateTimeMapping(
        RelationalTypeMappingInfo mappingInfo,
        string? normalizedStoreType = null
    )
    {
        if (!string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName))
        {
            return new DateTimeTypeMapping(mappingInfo.StoreTypeName, DbType.DateTime);
        }

        var precision = mappingInfo.Precision ?? DefaultDateTimePrecision;
        var storeTypeBase = normalizedStoreType == "timestamp" ? "timestamp" : "datetime";

        return new DateTimeTypeMapping($"{storeTypeBase}({precision})", DbType.DateTime);
    }

    private static TimeOnlyTypeMapping CreateTimeOnlyMapping(
        RelationalTypeMappingInfo mappingInfo
    )
    {
        if (!string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName))
        {
            return new TimeOnlyTypeMapping(mappingInfo.StoreTypeName, DbType.Time);
        }

        var precision = mappingInfo.Precision ?? DefaultTimePrecision;

        return new TimeOnlyTypeMapping($"time({precision})", DbType.Time);
    }

    private static TimeSpanTypeMapping CreateTimeSpanMapping(
        RelationalTypeMappingInfo mappingInfo
    )
    {
        if (!string.IsNullOrWhiteSpace(mappingInfo.StoreTypeName))
        {
            return new TimeSpanTypeMapping(mappingInfo.StoreTypeName, DbType.Time);
        }

        var precision = mappingInfo.Precision ?? DefaultTimePrecision;

        return new TimeSpanTypeMapping($"time({precision})", DbType.Time);
    }

    private static Type UnwrapNullableType(
        Type type
    )
    {
        ArgumentNullException.ThrowIfNull(type);

        return Nullable.GetUnderlyingType(type) ?? type;
    }
}
