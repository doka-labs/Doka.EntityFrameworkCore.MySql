namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Serializes relational scalars and composes partial JSON update setters.</summary>
internal sealed partial class MySqlQueryableMethodTranslatingExpressionVisitor
{
    /// <summary>
    /// Serializes a relational scalar expression through the engines' JSON
    /// constructor. This covers column-to-JSON updates for provider types whose
    /// JSON representation differs from their relational representation, such as
    /// temporal, binary and GUID values.
    /// </summary>
    protected override bool TrySerializeScalarToJson(
        JsonScalarExpression target,
        SqlExpression value,
        [NotNullWhen(true)] out SqlExpression? jsonValue
    )
    {
#pragma warning disable EF9002 // The provider override must compose EF Core's experimental JSON scalar serializer.
        if (base.TrySerializeScalarToJson(target, value, out jsonValue))
        {
            return true;
        }
#pragma warning restore EF9002

        var jsonTypeMapping = target.Json.TypeMapping;
        var valueForJsonObject = NormalizeRelationalScalarForJson(target.Type, value);
        var jsonObject = _sqlExpressionFactory.Function(
            "JSON_OBJECT",
            [
                _sqlExpressionFactory.Constant("value"),
                valueForJsonObject,
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                false,
                true,
            ],
            typeof(string),
            jsonTypeMapping);

        jsonValue = _sqlExpressionFactory.Function(
            "JSON_EXTRACT",
            [
                jsonObject,
                _sqlExpressionFactory.Constant("$.value"),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false,
            ],
            typeof(string),
            jsonTypeMapping);

        return true;
    }

    private SqlExpression NormalizeRelationalScalarForJson(
        Type targetType,
        SqlExpression value
    )
    {
        var nonNullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var stringTypeMapping = _typeMappingSource.FindMapping(typeof(string));

        if (nonNullableTargetType == typeof(DateTime))
        {
            return _sqlExpressionFactory.Function(
                "DATE_FORMAT",
                [
                    value,
                    _sqlExpressionFactory.Constant("%Y-%m-%dT%H:%i:%s.%f", stringTypeMapping),
                ],
                nullable: true,
                argumentsPropagateNullability:
                [
                    true,
                    false,
                ],
                typeof(string),
                stringTypeMapping);
        }

        if (nonNullableTargetType != typeof(byte[]))
        {
            return nonNullableTargetType == typeof(Guid)
                && value.TypeMapping?.StoreType.StartsWith("binary", StringComparison.OrdinalIgnoreCase) == true
                    ? FormatBinaryGuid(value, stringTypeMapping)
                    : value;
        }

        var base64 = _sqlExpressionFactory.Function(
            "TO_BASE64",
            [value],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(string),
            stringTypeMapping);

        return _sqlExpressionFactory.Function(
            "REPLACE",
            [
                base64,
                _sqlExpressionFactory.Constant("\n", stringTypeMapping),
                _sqlExpressionFactory.Constant(string.Empty, stringTypeMapping),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false,
                false,
            ],
            typeof(string),
            stringTypeMapping);
    }

    private SqlExpression FormatBinaryGuid(
        SqlExpression value,
        RelationalTypeMapping? stringTypeMapping
    )
    {
        var formatted = _sqlExpressionFactory.Function(
            "LOWER",
            [
                _sqlExpressionFactory.Function(
                    "HEX",
                    [value],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    typeof(string),
                    stringTypeMapping),
            ],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(string),
            stringTypeMapping);

        foreach (var position in (ReadOnlySpan<int>)[9, 14, 19, 24])
        {
            formatted = _sqlExpressionFactory.Function(
                "INSERT",
                [
                    formatted,
                    _sqlExpressionFactory.Constant(position),
                    _sqlExpressionFactory.Constant(0),
                    _sqlExpressionFactory.Constant("-", stringTypeMapping),
                ],
                nullable: true,
                argumentsPropagateNullability:
                [
                    true,
                    false,
                    false,
                    false
                ],
                typeof(string),
                stringTypeMapping);
        }

        return formatted;
    }

    /// <summary>
    /// Composes partial JSON ExecuteUpdate setters as nested <c>JSON_SET</c> calls.
    /// Objects and collections are parsed as JSON fragments. Boolean SQL values are
    /// parsed explicitly as JSON booleans because both engines otherwise store their
    /// numeric <c>0</c>/<c>1</c> representation.
    /// </summary>
    protected override SqlExpression? GenerateJsonPartialUpdateSetter(
        Expression target,
        SqlExpression value,
        ref SqlExpression? existingSetterValue
    )
    {
        var (jsonColumn, path, isJsonScalar) = target switch
        {
            JsonScalarExpression { TypeMapping.ElementTypeMapping: null } scalar =>
                ((ColumnExpression)scalar.Json, scalar.Path, true),
            JsonScalarExpression scalar => ((ColumnExpression)scalar.Json, scalar.Path, false),
            JsonQueryExpression query => (query.JsonColumn, query.Path, false),
            _ => throw new UnreachableException(),
        };

        var targetType = Nullable.GetUnderlyingType(target.Type) ?? target.Type;
        var jsonValue = isJsonScalar && targetType == typeof(bool)
            ? SerializeBooleanToJson(value, jsonColumn.TypeMapping)
            : isJsonScalar
                ? value
                : _sqlExpressionFactory.Function(
                    "JSON_EXTRACT",
                    [
                        value,
                        _sqlExpressionFactory.Constant("$"),
                    ],
                    nullable: true,
                    argumentsPropagateNullability:
                    [
                        true,
                        false,
                    ],
                    typeof(string),
                    jsonColumn.TypeMapping);

        var jsonSet = _sqlExpressionFactory.Function(
            MySqlSentinelContract.GetName(MySqlSentinelKind.JsonSet),
            [
                existingSetterValue ?? jsonColumn,
                _sqlExpressionFactory.Constant(path, RelationalTypeMapping.NullMapping),
                jsonValue,
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false,
                false,
            ],
            typeof(string),
            jsonColumn.TypeMapping);

        if (existingSetterValue is null)
        {
            return jsonSet;
        }

        existingSetterValue = jsonSet;
        return null;
    }

    private SqlExpression SerializeBooleanToJson(
        SqlExpression value,
        RelationalTypeMapping? jsonTypeMapping
    )
    {
        var stringTypeMapping = _typeMappingSource.FindMapping(typeof(string));
        var serializedValue = _sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    _sqlExpressionFactory.IsNull(value),
                    _sqlExpressionFactory.Constant(null, typeof(string), stringTypeMapping)),
                new CaseWhenClause(value, _sqlExpressionFactory.Constant("true", stringTypeMapping)),
            ],
            _sqlExpressionFactory.Constant("false", stringTypeMapping));

        return _sqlExpressionFactory.Function(
            "JSON_EXTRACT",
            [
                serializedValue,
                _sqlExpressionFactory.Constant("$"),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false
            ],
            typeof(string),
            jsonTypeMapping);
    }
}
