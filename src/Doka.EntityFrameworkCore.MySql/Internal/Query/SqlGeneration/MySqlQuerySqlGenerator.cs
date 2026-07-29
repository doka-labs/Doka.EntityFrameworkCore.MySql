namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlQuerySqlGenerator : QuerySqlGenerator
{
    private const string OffsetWithoutLimitSentinel = "18446744073709551615";

    private readonly MySqlSingletonOptions _singletonOptions;
    private TableExpression? _mutationTargetTable;
    private string? _unqualifiedTableAlias;

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
            case { Name: "__mysql_json_set", Arguments.Count: 3 }:
                {
                    EmitJsonSet(sqlFunctionExpression);
                    return sqlFunctionExpression;
                }

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

            case { Name: "__mysql_datetime_diff_ticks", Arguments.Count: 2 }:
                {
                    EmitDateTimeDifferenceTicks(sqlFunctionExpression);
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
    /// Emits MySQL's multi-table delete syntax. The target alias must precede
    /// <c>FROM</c> whenever the source query keeps an alias or joins other tables.
    /// Single-table deletes with ordering or a limit omit the alias because MySQL
    /// permits those clauses only in the single-table form.
    /// </summary>
    protected override Expression VisitDelete(
        DeleteExpression deleteExpression
    )
    {
        ArgumentNullException.ThrowIfNull(deleteExpression);

        var previousTargetTable = _mutationTargetTable;
        _mutationTargetTable = deleteExpression.Table;

        try
        {
            return VisitDeleteCore(deleteExpression);
        }
        finally
        {
            _mutationTargetTable = previousTargetTable;
            _unqualifiedTableAlias = null;
        }
    }

    private DeleteExpression VisitDeleteCore(
        DeleteExpression deleteExpression
    )
    {
        var selectExpression = deleteExpression.SelectExpression;

        if (selectExpression.Offset is not null
            || selectExpression.Having is not null
            || selectExpression.GroupBy.Count > 0
            || selectExpression.Projection.Count > 0
            || (selectExpression.Tables.Count > 1
                && (selectExpression.Orderings.Count > 0 || selectExpression.Limit is not null)))
        {
            throw new InvalidOperationException(
                RelationalStrings.ExecuteOperationWithUnsupportedOperatorInSqlGeneration(
                    nameof(EntityFrameworkQueryableExtensions.ExecuteDelete)));
        }

        var useSingleTableSyntax = selectExpression.Tables.Count == 1
            && (selectExpression.Orderings.Count > 0 || selectExpression.Limit is not null);

        Sql.Append("DELETE");

        if (!useSingleTableSyntax)
        {
            Sql.Append(" ");
            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(deleteExpression.Table.Alias));
        }

        Sql.AppendLine();
        Sql.Append("FROM ");

        if (useSingleTableSyntax)
        {
            _unqualifiedTableAlias = selectExpression.Tables[0].Alias;
        }

        VisitTableSources(selectExpression.Tables);

        if (selectExpression.Predicate is not null)
        {
            Sql.AppendLine();
            Sql.Append("WHERE ");
            Visit(selectExpression.Predicate);
        }

        GenerateOrderings(selectExpression);
        GenerateLimitOffset(selectExpression);

        return deleteExpression;
    }

    /// <summary>
    /// Emits table sources before <c>SET</c>, as required by MySQL and MariaDB.
    /// EF Core's relational default uses an <c>UPDATE ... SET ... FROM ...</c>
    /// shape which is not part of either engine's grammar.
    /// </summary>
    protected override Expression VisitUpdate(
        UpdateExpression updateExpression
    )
    {
        ArgumentNullException.ThrowIfNull(updateExpression);

        var previousTargetTable = _mutationTargetTable;
        _mutationTargetTable = updateExpression.Table;

        try
        {
            return VisitUpdateCore(updateExpression);
        }
        finally
        {
            _mutationTargetTable = previousTargetTable;
        }
    }

    private UpdateExpression VisitUpdateCore(
        UpdateExpression updateExpression
    )
    {
        var selectExpression = updateExpression.SelectExpression;

        if (selectExpression.Offset is not null
            || selectExpression.Having is not null
            || selectExpression.Orderings.Count > 0
            || selectExpression.GroupBy.Count > 0
            || selectExpression.Projection.Count > 0)
        {
            throw new InvalidOperationException(
                RelationalStrings.ExecuteOperationWithUnsupportedOperatorInSqlGeneration(
                    nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)));
        }

        Sql.Append("UPDATE ");

        if (selectExpression.Tables.Count > 1)
        {
            var tables = selectExpression.Tables;
            var targetOccursInSource = tables.Any(table =>
                updateExpression.Table.Equals(table is JoinExpressionBase join ? join.Table : table));

            if (!targetOccursInSource)
            {
                Visit(updateExpression.Table);
                Sql.AppendLine(",");

                if (tables[0] is not JoinExpressionBase)
                {
                    tables = tables
                        .Skip(1)
                        .Prepend(new CrossJoinExpression(tables[0]))
                        .ToArray();
                }
            }

            VisitTableSources(tables);
        }
        else
        {
            Visit(updateExpression.Table);
        }

        Sql.AppendLine();
        Sql.Append("SET ");

        for (var index = 0; index < updateExpression.ColumnValueSetters.Count; index++)
        {
            if (index > 0)
            {
                Sql.AppendLine(",");
            }

            var setter = updateExpression.ColumnValueSetters[index];
            Visit(setter.Column);
            Sql.Append(" = ");
            Visit(setter.Value);
        }

        if (selectExpression.Predicate is not null)
        {
            Sql.AppendLine();
            Sql.Append("WHERE ");
            Visit(selectExpression.Predicate);
        }

        GenerateLimitOffset(selectExpression);

        return updateExpression;
    }

    private void VisitTableSources(
        IReadOnlyList<TableExpressionBase> tables
    )
    {
        for (var index = 0; index < tables.Count; index++)
        {
            if (index > 0)
            {
                Sql.AppendLine();
            }

            Visit(tables[index]);
        }
    }

    protected override Expression VisitColumn(
        ColumnExpression columnExpression
    )
    {
        if (columnExpression.TableAlias != _unqualifiedTableAlias)
        {
            return base.VisitColumn(columnExpression);
        }

        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columnExpression.Name));

        return columnExpression;
    }

    protected override Expression VisitTable(
        TableExpression tableExpression
    )
    {
        if (tableExpression.Alias != _unqualifiedTableAlias)
        {
            return base.VisitTable(tableExpression);
        }

        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableExpression.Name));

        return tableExpression;
    }

    /// <summary>
    /// Gives an unordered offset subquery a stable key order while it selects
    /// rows for a mutation. EF Core's mutation fallback projects the target key
    /// first, so the ordering does not add columns or alter the selected range.
    /// </summary>
    protected override Expression VisitSelect(
        SelectExpression selectExpression
    )
    {
        if (_mutationTargetTable is null
            || selectExpression.Offset is null
            || selectExpression.Orderings.Count > 0
            || selectExpression.Projection.Count == 0)
        {
            return base.VisitSelect(selectExpression);
        }

        selectExpression.AppendOrdering(
            new OrderingExpression(selectExpression.Projection[0].Expression, ascending: true));

        try
        {
            return base.VisitSelect(selectExpression);
        }
        finally
        {
            selectExpression.ClearOrdering();
        }
    }

    private const string DateAddSentinelPrefix = "__mysql_date_add_";
    private const string TimeAddSentinelPrefix = "__mysql_time_add_";

    /// <summary>
    /// Emits a signed, range-preserving <see cref="TimeSpan"/> value as
    /// 100-nanosecond ticks.
    /// </summary>
    /// <remarks>
    /// <c>TIMESTAMPDIFF(MICROSECOND, start, end)</c> returns <c>end - start</c>
    /// as an integer on both engines. Multiplying by ten converts microseconds
    /// to .NET ticks. Sources retrieved 2026-07-28:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/date-and-time-functions.html#function_timestampdiff">
    /// MySQL TIMESTAMPDIFF</see> and
    /// <see href="https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/timestampdiff">
    /// MariaDB TIMESTAMPDIFF</see>.
    /// </remarks>
    private void EmitDateTimeDifferenceTicks(
        SqlFunctionExpression expression
    )
    {
        var arguments = expression.Arguments
            ?? throw new InvalidOperationException(
                "The __mysql_datetime_diff_ticks sentinel requires two arguments.");

        Sql.Append("(TIMESTAMPDIFF(MICROSECOND, ");
        Visit(arguments[0]);
        Sql.Append(", ");
        Visit(arguments[1]);
        Sql.Append(") * 10)");
    }

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
    /// Translates the EF Core T-SQL idiom <c>CROSS APPLY &lt;table&gt;</c> into the
    /// MySQL form <c>JOIN LATERAL &lt;derived-table&gt; ON TRUE</c>. MySQL permits the
    /// <c>LATERAL</c> modifier only on derived tables; ordinary tables need a plain join,
    /// while table functions such as JSON_TABLE are already inherently lateral. MariaDB
    /// has no LATERAL derived-table grammar and therefore receives a precise engine-capability
    /// exception instead of syntactically invalid SQL.
    /// </summary>
    protected override Expression VisitCrossApply(
        CrossApplyExpression crossApplyExpression
    )
    {
        ArgumentNullException.ThrowIfNull(crossApplyExpression);

        if (RequiresLateralModifier(crossApplyExpression.Table))
        {
            ThrowIfMariaDbLateralDerivedTableIsRequired();
            Sql.Append("JOIN LATERAL ");
        }
        else
        {
            Sql.Append("JOIN ");
        }

        Visit(crossApplyExpression.Table);
        Sql.Append(" ON TRUE");
        return crossApplyExpression;
    }

    /// <summary>
    /// Translates <c>OUTER APPLY &lt;table&gt;</c> into the MySQL form
    /// <c>LEFT JOIN LATERAL &lt;derived-table&gt; ON TRUE</c>. The outer variant preserves
    /// left-hand rows whose lateral subquery produces no match. Ordinary tables and
    /// inherently lateral table functions use <c>LEFT JOIN</c> without the modifier;
    /// MariaDB receives the same explicit capability exception as
    /// <see cref="VisitCrossApply"/> for a correlated derived table.
    /// </summary>
    protected override Expression VisitOuterApply(
        OuterApplyExpression outerApplyExpression
    )
    {
        ArgumentNullException.ThrowIfNull(outerApplyExpression);

        if (RequiresLateralModifier(outerApplyExpression.Table))
        {
            ThrowIfMariaDbLateralDerivedTableIsRequired();
            Sql.Append("LEFT JOIN LATERAL ");
        }
        else
        {
            Sql.Append("LEFT JOIN ");
        }

        Visit(outerApplyExpression.Table);
        Sql.Append(" ON TRUE");
        return outerApplyExpression;
    }

    /// <summary>
    /// Identifies derived-table shapes which require MySQL's explicit
    /// <c>LATERAL</c> modifier when they reference a preceding table.
    /// </summary>
    private static bool RequiresLateralModifier(
        TableExpressionBase tableExpression
    ) => tableExpression is SelectExpression or SetOperationBase;

    /// <summary>
    /// Prevents MariaDB from receiving a LATERAL derived-table construct that its SQL
    /// grammar cannot parse. The disposition ID links the runtime boundary to the
    /// primary-source evidence and re-evaluation trigger in the specification ledger.
    /// </summary>
    private void ThrowIfMariaDbLateralDerivedTableIsRequired()
    {
        if (_singletonOptions.ServerVersion?.IsMariaDb == true)
        {
            throw new InvalidOperationException(
                "MariaDB cannot execute a correlated derived table because its JOIN grammar "
                + "does not support LATERAL. See disposition MDB-CORRELATED-DERIVED-TABLE.");
        }
    }

    /// <summary>
    /// Emits an inline rowset as a sequence of <c>SELECT</c> branches.
    /// </summary>
    /// <remarks>
    /// EF Core's relational default emits the first row as <c>SELECT</c> and later rows as
    /// <c>UNION ALL VALUES</c>. MySQL and MariaDB diverge on that table-value-constructor
    /// grammar, while <c>UNION ALL SELECT</c> preserves the same values, duplicates, and order
    /// on every supported target.
    /// </remarks>
    protected override void GenerateValues(
        ValuesExpression valuesExpression
    )
    {
        ArgumentNullException.ThrowIfNull(valuesExpression);

        var rowValues = valuesExpression.RowValues
            ?? throw new InvalidOperationException(
                "Parameterized inline rowsets must be expanded before SQL generation.");

        if (rowValues.Count == 0)
        {
            throw new InvalidOperationException(RelationalStrings.EmptyCollectionNotSupportedAsInlineQueryRoot);
        }

        for (var rowIndex = 0; rowIndex < rowValues.Count; rowIndex++)
        {
            if (rowIndex > 0)
            {
                Sql.AppendLine();
                Sql.AppendLine("UNION ALL");
            }

            Sql.Append("SELECT ");

            var values = rowValues[rowIndex].Values;

            for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
            {
                if (columnIndex > 0)
                {
                    Sql.Append(", ");
                }

                Visit(values[columnIndex]);

                if (rowIndex == 0)
                {
                    Sql.Append(" AS ");
                    Sql.Append(
                        Dependencies.SqlGenerationHelper.DelimitIdentifier(valuesExpression.ColumnNames[columnIndex]));
                }
            }
        }
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
        var requiresLimitIsolation = subquery is { Limit: not null, } or { Offset: not null, };
        var requiresTargetIsolation = subquery is not null && RequiresMutationTargetIsolation(subquery);

        if (!requiresLimitIsolation && !requiresTargetIsolation)
        {
            base.GenerateIn(inExpression, negated);
            return;
        }

        Visit(inExpression.Item);
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

    private bool RequiresMutationTargetIsolation(
        SelectExpression subquery
    )
    {
        if (_singletonOptions.ServerVersion?.IsMariaDb != false
            || _mutationTargetTable is null)
        {
            return false;
        }

        var visitor = new TargetTableFindingExpressionVisitor(_mutationTargetTable);
        visitor.Visit(subquery);
        return visitor.Found;
    }

    private sealed class TargetTableFindingExpressionVisitor : ExpressionVisitor
    {
        private readonly TableExpression _targetTable;

        public TargetTableFindingExpressionVisitor(
            TableExpression targetTable
        )
        {
            _targetTable = targetTable;
        }

        public bool Found { get; private set; }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is TableExpression table
                && table.Name == _targetTable.Name
                && table.Schema == _targetTable.Schema)
            {
                Found = true;
                return node;
            }

            return Found ? node : base.VisitExtension(node);
        }
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

        // Default path: always JSON_UNQUOTE. The earlier branches (bool wrapper + CAST
        // path) cover every CLR type whose JSON representation is a non-string primitive
        // (boolean, number). Everything that reaches here was serialized into JSON as a
        // string and the .NET shaper needs the unquoted text form: string, Guid (e.g.
        // `"12345678-..."`), DateTimeOffset (e.g. `"2000-01-01 12:34:56-08:00"`), char,
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

        if (sqlUnaryExpression is not { OperatorType: ExpressionType.Convert, TypeMapping: { } typeMapping })
        {
            return base.VisitSqlUnary(sqlUnaryExpression);
        }

        var operandType = sqlUnaryExpression.Operand.Type;
        var castTarget = sqlUnaryExpression.Type == typeof(decimal)
            && (operandType == typeof(double) || operandType == typeof(float))
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
            "float" => "FLOAT",
            "double" or "real" => "DOUBLE",
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
