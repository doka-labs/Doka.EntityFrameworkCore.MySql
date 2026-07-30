namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <summary>
    /// Emits a composable <c>JSON_SET(document, path, value)</c> call for ExecuteUpdate.
    /// The path stays in the SQL expression tree so constant and parameterized array
    /// indices use the same escaping rules as JSON query translation.
    /// </summary>
    private void EmitJsonSet(
        SqlFunctionExpression expression
    )
    {
        var arguments = expression.Arguments
            ?? throw new InvalidOperationException("The __mysql_json_set sentinel requires three arguments.");

        if (arguments[1] is not SqlConstantExpression { Value: IReadOnlyList<PathSegment> path, })
        {
            throw new InvalidOperationException(
                "The __mysql_json_set sentinel requires an IReadOnlyList<PathSegment> path.");
        }

        Sql.Append("JSON_SET(");
        Visit(arguments[0]);
        Sql.Append(", ");

        if (HasDynamicArrayIndex(path))
        {
            AppendDynamicJsonPath(path);
        }
        else
        {
            AppendStaticJsonPath(path);
        }

        Sql.Append(", ");
        Visit(arguments[2]);
        Sql.Append(")");
    }

    /// <summary>
    /// Wraps a restricted <c>IN</c> subquery in a derived table. MySQL and
    /// MariaDB reject <c>LIMIT</c> directly below <c>IN</c>, <c>ALL</c>,
    /// <c>ANY</c>, or <c>SOME</c>. MySQL also rejects a mutation that reads its
    /// target table without an intervening derived-table boundary.
    /// The outer <c>SELECT *</c> preserves the single-column and NULL semantics of the
    /// original <see cref="InExpression"/>.
    /// </summary>
    protected override void GenerateIn(
        InExpression inExpression,
        bool negated
    )
    {
        ArgumentNullException.ThrowIfNull(inExpression);

        var subquery = inExpression.Subquery;

        if (TryGenerateJsonCollectionContains(inExpression, negated))
        {
            return;
        }

        var requiresLimitIsolation = subquery is { Limit: not null, } or { Offset: not null, };
        var requiresTargetIsolation = subquery is not null && RequiresMutationTargetIsolation(subquery);

        if (!requiresLimitIsolation
            && !requiresTargetIsolation)
        {
            GenerateInItem(inExpression.Item);
            Sql.Append(negated ? " NOT IN (" : " IN (");

            if (inExpression.Values is not null)
            {
                for (var index = 0; index < inExpression.Values.Count; index++)
                {
                    if (index > 0)
                    {
                        Sql.Append(", ");
                    }

                    Visit(inExpression.Values[index]);
                }
            }
            else
            {
                Sql.AppendLine();

                using (Sql.Indent())
                {
                    Visit(inExpression.Subquery);
                }

                Sql.AppendLine();
            }

            Sql.Append(")");
            return;
        }

        GenerateInItem(inExpression.Item);
        Sql.Append(negated ? " NOT IN (" : " IN (");
        Sql.AppendLine();

        using (Sql.Indent())
        {
            Sql.AppendLine("SELECT *");
            Sql.AppendLine("FROM (");

            using (Sql.Indent())
            {
                Visit(subquery!);
            }

            Sql.AppendLine();
            Sql.Append(") AS ");
            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("__doka_isolated_subquery"));
        }

        Sql.AppendLine();
        Sql.Append(")");
    }

    /// <summary>
    /// Emits scalar JSON membership for a primitive collection.
    /// </summary>
    /// <remarks>
    /// MySQL can decorrelate a rowset-returning <c>IN</c> subquery before
    /// evaluating its <c>JSON_TABLE</c> input, which loses the current
    /// outer-row value. Uncomposed membership becomes direct
    /// <c>JSON_CONTAINS</c>. Filtered or naturally paginated membership becomes
    /// a correlated scalar count, a shape both engines evaluate against the
    /// current row.
    /// </remarks>
    private bool TryGenerateJsonCollectionContains(
        InExpression inExpression,
        bool negated
    )
    {
        if (inExpression.Subquery is not
            {
                Tables: [MySqlJsonTableExpression jsonTable],
                Predicate: var predicate,
                GroupBy: [],
                Having: null,
                IsDistinct: var isDistinct,
                Orderings: var orderings,
                Limit: var limit,
                Offset: var offset,
                Projection:
                [
                { Expression: ColumnExpression { Name: "value", TableAlias: var projectionAlias, }, },
                ],
            }
            || projectionAlias != jsonTable.Alias)
        {
            return false;
        }

        if (predicate is null
            && !isDistinct
            && orderings.Count == 0
            && limit is null
            && offset is null
            && CanUseNativeJsonCandidate(inExpression.Item.Type))
        {
            if (negated)
            {
                Sql.Append("NOT ");
            }

            Sql.Append("JSON_CONTAINS(");
            Visit(jsonTable.JsonExpression);
            Sql.Append(", ");
            GenerateJsonCollectionCandidate(inExpression.Item);
            Sql.Append(")");

            return true;
        }

        if (isDistinct
            || !HasNaturalJsonTableOrdering(orderings, jsonTable)
            || ((limit is not null || offset is not null) && predicate is not null))
        {
            return false;
        }

        var ordinalityColumnName = GetOrdinalityColumnName(jsonTable);

        Sql.Append("(");
        Sql.AppendLine();

        using (Sql.Indent())
        {
            Sql.AppendLine("SELECT COUNT(*)");
            Sql.Append("FROM ");
            Visit(jsonTable);
            Sql.AppendLine();
            Sql.Append("WHERE ");

            if (predicate is not null)
            {
                Visit(predicate);
                Sql.Append(" AND ");
            }

            Visit(
                new ColumnExpression(
                    "value",
                    jsonTable.Alias!,
                    inExpression.Item.Type,
                    inExpression.Item.TypeMapping,
                    nullable: true));
            Sql.Append(" <=> ");
            Visit(inExpression.Item);

            if (offset is not null)
            {
                Sql.Append(" AND ");
                Visit(
                    new ColumnExpression(
                        ordinalityColumnName,
                        jsonTable.Alias!,
                        typeof(int),
                        RelationalTypeMapping.NullMapping,
                        nullable: false));
                Sql.Append(" > ");
                Visit(offset);
            }

            if (limit is not null)
            {
                Sql.Append(" AND ");
                Visit(
                    new ColumnExpression(
                        ordinalityColumnName,
                        jsonTable.Alias!,
                        typeof(int),
                        RelationalTypeMapping.NullMapping,
                        nullable: false));
                Sql.Append(" <= ");

                if (offset is not null)
                {
                    Sql.Append("(");
                    Visit(offset);
                    Sql.Append(" + ");
                    Visit(limit);
                    Sql.Append(")");
                }
                else
                {
                    Visit(limit);
                }
            }
        }

        Sql.AppendLine();
        Sql.Append(negated ? ") = 0" : ") > 0");

        return true;
    }

    private static bool CanUseNativeJsonCandidate(
        Type itemType
    )
    {
        itemType = itemType.UnwrapNullableType();

        return itemType.IsEnum
            || itemType == typeof(bool)
            || itemType == typeof(string)
            || itemType == typeof(char)
            || itemType == typeof(byte)
            || itemType == typeof(sbyte)
            || itemType == typeof(short)
            || itemType == typeof(ushort)
            || itemType == typeof(int)
            || itemType == typeof(uint)
            || itemType == typeof(long)
            || itemType == typeof(ulong)
            || itemType == typeof(float)
            || itemType == typeof(double)
            || itemType == typeof(decimal);
    }

    private static bool HasNaturalJsonTableOrdering(
        IReadOnlyList<OrderingExpression> orderings,
        MySqlJsonTableExpression jsonTable
    ) => orderings.Count == 0
        || (orderings is
            [
            {
                IsAscending: true,
                Expression: ColumnExpression { Name: var columnName, TableAlias: var orderingAlias, },
            },
            ]
            && orderingAlias == jsonTable.Alias
            && columnName == GetOrdinalityColumnName(jsonTable));

    private static string GetOrdinalityColumnName(
        MySqlJsonTableExpression jsonTable
    ) => (jsonTable.ColumnInfos
            ?? throw new UnreachableException("A translated JSON_TABLE rowset must declare its columns."))
        .Single(static column => column.ForOrdinality)
        .Name;

    private void GenerateJsonCollectionCandidate(
        SqlExpression item
    )
    {
        if (item.Type.UnwrapNullableType() == typeof(bool))
        {
            Sql.Append("JSON_EXTRACT(CASE WHEN ");
            Visit(item);
            Sql.Append(" IS NULL THEN 'null' WHEN ");
            Visit(item);
            Sql.Append(" THEN 'true' ELSE 'false' END, '$')");
            return;
        }

        Sql.Append("JSON_EXTRACT(JSON_ARRAY(");
        Visit(item);
        Sql.Append("), '$[0]')");
    }

    /// <summary>
    /// Parenthesizes the value tested by an <c>IN</c> expression.
    /// </summary>
    /// <remarks>
    /// EF Core can produce a predicate as the tested value from a custom
    /// function translation. MySQL and MariaDB require that predicate to be
    /// grouped before it can be compared with a boolean value list. Grouping
    /// every tested value keeps the SQL shape deterministic and is
    /// semantics-neutral for scalar values.
    /// </remarks>
    private void GenerateInItem(
        SqlExpression item
    )
    {
        Sql.Append("(");
        Visit(item);
        Sql.Append(")");
    }

    /// <summary>
    /// Emits JSON_TABLE-backed existence checks as a limited scalar subquery on MySQL.
    /// MySQL bug #114897 can reorder the correlated table function ahead of its source
    /// during EXISTS-to-semijoin optimization and silently return no rows. A scalar
    /// <c>SELECT 1 ... LIMIT 1</c> preserves EXISTS / NOT EXISTS semantics while avoiding
    /// the faulty semijoin transformation.
    /// </summary>
    protected override void GenerateExists(
        ExistsExpression existsExpression,
        bool negated
    )
    {
        ArgumentNullException.ThrowIfNull(existsExpression);

        var subquery = existsExpression.Subquery;
        if (_singletonOptions.ServerVersion?.IsMariaDb != false
            || subquery.Tables is not [MySqlJsonTableExpression, ..]
            || subquery.GroupBy.Count > 0
            || subquery.Having is not null
            || subquery.Limit is not null
            || subquery.Offset is not null)
        {
            base.GenerateExists(existsExpression, negated);
            return;
        }

        Sql.AppendLine("(");
        using (Sql.Indent())
        {
            Sql.Append("SELECT 1");
            GenerateFrom(subquery);

            if (subquery.Predicate is not null)
            {
                Sql.AppendLine();
                Sql.Append("WHERE ");
                Visit(subquery.Predicate);
            }

            Sql.AppendLine();
            Sql.Append("LIMIT 1");
        }

        Sql.AppendLine();
        Sql.Append(negated ? ") IS NULL" : ") IS NOT NULL");
    }

    /// <summary>
    /// Dispatches to the <see cref="MySqlJsonTableExpression"/>-specific emitter when the
    /// table-valued-function expression is our JSON_TABLE shape; falls through to the base
    /// emitter for ordinary stored TVFs (no MySQL-specific TVFs other than JSON_TABLE today).
    /// </summary>
    protected override Expression VisitTableValuedFunction(
        TableValuedFunctionExpression tableValuedFunctionExpression
    )
    {
        ArgumentNullException.ThrowIfNull(tableValuedFunctionExpression);

        if (tableValuedFunctionExpression is MySqlJsonTableExpression jsonTableExpression)
        {
            return VisitJsonTableExpression(jsonTableExpression);
        }

        return base.VisitTableValuedFunction(tableValuedFunctionExpression);
    }

    /// <summary>
    /// Emits the cross-engine JSON_TABLE grammar both MySQL 8.0+ and MariaDB 10.6+ accept:
    /// <c>JSON_TABLE(json, '$[*]' COLUMNS (...)) AS alias</c>.
    /// Path strings reuse <see cref="AppendStaticJsonPath"/> / <see cref="AppendDynamicJsonPath"/>
    /// so dynamic array indices (non-constant SqlExpressions) splice in through CONCAT the same
    /// way <see cref="VisitJsonScalar"/> handles them.
    /// </summary>
    private MySqlJsonTableExpression VisitJsonTableExpression(
        MySqlJsonTableExpression jsonTableExpression
    )
    {
        Sql.Append("JSON_TABLE(");

        if (jsonTableExpression.Path is { Count: > 0 } rowPath)
        {
            if (HasDynamicArrayIndex(rowPath))
            {
                // JSON_TABLE's path argument must be a literal SQL string, so a path that
                // splices a runtime expression cannot live there. Pre-extract the dynamic
                // subtree via JSON_EXTRACT(col, CONCAT('$...')) and let JSON_TABLE iterate
                // the resulting array via the literal '$[*]' row-source path. Both MySQL 8.x
                // and MariaDB 10.6+ accept this composition.
                Sql.Append("JSON_EXTRACT(");
                Visit(jsonTableExpression.JsonExpression);
                Sql.Append(", ");
                AppendDynamicJsonPath(rowPath);
                Sql.Append("), '$[*]'");
            }
            else
            {
                Visit(jsonTableExpression.JsonExpression);
                Sql.Append(", ");
                AppendStaticJsonPathForRowSource(rowPath);
            }
        }
        else
        {
            // No row-source path means "iterate over the top-level array elements" --
            // the standard expansion for a primitive collection without nested access.
            Visit(jsonTableExpression.JsonExpression);
            Sql.Append(", '$[*]'");
        }

        if (jsonTableExpression.ColumnInfos is { Count: > 0 } columnInfos)
        {
            Sql.Append(" COLUMNS (");

            for (var i = 0; i < columnInfos.Count; i++)
            {
                if (i > 0)
                {
                    Sql.Append(", ");
                }

                var column = columnInfos[i];

                Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(column.Name));

                if (column.ForOrdinality)
                {
                    Sql.Append(" FOR ORDINALITY");
                    continue;
                }

                Sql.Append(" ");
                Sql.Append(column.AsJson ? "JSON" : column.TypeMapping.StoreType);
                Sql.Append(" PATH ");

                if (column.Path is { Count: > 0 } columnPath)
                {
                    if (HasDynamicArrayIndex(columnPath))
                    {
                        AppendDynamicJsonPath(columnPath);
                    }
                    else
                    {
                        AppendStaticJsonPath(columnPath);
                    }
                }
                else
                {
                    // No per-column path -- the JSON_TABLE element itself (i.e. the row's whole
                    // JSON value); the standard JSON-path expression for "current row" is '$'.
                    Sql.Append("'$'");
                }
            }

            Sql.Append(")");
        }

        Sql.Append(")");
        Sql.Append(AliasSeparator);
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(jsonTableExpression.Alias));

        return jsonTableExpression;
    }

    /// <summary>
    /// Translates JSON scalar path expressions to MySQL / MariaDB syntax with type-aware
    /// coercion so the column value the .NET shaper reads matches the type the shaper
    /// expects from <c>RelationalTypeMapping.GetDataReaderMethod()</c>.
    /// <list type="bullet">
    /// <item>Element-collection / nested-JSON mapping -> raw <c>JSON_EXTRACT</c>
    /// (returns JSON text; downstream JSON-aware shaper handles parsing).</item>
    /// <item><see cref="string"/> -> <c>JSON_UNQUOTE(JSON_EXTRACT(...))</c> to strip the
    /// JSON-text surrounding quotes.</item>
    /// <item><see cref="bool"/> -> <c>(JSON_EXTRACT(...) = TRUE)</c>; MySQL and MariaDB
    /// have no <c>CAST ... AS BOOL</c> / <c>BIT</c> grammar, so a boolean comparison
    /// produces a TINYINT 0/1 that <c>reader.GetBoolean</c> reads natively.</item>
    /// <item>Numeric and temporal mappings -> an engine-aware <c>CAST</c> that preserves
    /// SQL <c>NULL</c> and matches the data-reader type expected by EF Core.</item>
    /// <item><see cref="byte"/> arrays -> <c>FROM_BASE64</c> because JSON stores the value
    /// as Base64 text while relational comparisons use binary literals.</item>
    /// </list>
    ///
    /// When the path contains a non-constant array index (a SQL expression rather than a
    /// literal integer), the path is emitted via <c>CONCAT</c> so the dynamic expression
    /// lives OUTSIDE the JSON path string literal:
    /// <c>JSON_EXTRACT(col, CONCAT('$[', CAST(expr AS CHAR), '].Name'))</c>. The base
    /// implementation would inline the SQL expression verbatim inside the path string and
    /// trip <c>Invalid JSON path expression</c> against both engines.
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

        var typeMapping = jsonScalarExpression.TypeMapping;
        var modelClrType = typeMapping?.ClrType;
        var modelNonNullable = modelClrType is null
            ? null
            : Nullable.GetUnderlyingType(modelClrType) ?? modelClrType;
        var hasConverter = typeMapping?.Converter is not null;

        // bool: emit `(JSON_EXTRACT(...) = TRUE)`. The boolean-comparison result is TINYINT
        // 0/1 which the shaper's reader.GetBoolean consumes natively. Two guard rails:
        // (1) skip for value-converted properties (TypeMapping.Converter set) -- for a bool
        // stored as int 0/1 the SQL must produce the int value the converter then maps to
        // bool on the .NET side; wrapping with `(=TRUE)` shortcircuits to 0/1 before the
        // converter sees the raw stored value. (2) skip for element-collection mappings
        // (structural-JSON path; raw JSON_EXTRACT below feeds the JSON-aware shaper).
        if (modelNonNullable == typeof(bool)
            && !hasConverter
            && typeMapping?.ElementTypeMapping is null)
        {
            Sql.Append("(");
            EmitJsonExtract(jsonScalarExpression);
            Sql.Append(" = TRUE)");
            return jsonScalarExpression;
        }

        // Non-string non-bool primitives that have a known MySQL CAST target: wrap in
        // CAST so the reader sees a typed value matching its GetXxx expectation. Driven
        // by TypeMapping.StoreType (storage SQL type, post-converter) rather than ClrType
        // -- enum/bool/etc with int storage all want CAST AS SIGNED regardless of the
        // model-side type. The CAST target maps the MySQL column type into the narrower
        // CAST grammar accepted by both engines (SIGNED, UNSIGNED, DECIMAL(p,s), DOUBLE).
        // Engine-conditional reader choice:
        //   - MariaDB uses JSON_VALUE which gives both NULL-safety (JSON null -> SQL
        //     NULL) and correct bool coercion (CAST of MariaDB's JSON_VALUE(true)=1;
        //     CAST of JSON_EXTRACT(true) returns 0 on MariaDB because JSON_EXTRACT
        //     preserves the JSON-text form).
        //   - MySQL has the opposite asymmetry: CAST(JSON_VALUE(true)) returns 0 (text
        //     "true" coerced to int), CAST(JSON_EXTRACT(true)) returns 1 (preserves
        //     JSON-bool primitive). MySQL also CAST'es JSON null to 0 (not NULL) which
        //     breaks `WHERE x.NullableInt != null` predicates. Keep JSON_EXTRACT for
        //     the bool-via-int-converter correctness AND wrap the CAST in
        //     `CASE WHEN JSON_TYPE(...) = 'NULL' THEN NULL ELSE CAST(...) END` so JSON
        //     null propagates as SQL NULL through the predicate translation.
        if (typeMapping?.ElementTypeMapping is null
            && typeMapping is not null
            && JsonScalarCastTarget(typeMapping.StoreType) is { } cast)
        {
            var isMariaDb = _singletonOptions.ServerVersion?.IsMariaDb == true;

            if (isMariaDb)
            {
                Sql.Append("CAST(");
                EmitJsonScalarRead(jsonScalarExpression);
                Sql.Append(" AS ").Append(cast.Target).Append(")");
            }
            else
            {
                Sql.Append("CASE WHEN JSON_TYPE(");
                EmitJsonExtract(jsonScalarExpression);
                Sql.Append(") = 'NULL' THEN NULL ELSE CAST(");
                if (cast.NeedsUnquote)
                {
                    Sql.Append("JSON_UNQUOTE(");
                }

                EmitJsonExtract(jsonScalarExpression);

                if (cast.NeedsUnquote)
                {
                    Sql.Append(")");
                }

                Sql.Append(" AS ").Append(cast.Target).Append(") END");
            }

            return jsonScalarExpression;
        }

        // byte[] case: JSON storage is a base64-encoded string ("AQID" for new byte[]
        // {1,2,3}). The .NET reader expects binary bytes and EF Core's predicate
        // translator emits binary literals (`X'010203'`) for byte[] constants -- those
        // do not compare against a base64 text result. Wrap with FROM_BASE64 to decode
        // the JSON string into the binary form both engines compare against the
        // X'HEX' literal correctly. Cross-engine: FROM_BASE64 works on MySQL 8.4 and
        // MariaDB 11.8.
        if (modelNonNullable == typeof(byte[]))
        {
            Sql.Append("FROM_BASE64(JSON_UNQUOTE(");
            EmitJsonExtract(jsonScalarExpression);
            Sql.Append("))");
            return jsonScalarExpression;
        }

        // Binary GUID columns use the RFC 4122 byte order configured for
        // MySqlConnector's Binary16 mode. JSON stores the same value as canonical
        // text, so rebuild the byte sequence before comparison or materialization.
        if (modelNonNullable == typeof(Guid)
            && typeMapping?.StoreType.StartsWith("binary", StringComparison.OrdinalIgnoreCase) == true)
        {
            Sql.Append("UNHEX(REPLACE(JSON_UNQUOTE(");
            EmitJsonExtract(jsonScalarExpression);
            Sql.Append("), '-', ''))");
            return jsonScalarExpression;
        }

        if (modelNonNullable == typeof(string)
            && typeMapping?.ElementTypeMapping is null)
        {
            Sql.Append("CASE WHEN JSON_TYPE(");
            EmitJsonExtract(jsonScalarExpression);
            Sql.Append(") = 'NULL' THEN NULL ELSE JSON_UNQUOTE(");
            EmitJsonExtract(jsonScalarExpression);
            Sql.Append(") END");
            return jsonScalarExpression;
        }

        // Default path: always JSON_UNQUOTE. The earlier branches (bool wrapper + CAST
        // path) cover every CLR type whose JSON representation is a non-string primitive
        // (boolean, number). Everything that reaches here was serialized into JSON as a
        // string and the .NET shaper needs the unquoted text form: string,
        // DateTimeOffset (e.g. `"2000-01-01 12:34:56-08:00"`), char and
        // custom-converter types with string provider mapping. NOTE: JSON_VALUE would
        // give NULL-safe semantics for JSON null (vs JSON_UNQUOTE's returning the string
        // "null") but it returns SQL NULL on non-scalar JSON values (objects, arrays) --
        // and the EF Core shaper for JSON-owned-entity projections routes through
        // JsonScalarExpression too, expecting the raw JSON-text of the owned object/array.
        // Keep JSON_UNQUOTE+JSON_EXTRACT here so owned-entity projections continue to
        // receive the JSON-text payload they need.
        Sql.Append("JSON_UNQUOTE(");
        EmitJsonExtract(jsonScalarExpression);
        Sql.Append(")");
        return jsonScalarExpression;
    }

    /// <summary>
    /// Reads a JSON scalar with NULL-safe semantics: emits <c>JSON_VALUE(json, '$.path')</c>
    /// when the path is a literal string (returns SQL NULL for JSON null), or falls back
    /// to <c>JSON_UNQUOTE(JSON_EXTRACT(json, CONCAT(...)))</c> when the path carries a
    /// non-constant array index (MySQL rejects <c>JSON_VALUE</c> with a <c>CONCAT</c>
    /// path argument; MariaDB accepts both). Both forms produce the unquoted text value
    /// of a JSON primitive at the path, which the surrounding context (raw projection,
    /// CAST) consumes uniformly.
    /// </summary>
    private void EmitJsonScalarRead(
        JsonScalarExpression jsonScalarExpression
    )
    {
        if (HasDynamicArrayIndex(jsonScalarExpression.Path))
        {
            Sql.Append("JSON_UNQUOTE(");
            EmitJsonExtract(jsonScalarExpression);
            Sql.Append(")");
            return;
        }

        Sql.Append("JSON_VALUE(");
        Visit(jsonScalarExpression.Json);
        Sql.Append(", ");
        AppendStaticJsonPath(jsonScalarExpression.Path);
        Sql.Append(")");
    }

    /// <summary>
    /// Holds the CAST target token (e.g. <c>SIGNED</c>, <c>DECIMAL(10,2)</c>,
    /// <c>DATETIME(6)</c>) plus a flag indicating whether the source expression must go
    /// through <c>JSON_UNQUOTE</c> first. Numeric types do not need UNQUOTE (JSON_EXTRACT
    /// returns the numeric value as a JSON number without quotes); temporal types DO need
    /// UNQUOTE because they are stored as JSON strings (e.g. <c>"2023-01-01T00:00:00"</c>).
    /// </summary>
    private readonly record struct JsonScalarCast(string Target, bool NeedsUnquote);

    /// <summary>
    /// Maps a column-level MySQL <c>StoreType</c> string to the CAST target a JSON scalar
    /// projection wraps the <c>JSON_EXTRACT</c> call in. Returns <see langword="null"/> for
    /// store types where no CAST is needed -- string-shaped types fall through to
    /// <c>JSON_UNQUOTE</c>; <c>json</c> stays raw; <c>tinyint(1)</c>/<c>bit(1)</c> are the
    /// boolean shape handled by the earlier <c>(= TRUE)</c> branch; binary store types are
    /// handled by the <c>FROM_BASE64</c> branch.
    /// </summary>
    private static JsonScalarCast? JsonScalarCastTarget(
        string storeType
    )
    {
        var lower = storeType.ToLowerInvariant();

        // Strip trailing UNSIGNED suffix so the base type-name switch finds the matching
        // numeric family; UNSIGNED then drives the CAST-target selection.
        var unsigned = lower.EndsWith(" unsigned", StringComparison.Ordinal);

        var baseType = unsigned
            ? lower[..^" unsigned".Length].TrimEnd()
            : lower;

        // Strip trailing parameter list `(p,s)` / `(N)` so e.g. `decimal(10,2)` matches
        // `decimal`; the original storeType is reused verbatim when the CAST target needs
        // the precision (DECIMAL, DATETIME, TIME).
        var parenIndex = baseType.IndexOf('(');
        var simpleType = parenIndex >= 0 ? baseType[..parenIndex] : baseType;

        var precisionSuffix = parenIndex >= 0
            ? storeType[storeType.IndexOf('(')..(storeType.IndexOf(')') + 1)]
            : null;

        return simpleType switch
        {
            "tinyint" or "smallint" or "mediumint" or "int" or "integer" or "bigint" or "year"
                => new JsonScalarCast(unsigned ? "UNSIGNED" : "SIGNED", NeedsUnquote: false),
            "decimal" or "numeric" or "fixed" or "dec"
                // Preserve the precision/scale from the store type so the reader gets a
                // DECIMAL with the same shape as the underlying column.
                => new JsonScalarCast(
                    precisionSuffix is null ? "DECIMAL" : "DECIMAL" + precisionSuffix,
                    NeedsUnquote: false),
            "float" or "double" or "real"
                // Both engines accept CAST AS DOUBLE for any floating-point storage; reader
                // GetFloat narrows the DOUBLE to float losslessly within the original range.
                => new JsonScalarCast("DOUBLE", NeedsUnquote: false),
            "datetime" or "timestamp"
                // Stored as JSON string "2023-01-01T00:00:00..."; UNQUOTE first to strip
                // the surrounding double quotes before MySQL parses it as a temporal
                // literal. Precision suffix preserved so DATETIME(6) round-trips
                // microseconds.
                => new JsonScalarCast(
                    precisionSuffix is null ? "DATETIME" : "DATETIME" + precisionSuffix,
                    NeedsUnquote: true),
            "date"
                => new JsonScalarCast("DATE", NeedsUnquote: true),
            "time"
                => new JsonScalarCast(
                    precisionSuffix is null ? "TIME" : "TIME" + precisionSuffix,
                    NeedsUnquote: true),
            _ => null,
        };
    }

    /// <summary>
    /// Emits the raw <c>JSON_EXTRACT(&lt;json&gt;, '&lt;path&gt;')</c> expression, dispatching
    /// to the static-path / dynamic-path helper based on whether the path carries any
    /// non-constant array index. Shared by every <see cref="VisitJsonScalar"/> code path.
    /// </summary>
    private void EmitJsonExtract(
        JsonScalarExpression jsonScalarExpression
    )
    {
        Sql.Append("JSON_EXTRACT(");
        Visit(jsonScalarExpression.Json);
        Sql.Append(", ");

        if (HasDynamicArrayIndex(jsonScalarExpression.Path))
        {
            AppendDynamicJsonPath(jsonScalarExpression.Path);
        }
        else
        {
            AppendStaticJsonPath(jsonScalarExpression.Path);
        }

        Sql.Append(")");
    }

    private void AppendStaticJsonPath(
        IReadOnlyList<PathSegment> path
    )
    {
        Sql.Append("'$");
        foreach (var segment in path)
        {
            AppendPathSegmentLiteral(segment);
        }

        Sql.Append("'");
    }

    /// <summary>
    /// Emits a static JSON path for a <c>JSON_TABLE</c> row-source argument. Differs from
    /// <see cref="AppendStaticJsonPath"/> by appending an <c>[*]</c> wildcard suffix when
    /// the path's last segment is a property name (e.g. <c>$.OwnedCollectionBranch</c>
    /// becomes <c>$.OwnedCollectionBranch[*]</c>) so JSON_TABLE iterates the elements of
    /// the array AT the path rather than treating the array as a single row. A trailing
    /// array-index segment (e.g. <c>$.Coll[0]</c>) targets one specific element and
    /// already produces a single-row source; no wildcard is appended.
    /// </summary>
    private void AppendStaticJsonPathForRowSource(
        IReadOnlyList<PathSegment> path
    )
    {
        Sql.Append("'$");
        foreach (var segment in path)
        {
            AppendPathSegmentLiteral(segment);
        }

        if (path[^1].PropertyName is not null)
        {
            Sql.Append("[*]");
        }

        Sql.Append("'");
    }

    private void AppendPathSegmentLiteral(
        PathSegment segment
    )
    {
        if (segment.PropertyName is not null)
        {
            Sql.Append(".");
            Sql.Append(EscapeJsonPathPropertyName(segment.PropertyName));
        }
        else if (segment.ArrayIndex is SqlConstantExpression { Value: int constantIndex })
        {
            Sql.Append("[");
            Sql.Append(constantIndex.ToString(CultureInfo.InvariantCulture));
            Sql.Append("]");
        }
    }

    private void AppendDynamicJsonPath(
        IReadOnlyList<PathSegment> path
    )
    {
        Sql.Append("CONCAT('$");
        var stringBufferOpen = true;

        foreach (var segment in path)
        {
            if (segment.PropertyName is not null)
            {
                Sql.Append(".");
                Sql.Append(EscapeJsonPathPropertyName(segment.PropertyName));
                continue;
            }

            if (segment.ArrayIndex is SqlConstantExpression { Value: int constantIndex })
            {
                Sql.Append("[");
                Sql.Append(constantIndex.ToString(CultureInfo.InvariantCulture));
                Sql.Append("]");
                continue;
            }

            if (segment.ArrayIndex is not null)
            {
                // Break out of the path string literal, splice in the expression as CHAR,
                // then re-open the literal for the suffix segments.
                Sql.Append("[', CAST(");
                Visit(segment.ArrayIndex);
                Sql.Append(" AS CHAR), ']");
                stringBufferOpen = true;
            }
        }

        if (stringBufferOpen)
        {
            Sql.Append("')");
        }
    }

    private static bool HasDynamicArrayIndex(
        IReadOnlyList<PathSegment> path
    )
    {
        foreach (var segment in path)
        {
            if (segment.ArrayIndex is not null and not SqlConstantExpression)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Escapes a JSON path property name for safe inclusion in a MySQL / MariaDB JSON path
    /// literal. MySQL accepts an unquoted path segment <c>$.ident</c> only when the segment
    /// matches the identifier shape (ASCII letter / underscore followed by ASCII letters /
    /// digits / underscores); any other shape -- non-ASCII characters, dots, brackets,
    /// punctuation, leading digit -- has to be wrapped in JSON path double quotes, with
    /// embedded double quotes / backslashes escaped per the JSON spec
    /// (<c>$."weird\\\"name"</c>). The clean-name fast path returns the unquoted form so
    /// the common case stays allocation-light; everything else flows through the
    /// quote-and-escape path so the engines do not reject the literal with
    /// "Invalid JSON path expression".
    /// </summary>
    private string EscapeJsonPathPropertyName(
        string propertyName
    ) => IsSimpleIdentifier(propertyName)
        ? propertyName
        : BuildQuotedJsonPathSegment(propertyName);

    private static bool IsSimpleIdentifier(
        string name
    )
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var first = name[0];
        if (!(char.IsAsciiLetter(first) || first == '_'))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private string BuildQuotedJsonPathSegment(
        string name
    )
    {
        var isMariaDb = _singletonOptions.ServerVersion?.IsMariaDb == true;
        var sb = new StringBuilder(name.Length + 4);
        sb.Append('"');
        foreach (var c in name)
        {
            switch (c)
            {
                case '"':
                    // MySQL 8.4 rejects the `\"` escape inside a JSON-path quoted name
                    // ("Invalid JSON path expression at position N"); MariaDB 11.8 accepts
                    // both forms. Engine-discriminated: MariaDB takes the simple `\"`,
                    // MySQL takes `\\u0022` -- the double backslash survives MySQL's
                    // single-quoted-string parser (which strips a single `\` before the
                    // unrecognized `\u` escape) and arrives at the JSON path parser as
                    // `"`, which then decodes to a literal double-quote. Empirical
                    // probe (MySqlConnector direct, 2026-05-17) confirmed both shapes work
                    // on their respective engines and fail when swapped.
                    sb.Append(isMariaDb ? "\\\"" : @"\\u0022");
                    break;
                case '\\':
                    // Same engine asymmetry as for the double-quote: MariaDB's SQL parser
                    // preserves `\` literally in single-quoted strings outside the documented
                    // escape set, MySQL's SQL parser silently strips the leading `\`.
                    // MariaDB's `\\` survives both layers; MySQL needs `\\u005C` so the SQL
                    // parser ends up handing `\` to the JSON path parser which then
                    // decodes to a literal backslash.
                    sb.Append(isMariaDb ? @"\\" : @"\\u005C");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        // The path literal itself sits inside a single-quoted SQL string, so single
        // quotes embedded in the property name still need SQL-level doubling.
        return sb
            .ToString()
            .Replace("'", "''", StringComparison.Ordinal);
    }
}
