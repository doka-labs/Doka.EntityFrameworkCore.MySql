namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Preserves the distinction between JSON text representation and relational
/// storage representation for <c>JSON_TABLE</c> value columns.
/// </summary>
internal static class MySqlJsonTableValueEncoding
{
    private static readonly ConditionalWeakTable<RelationalTypeMapping, RelationalTypeMapping>
        s_parameterElementTypeMappings = [];

    /// <summary>
    /// Selects a lossless SQL extraction mapping for a scalar stored inside a
    /// JSON document.
    /// </summary>
    public static RelationalTypeMapping GetDocumentExtractionTypeMapping(
        RelationalTypeMapping propertyTypeMapping,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        var providerType = (propertyTypeMapping.Converter?.ProviderClrType ?? propertyTypeMapping.ClrType)
            .UnwrapNullableType();

        if (providerType == typeof(Guid)
            && propertyTypeMapping is MySqlGuidBinaryTypeMapping)
        {
            return typeMappingSource.FindMapping(typeof(string), "char(36)")
                ?? throw new InvalidOperationException(
                    "A textual JSON document extraction mapping for binary GUID values could not be found.");
        }

        if (providerType == typeof(byte[]))
        {
            return typeMappingSource.FindMapping(typeof(string), "longtext")
                ?? throw new InvalidOperationException(
                    "A textual JSON document extraction mapping for binary values could not be found.");
        }

        var storeType = providerType == typeof(string)
            ? "longtext"
            : providerType == typeof(decimal)
                ? "decimal(65,30)"
                : providerType == typeof(DateTime)
                    ? "datetime(6)"
                    : providerType == typeof(DateOnly)
                        ? "date"
                        : providerType == typeof(TimeOnly)
                            ? "time(6)"
                            : providerType == typeof(TimeSpan)
                                ? "longtext"
                                : null;

        if (storeType is null)
        {
            // Fixed-width numeric and boolean provider mappings do not carry
            // a facet that can narrow their JSON value during extraction.
            return propertyTypeMapping;
        }

        // Relational property facets describe columns, not members embedded
        // in a JSON document. Widen the JSON_TABLE boundary so it cannot
        // truncate or round the document value before EF applies the actual
        // property mapping and converter to the projected column.
        return typeMappingSource.FindMapping(providerType, storeType)
            ?? throw new InvalidOperationException(
                $"A lossless JSON document extraction mapping for '{providerType}' could not be found.");
    }

    /// <summary>
    /// Selects the element mapping used to serialize a parameter collection.
    /// </summary>
    public static RelationalTypeMapping GetParameterElementTypeMapping(
        RelationalTypeMapping elementTypeMapping,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        var elementType = elementTypeMapping.ClrType.UnwrapNullableType();
        var providerType = (elementTypeMapping.Converter?.ProviderClrType ?? elementType).UnwrapNullableType();

        if (!UsesBase64StringTransport(elementType, elementTypeMapping)
            && providerType != typeof(TimeSpan))
        {
            return elementTypeMapping;
        }

        if (s_parameterElementTypeMappings.TryGetValue(elementTypeMapping, out var cachedMapping))
        {
            return cachedMapping;
        }

        var createdMapping = CreateParameterElementTypeMapping(elementTypeMapping, typeMappingSource);

        // EF includes the element mapping by reference in its collection
        // mapping cache key. Reuse one transport clone per inferred mapping so
        // dynamic query shapes cannot grow that cache with equivalent clones.
        return s_parameterElementTypeMappings.GetValue(elementTypeMapping, _ => createdMapping);
    }

    /// <summary>
    /// Selects the mapping used by <c>JSON_TABLE</c> before the value is decoded
    /// into its relational representation.
    /// </summary>
    public static RelationalTypeMapping? GetExtractionTypeMapping(
        Type elementType,
        RelationalTypeMapping? elementTypeMapping,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        if (elementTypeMapping is null)
        {
            return null;
        }

        if (elementType == typeof(Guid)
            && elementTypeMapping is MySqlGuidBinaryTypeMapping { Converter: null })
        {
            return typeMappingSource.FindMapping(typeof(string), "char(36)");
        }

        if (elementType == typeof(byte[]))
        {
            return typeMappingSource.FindMapping(typeof(string), "longtext");
        }

        if (elementType == typeof(Guid)
            && elementTypeMapping is MySqlGuidStringTypeMapping)
        {
            return typeMappingSource.FindMapping(typeof(string), "char(36)");
        }

        var providerType = (elementTypeMapping.Converter?.ProviderClrType ?? elementType).UnwrapNullableType();

        if (providerType == typeof(Guid)
            && elementTypeMapping is MySqlGuidBinaryTypeMapping)
        {
            return typeMappingSource.FindMapping(typeof(string), "char(36)");
        }

        if (providerType == typeof(byte[]))
        {
            return typeMappingSource.FindMapping(typeof(string), "longtext");
        }

        string? storeType = null;

        if (UsesBase64StringTransport(elementType, elementTypeMapping)
            || providerType == typeof(string))
        {
            storeType = "longtext";
        }
        else if (providerType == typeof(decimal))
        {
            storeType = "decimal(65,30)";
        }
        else if (providerType == typeof(DateTime))
        {
            // DATE has no fractional-seconds form, and TIMESTAMP would apply
            // session time-zone conversion to an intermediate value. DATETIME
            // preserves the complete comparison value for every DateTime
            // storage family before the property mapping participates.
            storeType = "datetime(6)";
        }
        else if (providerType == typeof(DateOnly))
        {
            storeType = "date";
        }
        else if (providerType == typeof(TimeOnly))
        {
            storeType = "time(6)";
        }
        else if (providerType == typeof(TimeSpan))
        {
            // EF Core's JSON reader/writer emits a separate day segment above
            // 24 hours. Extract it as text so decoding can retain MySQL's full
            // elapsed-time range instead of treating the day as an hour.
            storeType = "longtext";
        }

        var extractionTypeMapping = storeType is null
            ? typeMappingSource.FindMapping(providerType)
            : typeMappingSource.FindMapping(providerType, storeType);

        return extractionTypeMapping
            ?? throw new InvalidOperationException(
                $"A lossless JSON_TABLE extraction mapping for '{providerType}' could not be found.");
    }

    /// <summary>
    /// Returns whether JSON text requires an explicit conversion before it can
    /// be compared with the inferred relational element mapping.
    /// </summary>
    public static bool RequiresDecoding(
        Type elementType,
        RelationalTypeMapping elementTypeMapping
    )
    {
        var providerType = (elementTypeMapping.Converter?.ProviderClrType ?? elementType).UnwrapNullableType();

        return providerType == typeof(string)
            || providerType == typeof(byte[])
            || elementTypeMapping.Converter is not null
            || (providerType == typeof(Guid) && elementTypeMapping is MySqlGuidStringTypeMapping)
            || (providerType == typeof(Guid) && elementTypeMapping is MySqlGuidBinaryTypeMapping)
            || providerType == typeof(TimeSpan);
    }

    /// <summary>
    /// Converts one extracted JSON value into the representation expected by
    /// its relational element mapping.
    /// </summary>
    public static SqlExpression Decode(
        ColumnExpression valueColumn,
        Type elementType,
        RelationalTypeMapping? elementTypeMapping,
        ISqlExpressionFactory sqlExpressionFactory,
        bool usesBase64StringTransport = false
    )
    {
        if (elementTypeMapping is null)
        {
            // Parameter collections remain untyped until EF Core infers their
            // element mapping from the consumer. Wrapping the column here would
            // hide it from the relational type-mapping scanner.
            return valueColumn;
        }

        if (usesBase64StringTransport)
        {
            // Base64 keeps JSON string transport independent of the server's
            // backslash-escape mode. The SQL emitter restores UTF-8 text with
            // weaker coercibility so the model column's collation still wins.
            return sqlExpressionFactory.Function(
                MySqlSentinelContract.GetName(MySqlSentinelKind.StringJsonDecode),
                [valueColumn],
                nullable: true,
                argumentsPropagateNullability: [true],
                elementType,
                elementTypeMapping);
        }

        var providerType = (elementTypeMapping.Converter?.ProviderClrType ?? elementType).UnwrapNullableType();

        if (providerType == typeof(string)
            || (providerType == typeof(Guid) && elementTypeMapping is MySqlGuidStringTypeMapping))
        {
            var quoted = sqlExpressionFactory.Function(
                "JSON_QUOTE",
                [valueColumn],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(string),
                valueColumn.TypeMapping);

            // JSON_TABLE already returns SQL text. Quoting before unquoting is
            // an identity transformation for every string, including values
            // that themselves resemble JSON literals, while retaining the
            // coercible collation needed for comparison with model columns.
            return sqlExpressionFactory.Function(
                "JSON_UNQUOTE",
                [quoted],
                nullable: true,
                argumentsPropagateNullability: [true],
                elementType,
                elementTypeMapping);
        }

        if (providerType == typeof(byte[]))
        {
            return sqlExpressionFactory.Function(
                "FROM_BASE64",
                [valueColumn],
                nullable: true,
                argumentsPropagateNullability: [true],
                elementType,
                elementTypeMapping);
        }

        if (providerType == typeof(TimeSpan))
        {
            return DecodeTimeSpan(valueColumn, elementType, elementTypeMapping, sqlExpressionFactory);
        }

        if (providerType != typeof(Guid)
            || elementTypeMapping is not MySqlGuidBinaryTypeMapping)
        {
            if (elementTypeMapping.Converter is null)
            {
                return valueColumn;
            }

            // JSON_TABLE exposes the provider value, while the surrounding
            // query still reasons in the model type. Reapply the original
            // mapping here so its converter remains the sole model/provider
            // boundary after the lossless provider-type extraction.
            return new ColumnExpression(
                valueColumn.Name,
                valueColumn.TableAlias,
                elementType,
                elementTypeMapping,
                valueColumn.IsNullable);
        }

        var normalized = sqlExpressionFactory.Function(
            "REPLACE",
            [
                valueColumn,
                sqlExpressionFactory.Constant("-", valueColumn.TypeMapping),
                sqlExpressionFactory.Constant(string.Empty, valueColumn.TypeMapping),
            ],
            nullable: true,
            argumentsPropagateNullability: [true, false, false,],
            typeof(string),
            valueColumn.TypeMapping);

        return sqlExpressionFactory.Function(
            "UNHEX",
            [normalized],
            nullable: true,
            argumentsPropagateNullability: [true],
            elementType,
            elementTypeMapping);
    }

    private static SqlExpression DecodeTimeSpan(
        ColumnExpression valueColumn,
        Type resultType,
        RelationalTypeMapping resultTypeMapping,
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        // A sentinel keeps the text operand mapped as text while the emitted
        // SQL returns TIME. Letting generic function inference see both types
        // would incorrectly apply the TimeSpan mapping to string constants.
        return sqlExpressionFactory.Function(
            MySqlSentinelContract.GetName(MySqlSentinelKind.TimeSpanJsonDecode),
            [valueColumn],
            nullable: true,
            argumentsPropagateNullability: [true],
            resultType,
            resultTypeMapping);
    }

    /// <summary>
    /// Returns whether the parameter element uses Base64-encoded UTF-8 JSON
    /// transport before relational comparison.
    /// </summary>
    public static bool UsesBase64StringTransport(
        Type elementType,
        RelationalTypeMapping elementTypeMapping
    )
    {
        var providerType = (elementTypeMapping.Converter?.ProviderClrType ?? elementType).UnwrapNullableType();

        // Canonical GUID text contains no SQL-mode-sensitive escapes and has
        // its own Char36 decode path. Every other string provider value needs
        // the same safe transport, including enums and value objects.
        return providerType == typeof(string) && elementType.UnwrapNullableType() != typeof(Guid);
    }

    private static RelationalTypeMapping CreateParameterElementTypeMapping(
        RelationalTypeMapping elementTypeMapping,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        var elementType = elementTypeMapping.ClrType.UnwrapNullableType();
        var providerType = (elementTypeMapping.Converter?.ProviderClrType ?? elementType).UnwrapNullableType();
        JsonValueReaderWriter providerReaderWriter;

        if (UsesBase64StringTransport(elementType, elementTypeMapping))
        {
            providerReaderWriter = MySqlBase64StringJsonValueReaderWriter.Instance;
        }
        else if (providerType == typeof(TimeSpan))
        {
            providerReaderWriter = MySqlTimeSpanJsonValueReaderWriter.Instance;
        }
        else
        {
            throw new InvalidOperationException(
                $"No parameter JSON transport is defined for provider type '{providerType}'.");
        }

        var providerMapping = typeMappingSource.FindMapping(providerType, elementTypeMapping.StoreType)
            ?? throw new InvalidOperationException(
                $"A provider mapping for '{providerType}' and store type "
                + $"'{elementTypeMapping.StoreType}' could not be found.");
        var transportProviderMapping = (RelationalTypeMapping)providerMapping.Clone(
            jsonValueReaderWriter: providerReaderWriter);

        // Only the parameter's JSON representation changes. The inferred
        // relational mapping remains authoritative for the final comparison.
        if (elementTypeMapping.Converter is null)
        {
            return transportProviderMapping;
        }

        // Let EF Core compose the application converter around the provider
        // reader/writer. This is its supported path for arbitrary model types
        // and preserves compiled-model reconstruction semantics.
        return (RelationalTypeMapping)transportProviderMapping.WithComposedConverter(
            elementTypeMapping.Converter,
            elementTypeMapping.Comparer,
            elementTypeMapping.KeyComparer,
            elementTypeMapping.ElementTypeMapping);
    }
}
