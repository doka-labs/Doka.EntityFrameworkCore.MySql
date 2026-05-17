namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlQuerySqlGenerator : QuerySqlGenerator
{
    private const string OffsetWithoutLimitSentinel = "18446744073709551615";

    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        MySqlSingletonOptions singletonOptions
    ) : base(dependencies)
    {
        _singletonOptions = singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions));
    }

    /// <summary>
    /// Intercepts sentinel function names for MySQL-specific compound SQL expressions
    /// like <c>MATCH(...) AGAINST(...)</c>.
    /// </summary>
    protected override Expression VisitSqlFunction(
        SqlFunctionExpression sqlFunctionExpression
    )
    {
        ArgumentNullException.ThrowIfNull(sqlFunctionExpression);

        switch (sqlFunctionExpression)
        {
            case { Name: "__mysql_regexp", Arguments.Count: 2 }:
                {
                    // MySQL 8.0+: REGEXP_LIKE(input, pattern) -- scalar function
                    // MariaDB: input REGEXP pattern -- infix operator (REGEXP_LIKE does not exist)
                    var isMariaDb = _singletonOptions.ServerVersion?.IsMariaDb == true;

                    if (!isMariaDb)
                    {
                        Sql.Append("REGEXP_LIKE(");
                        Visit(sqlFunctionExpression.Arguments[0]);
                        Sql.Append(", ");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(")");
                    }
                    else
                    {
                        Visit(sqlFunctionExpression.Arguments[0]);
                        Sql.Append(" REGEXP ");
                        Visit(sqlFunctionExpression.Arguments[1]);
                    }

                    return sqlFunctionExpression;
                }

            case { Name: "__mysql_match" or "__mysql_match_boolean", Arguments.Count: 2 }:
                {
                    var isBooleanMode = sqlFunctionExpression.Name == "__mysql_match_boolean";

                    Sql.Append("MATCH(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") AGAINST(");
                    Visit(sqlFunctionExpression.Arguments[1]);

                    if (isBooleanMode)
                    {
                        Sql.Append(" IN BOOLEAN MODE");
                    }

                    Sql.Append(")");

                    return sqlFunctionExpression;
                }

            case { Name: "__mysql_group_concat", Arguments.Count: 2 }:
                {
                    // GROUP_CONCAT(expr SEPARATOR sep) -- MySQL requires the SEPARATOR keyword;
                    // a standard comma-separated argument list is invalid syntax.
                    Sql.Append("GROUP_CONCAT(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(" SEPARATOR ");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(")");

                    return sqlFunctionExpression;
                }

            case { Name: var name, Arguments.Count: 2 }
                when name.StartsWith(DateAddSentinelPrefix, StringComparison.Ordinal):
                {
                    EmitDateAdd(sqlFunctionExpression, name[DateAddSentinelPrefix.Length..]);
                    return sqlFunctionExpression;
                }

            case { Name: var name, Arguments.Count: 2 }
                when name.StartsWith(TimeAddSentinelPrefix, StringComparison.Ordinal):
                {
                    EmitDateAdd(sqlFunctionExpression, name[TimeAddSentinelPrefix.Length..]);
                    return sqlFunctionExpression;
                }

            default:
                return base.VisitSqlFunction(sqlFunctionExpression);
        }
    }

    private const string DateAddSentinelPrefix = "__mysql_date_add_";
    private const string TimeAddSentinelPrefix = "__mysql_time_add_";

    /// <summary>
    /// Emits <c>DATE_ADD(arg0, INTERVAL arg1 UNIT)</c> for the parametrized-interval
    /// translation path. The interval keyword sits between the comma and the value, so
    /// the standard function-arguments comma-separator path cannot express the shape;
    /// the sentinel-function-name pattern lets the translator stay inside the
    /// SqlExpression tree and lets this writer hand-roll the syntax.
    /// </summary>
    private void EmitDateAdd(
        SqlFunctionExpression expression,
        string unit
    )
    {
        var arguments = expression.Arguments
            ?? throw new InvalidOperationException($"Sentinel function '{expression.Name}' must carry arguments.");

        Sql.Append("DATE_ADD(");
        Visit(arguments[0]);
        Sql.Append(", INTERVAL ");
        Visit(arguments[1]);
        Sql.Append(" ");
        Sql.Append(unit);
        Sql.Append(")");
    }

    /// <summary>
    /// Translates JSON scalar path expressions to MySQL JSON_EXTRACT / JSON_UNQUOTE syntax.
    /// For string results: JSON_UNQUOTE(JSON_EXTRACT(column, '$.Path'))
    /// For numeric/bool results: JSON_EXTRACT(column, '$.Path')
    /// </summary>
    protected override Expression VisitJsonScalar(
        JsonScalarExpression jsonScalarExpression
    )
    {
        ArgumentNullException.ThrowIfNull(jsonScalarExpression);

        var path = jsonScalarExpression.Path;

        if (path.Count == 0)
        {
            Visit(jsonScalarExpression.Json);
            return jsonScalarExpression;
        }

        // String properties need JSON_UNQUOTE to strip the surrounding double quotes.
        var needsUnquote = jsonScalarExpression.Type == typeof(string);

        if (needsUnquote)
        {
            Sql.Append("JSON_UNQUOTE(");
        }

        Sql.Append("JSON_EXTRACT(");
        Visit(jsonScalarExpression.Json);
        Sql.Append(", '$");

        foreach (var segment in path)
        {
            if (segment.PropertyName is not null)
            {
                Sql.Append(".");
                Sql.Append(EscapeJsonPathPropertyName(segment.PropertyName));
            }
            else if (segment.ArrayIndex is not null)
            {
                Sql.Append("[");
                Visit(segment.ArrayIndex);
                Sql.Append("]");
            }
        }

        Sql.Append("')");

        if (needsUnquote)
        {
            Sql.Append(")");
        }

        return jsonScalarExpression;
    }

    /// <summary>
    /// Translates EF Core's <c>SqlUnaryExpression</c> Convert operator into a MySQL-valid
    /// <c>CAST(... AS target)</c>. The base generator uses the type-mapping's column-level
    /// <c>StoreType</c> verbatim, which produces MySQL-invalid syntax: <c>CAST(x AS int)</c>,
    /// <c>CAST(x AS bigint)</c>, <c>CAST(x AS longtext)</c> all fail to parse. MySQL's CAST
    /// grammar accepts only a narrow vocabulary -- <c>SIGNED</c>, <c>UNSIGNED</c>,
    /// <c>CHAR</c>, <c>BINARY</c>, <c>DECIMAL</c>, <c>DATE</c>, <c>DATETIME</c>, <c>TIME</c>,
    /// <c>JSON</c>, <c>NCHAR</c>. This override translates the column-level store-type into
    /// the cast-context-valid keyword for the Convert path; all other operators fall
    /// through to the base implementation.
    /// </summary>
    protected override Expression VisitSqlUnary(
        SqlUnaryExpression sqlUnaryExpression
    )
    {
        ArgumentNullException.ThrowIfNull(sqlUnaryExpression);

        if (sqlUnaryExpression is not { OperatorType: ExpressionType.Convert, TypeMapping: { } typeMapping }
            || TranslateStoreTypeToCastTarget(typeMapping.StoreType) is not { } castTarget)
        {
            return base.VisitSqlUnary(sqlUnaryExpression);
        }

        Sql.Append("CAST(");
        Visit(sqlUnaryExpression.Operand);
        Sql.Append(" AS ");
        Sql.Append(castTarget);
        Sql.Append(")");
        return sqlUnaryExpression;
    }

    /// <summary>
    /// Intercepts string-typed Add expressions so they emit MySQL <c>CONCAT(left, right)</c> rather
    /// than the base generator's <c>left + right</c>. MySQL's <c>+</c> operator is arithmetic
    /// addition only; the implicit string-to-number coercion silently produces wrong results
    /// (<c>'10' + 'ALFKI' + '10'</c> evaluates to <c>20</c> not <c>'10ALFKI10'</c>). The check
    /// fires on Add binaries whose CLR Type is <see cref="string"/>; nested chains of string-Adds
    /// produce nested CONCATs which MySQL evaluates left-to-right with the documented string
    /// concatenation semantics.
    /// </summary>
    protected override Expression VisitSqlBinary(
        SqlBinaryExpression sqlBinaryExpression
    )
    {
        ArgumentNullException.ThrowIfNull(sqlBinaryExpression);

        if (sqlBinaryExpression.OperatorType != ExpressionType.Add
            || sqlBinaryExpression.Type != typeof(string))
        {
            return base.VisitSqlBinary(sqlBinaryExpression);
        }

        Sql.Append("CONCAT(");
        Visit(sqlBinaryExpression.Left);
        Sql.Append(", ");
        Visit(sqlBinaryExpression.Right);
        Sql.Append(")");
        return sqlBinaryExpression;
    }

    /// <summary>
    /// Maps a column-level MySQL store-type string to the cast-context-valid keyword. Returns
    /// <see langword="null"/> when the input is not a recognized integer / text / binary store
    /// type, leaving the base generator's StoreType-verbatim path untouched (which is correct
    /// for the cast-grammar keywords that MySQL already accepts as both column and cast type,
    /// e.g. <c>DECIMAL</c>, <c>DATE</c>, <c>DATETIME</c>, <c>TIME</c>, <c>JSON</c>).
    /// </summary>
    private static string? TranslateStoreTypeToCastTarget(
        string storeType
    )
    {
        if (string.IsNullOrEmpty(storeType))
        {
            return null;
        }

        // Strip any "(N)" / "(p,s)" suffix for the lookup; CAST keeps the precision for
        // DECIMAL / CHAR-with-length / BINARY-with-length.
        var parenthesisIndex = storeType.IndexOf('(', StringComparison.Ordinal);
        var baseToken = parenthesisIndex < 0 ? storeType : storeType[..parenthesisIndex];
        var trailing = parenthesisIndex < 0 ? string.Empty : storeType[parenthesisIndex..];

        return baseToken.ToLowerInvariant() switch
        {
            "tinyint" or "smallint" or "mediumint" or "int" or "integer" or "bigint" => "SIGNED",
            "tinyint unsigned" or "smallint unsigned" or "mediumint unsigned" or "int unsigned" or "bigint unsigned" =>
                "UNSIGNED",
            "char" or "varchar" or "text" or "tinytext" or "mediumtext" or "longtext" or "nchar" or "nvarchar" =>
                "CHAR" + trailing,
            "binary" or "varbinary" or "blob" or "tinyblob" or "mediumblob" or "longblob" => "BINARY" + trailing,
            "float" or "double" or "real" => "DECIMAL",
            _ => null,
        };
    }

    protected override void GenerateLimitOffset(
        SelectExpression selectExpression
    )
    {
        ArgumentNullException.ThrowIfNull(selectExpression);

        if (selectExpression.Limit is null
            && selectExpression.Offset is null)
        {
            return;
        }

        Sql.AppendLine();
        Sql.Append("LIMIT ");

        if (selectExpression.Offset is null)
        {
            Visit(selectExpression.Limit);

            return;
        }

        if (selectExpression.Limit is null)
        {
            Sql.Append(OffsetWithoutLimitSentinel);
        }
        else
        {
            Visit(selectExpression.Limit);
        }

        Sql.Append(" OFFSET ");
        Visit(selectExpression.Offset);
    }

    /// <summary>
    /// Escapes a JSON path property name for safe inclusion in a MySQL JSON path literal.
    /// Property names come from model metadata (developer-controlled), but characters that
    /// could terminate the enclosing SQL string literal or act as escape sequences must be
    /// escaped defensively to prevent silent query mismatches. The clean-name fast path
    /// returns the input unchanged so the common case incurs no allocation; the slow path
    /// delegates to <see cref="MySqlSqlLiteralEscaper.Escape"/> for the SQL-standard
    /// double-quote / double-backslash form.
    /// </summary>
    private static string EscapeJsonPathPropertyName(
        string propertyName
    ) => propertyName.Contains('\\', StringComparison.Ordinal) || propertyName.Contains('\'', StringComparison.Ordinal)
        ? MySqlSqlLiteralEscaper.Escape(propertyName)
        : propertyName;
}
