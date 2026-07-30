namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <summary>
    /// Translates EF Core's <c>SqlUnaryExpression</c> Convert operator into a MySQL-valid
    /// <c>CAST(... AS target)</c>. The base generator uses the type-mapping's column-level
    /// <c>StoreType</c> verbatim, which produces MySQL-invalid syntax: <c>CAST(x AS int)</c>,
    /// <c>CAST(x AS bigint)</c>, <c>CAST(x AS longtext)</c> all fail to parse. MySQL's CAST
    /// grammar accepts only a narrow vocabulary -- <c>SIGNED</c>, <c>UNSIGNED</c>,
    /// <c>CHAR</c>, <c>BINARY</c>, <c>DECIMAL</c>, <c>FLOAT</c>, <c>DOUBLE</c>, <c>DATE</c>,
    /// <c>DATETIME</c>, <c>TIME</c>, <c>JSON</c>, <c>NCHAR</c>. This override translates
    /// the column-level store-type into the cast-context-valid keyword for the Convert
    /// path; all other operators fall through to the base implementation.
    /// </summary>
    /// <remarks>
    /// Floating-point to decimal query casts use the engines' common <c>DECIMAL(65,30)</c>
    /// maximum instead of the schema-column default. Sources retrieved 2026-07-28:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/precision-math-decimal-characteristics.html">
    /// MySQL 8.4 DECIMAL characteristics</see> and
    /// <see href="https://mariadb.com/docs/server/reference/data-types/numeric-data-types/decimal">
    /// MariaDB DECIMAL</see>.
    /// </remarks>
    protected override Expression VisitSqlUnary(
        SqlUnaryExpression sqlUnaryExpression
    )
    {
        ArgumentNullException.ThrowIfNull(sqlUnaryExpression);

        if (sqlUnaryExpression.OperatorType is ExpressionType.Not or ExpressionType.OnesComplement
            && IsSignedIntegralType(sqlUnaryExpression.Type.UnwrapNullableType()))
        {
            Sql.Append("CAST((~");
            Visit(sqlUnaryExpression.Operand);
            Sql.Append(") AS SIGNED)");
            return sqlUnaryExpression;
        }

        if (sqlUnaryExpression is not { OperatorType: ExpressionType.Convert, TypeMapping: { } typeMapping })
        {
            return base.VisitSqlUnary(sqlUnaryExpression);
        }

        if (sqlUnaryExpression.Operand.Type.UnwrapNullableType() == typeof(char)
            && IsNumericType(sqlUnaryExpression.Type.UnwrapNullableType()))
        {
            return VisitCharToNumericConvert(sqlUnaryExpression);
        }

        var operandType = sqlUnaryExpression.Operand.Type;

        if (sqlUnaryExpression.Type == typeof(decimal)
            && operandType == typeof(float))
        {
            Sql.Append("CAST(CAST(");
            Visit(sqlUnaryExpression.Operand);
            Sql.Append(" AS CHAR) AS DECIMAL(65,30))");
            return sqlUnaryExpression;
        }

        var castTarget = sqlUnaryExpression.Type == typeof(decimal) && operandType == typeof(double)
            ? "DECIMAL(65,30)"
            : TranslateStoreTypeToCastTarget(typeMapping.StoreType);

        if (castTarget is null)
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
    /// Converts a database character to its numeric CLR <see cref="char"/> value
    /// before applying the requested numeric target type.
    /// </summary>
    /// <remarks>
    /// A direct numeric cast interprets a digit character by its decimal text
    /// (<c>'1'</c> becomes 1), whereas the CLR conversion yields numeric value 49.
    /// Converting the character to UTF-32, reading those bytes as hexadecimal, and
    /// converting base 16 to base 10 preserves the value of database-representable
    /// CLR characters on every supported engine. Sources retrieved 2026-07-29:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/cast-functions.html">MySQL cast functions</see>,
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/string-functions.html#function_hex">MySQL HEX</see>,
    /// <see href="https://mariadb.com/docs/server/reference/sql-functions/string-functions/convert">
    /// MariaDB CONVERT</see>, and
    /// <see href="https://mariadb.com/docs/server/reference/sql-functions/numeric-functions/conv">
    /// MariaDB CONV</see>.
    /// </remarks>
    private SqlUnaryExpression VisitCharToNumericConvert(
        SqlUnaryExpression expression
    )
    {
        var castTarget = TranslateStoreTypeToCastTarget(expression.TypeMapping!.StoreType)
            ?? throw new InvalidOperationException(
                $"The numeric character conversion has no MySQL cast target for "
                + $"'{expression.TypeMapping.StoreType}'.");

        Sql.Append("CAST(CONV(HEX(CONVERT(");
        Visit(expression.Operand);
        Sql.Append(" USING utf32)), 16, 10) AS ");
        Sql.Append(castTarget);
        Sql.Append(")");
        return expression;
    }

    private static bool IsNumericType(
        Type type
    ) => type == typeof(sbyte)
        || type == typeof(byte)
        || type == typeof(short)
        || type == typeof(ushort)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong)
        || type == typeof(float)
        || type == typeof(double)
        || type == typeof(decimal);

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

        if (sqlBinaryExpression.OperatorType is ExpressionType.Equal or ExpressionType.NotEqual
            && IsJsonDocument(sqlBinaryExpression.Left)
            && IsJsonDocument(sqlBinaryExpression.Right))
        {
            EmitJsonDocumentComparison(sqlBinaryExpression);
            return sqlBinaryExpression;
        }

        if (IsSignedIntegralType(sqlBinaryExpression.Type.UnwrapNullableType())
            && sqlBinaryExpression.OperatorType is ExpressionType.And
                or ExpressionType.Or
                or ExpressionType.ExclusiveOr)
        {
            EmitSignedBitwise(sqlBinaryExpression);
            return sqlBinaryExpression;
        }

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
    /// Emits structural equality for JSON objects and arrays instead of comparing their
    /// serialized text. MySQL compares native JSON values, while MariaDB normalizes its
    /// LONGTEXT-backed JSON documents before applying the relational operator.
    /// </summary>
    /// <remarks>
    /// Sources retrieved 2026-07-29:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/json-search-functions.html">
    /// MySQL JSON search functions</see> and
    /// <see
    ///     href="https://mariadb.com/docs/server/reference/sql-functions/special-functions/json-functions/json_normalize">
    /// MariaDB JSON_NORMALIZE</see>.
    /// </remarks>
    private void EmitJsonDocumentComparison(
        SqlBinaryExpression expression
    )
    {
        var usesJsonTextSemantics =
            Profile.GetSupport(ProviderCapability.JsonColumns) == ProviderSupportStatus.Emulated;

        if (usesJsonTextSemantics)
        {
            Sql.Append("JSON_NORMALIZE(");
            EmitJsonDocument(expression.Left);
            Sql.Append(")");
        }
        else
        {
            EmitJsonDocument(expression.Left);
        }

        Sql.Append(expression.OperatorType == ExpressionType.Equal ? " = " : " <> ");

        if (usesJsonTextSemantics)
        {
            Sql.Append("JSON_NORMALIZE(");
            EmitJsonDocument(expression.Right);
            Sql.Append(")");
        }
        else
        {
            EmitJsonDocument(expression.Right);
        }
    }

    private void EmitJsonDocument(
        SqlExpression expression
    )
    {
        if (expression is JsonScalarExpression jsonScalarExpression)
        {
            EmitJsonExtract(jsonScalarExpression);
            return;
        }

        if (Profile.GetSupport(ProviderCapability.JsonColumns) == ProviderSupportStatus.Emulated)
        {
            Visit(expression);
            return;
        }

        Sql.Append("JSON_EXTRACT(");
        Visit(expression);
        Sql.Append(", '$')");
    }

    private static bool IsJsonDocument(
        SqlExpression expression
    ) => expression.TypeMapping?.ElementTypeMapping is not null
        || string.Equals(expression.TypeMapping?.StoreType, "json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Casts MySQL-family unsigned 64-bit bitwise results back to the signed CLR
    /// domain.
    /// </summary>
    private void EmitSignedBitwise(
        SqlBinaryExpression expression
    )
    {
        Sql.Append("CAST((");
        Visit(expression.Left);
        Sql.Append(expression.OperatorType switch
        {
            ExpressionType.And => " & ",
            ExpressionType.Or => " | ",
            ExpressionType.ExclusiveOr => " ^ ",
            _ => throw new UnreachableException(),
        });
        Visit(expression.Right);
        Sql.Append(") AS SIGNED)");
    }

    private static bool IsSignedIntegralType(
        Type type
    ) => type == typeof(sbyte)
        || type == typeof(short)
        || type == typeof(int)
        || type == typeof(long);

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
            "float" => "FLOAT",
            "double" or "real" => "DOUBLE",
            _ => null,
        };
    }
}
