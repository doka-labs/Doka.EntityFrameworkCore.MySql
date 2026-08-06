namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
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
        var applicationTimeTable = GetApplicationTimeMutationTable(
            selectExpression,
            deleteExpression.Table,
            nameof(EntityFrameworkQueryableExtensions.ExecuteDelete));

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

        var useSingleTableSyntax = applicationTimeTable is not null
            || (selectExpression.Tables.Count == 1
                && (selectExpression.Orderings.Count > 0 || selectExpression.Limit is not null));

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
            _unqualifiedTableAlias = applicationTimeTable?.Alias
                ?? selectExpression.Tables[0].Alias;
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
            _unqualifiedTableAlias = null;
        }
    }

    private UpdateExpression VisitUpdateCore(
        UpdateExpression updateExpression
    )
    {
        var selectExpression = updateExpression.SelectExpression;
        var applicationTimeTable = GetApplicationTimeMutationTable(
            selectExpression,
            updateExpression.Table,
            nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate));

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

        if (applicationTimeTable is not null)
        {
            _unqualifiedTableAlias = applicationTimeTable.Alias;
            Visit(applicationTimeTable);
        }
        else if (selectExpression.Tables.Count > 1)
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

    private static TableExpression? GetApplicationTimeMutationTable(
        SelectExpression selectExpression,
        TableExpression targetTable,
        string operationName
    )
    {
        var finder = new ApplicationTimeTableFindingExpressionVisitor();
        finder.Visit(selectExpression);

        if (finder.Tables.Count == 0)
        {
            return null;
        }

        if (finder.Tables.Count != 1
            || selectExpression.Tables is not [TableExpression applicationTimeTable]
            || !ReferenceEquals(finder.Tables[0], applicationTimeTable)
            || applicationTimeTable.Name != targetTable.Name
            || applicationTimeTable.Schema != targetTable.Schema)
        {
            throw new InvalidOperationException(
                $"{operationName} with FOR PORTION OF requires one directly mapped mutation table. "
                + "Joins, derived tables and multi-table inheritance mutations are not valid "
                + "MariaDB application-time DML shapes.");
        }

        return applicationTimeTable;
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

    private bool RequiresMutationTargetIsolation(
        SelectExpression subquery
    )
    {
        if (Profile.GetSupport(ProviderCapability.SelfReferencingMutations) != ProviderSupportStatus.Emulated
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

    private sealed class ApplicationTimeTableFindingExpressionVisitor : ExpressionVisitor
    {
        public List<TableExpression> Tables { get; } = [];

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is TableExpression table
                && table.FindAnnotation(MySqlAnnotationNames.ApplicationTimeOperation)?.Value is true)
            {
                Tables.Add(table);
            }

            return base.VisitExtension(node);
        }
    }
}
