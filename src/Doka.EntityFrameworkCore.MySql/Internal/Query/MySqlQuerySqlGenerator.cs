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

            default:
                return base.VisitSqlFunction(sqlFunctionExpression);
        }
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
    /// escaped defensively to prevent silent query mismatches.
    /// </summary>
    private static string EscapeJsonPathPropertyName(
        string propertyName
    )
    {
        // Escape backslashes first -- in MySQL's default SQL mode (NO_BACKSLASH_ESCAPES
        // absent), a raw backslash in a string literal is treated as an escape prefix.
        // Without doubling, 'path\segment' would be parsed as '$.pathsegment'.
        // Then double any single quotes to prevent premature termination of the
        // enclosing MySQL string literal '$.Path'.
        if (propertyName.Contains('\\', StringComparison.Ordinal)
            || propertyName.Contains('\'', StringComparison.Ordinal))
        {
            return propertyName
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "''", StringComparison.Ordinal);
        }

        return propertyName;
    }
}
