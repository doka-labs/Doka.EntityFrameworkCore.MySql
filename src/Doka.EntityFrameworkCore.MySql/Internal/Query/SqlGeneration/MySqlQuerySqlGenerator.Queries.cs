namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
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
    /// Gives unordered, identity-sensitive subqueries a stable key order.
    /// </summary>
    protected override Expression VisitSelect(
        SelectExpression selectExpression
    )
    {
        var orderMutationTarget = _mutationTargetTable is not null
            && selectExpression.Offset is not null
            && selectExpression.Orderings.Count == 0
            && selectExpression.Projection.Count > 0;

        if (orderMutationTarget)
        {
            // EF Core's mutation fallback projects the target key first.
            selectExpression = WithOrdering(selectExpression, selectExpression.Projection[0].Expression);
        }

        return base.VisitSelect(OrderLimitedApplyParents(selectExpression));
    }

    /// <summary>
    /// Stabilizes the parent selected by an unordered <c>LIMIT</c> before a correlated
    /// collection APPLY. EF Core projects the parent identity first for collection
    /// shaping, so the ordering does not add a projection or change an explicit order.
    /// </summary>
    private static SelectExpression OrderLimitedApplyParents(
        SelectExpression selectExpression
    )
    {
        if (!selectExpression.Tables.Any(table => MySqlTableExpressionHelper.GetApplySelect(table) is not null))
        {
            return selectExpression;
        }

        TableExpressionBase[]? rewrittenTables = null;

        for (var index = 0; index < selectExpression.Tables.Count; index++)
        {
            var table = selectExpression.Tables[index];

            if (table is not SelectExpression { Limit: not null, Orderings.Count: 0, Projection.Count: > 0, } parent)
            {
                continue;
            }

            rewrittenTables ??= selectExpression.Tables.ToArray();
            rewrittenTables[index] = WithOrdering(parent, parent.Projection[0].Expression);
        }

        return rewrittenTables is null
            ? selectExpression
            : selectExpression.Update(
                rewrittenTables,
                selectExpression.Predicate,
                selectExpression.GroupBy,
                selectExpression.Having,
                selectExpression.Projection,
                selectExpression.Orderings,
                selectExpression.Offset,
                selectExpression.Limit);
    }

    private static SelectExpression WithOrdering(
        SelectExpression selectExpression,
        SqlExpression expression
    ) => selectExpression.Update(
        selectExpression.Tables,
        selectExpression.Predicate,
        selectExpression.GroupBy,
        selectExpression.Having,
        selectExpression.Projection,
        [new OrderingExpression(expression, ascending: true)],
        selectExpression.Offset,
        selectExpression.Limit);

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
            ThrowIfLateralDerivedTablesAreUnsupported();
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
            ThrowIfLateralDerivedTablesAreUnsupported();
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
    /// Prevents an engine from receiving a LATERAL derived-table construct that its
    /// SQL grammar cannot parse. The disposition ID links the runtime boundary to
    /// the primary-source evidence and re-evaluation trigger in the specification
    /// ledger.
    /// </summary>
    private void ThrowIfLateralDerivedTablesAreUnsupported()
    {
        if (!Profile.Supports(ProviderCapability.LateralDerivedTables))
        {
            throw new InvalidOperationException(
                "The configured database engine cannot execute a correlated derived table "
                + "because its JOIN grammar "
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
    /// Keeps a multi-row inline operand grouped inside its enclosing set operation.
    /// </summary>
    /// <remarks>
    /// <see cref="GenerateValues"/> expands one <see cref="ValuesExpression"/> to
    /// multiple <c>UNION ALL SELECT</c> branches. Without parentheses, a parent
    /// <c>UNION</c> applies its distinctness only to the first branch and changes
    /// the result cardinality.
    /// </remarks>
    protected override void GenerateSetOperationOperand(
        SetOperationBase setOperation,
        SelectExpression operand
    )
    {
        if (operand is
            {
                Tables: [ValuesExpression { RowValues.Count: > 1 }],
                Predicate: null,
                GroupBy: [],
                Having: null,
                Orderings: [],
                Offset: null,
                Limit: null,
            })
        {
            Sql.AppendLine("(");

            using (Sql.Indent())
            {
                Visit(operand);
            }

            Sql.AppendLine();
            Sql.Append(")");
            return;
        }

        base.GenerateSetOperationOperand(setOperation, operand);
    }

}
