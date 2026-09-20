namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    private bool _isInsertSelectSource;
    private Dictionary<int, string>? _materializedSourceNames;

    protected override void GenerateRootCommand(
        Expression queryExpression
    )
    {
        if (queryExpression is MySqlInsertSelectExpression insert)
        {
            var previous = _isInsertSelectSource;
            _isInsertSelectSource = true;

            try
            {
                GenerateInsertSelect(insert);
            }
            finally
            {
                _isInsertSelectSource = previous;
            }

            return;
        }

        var inventory = new MaterializedSourceInventory();
        inventory.Visit(queryExpression);
        _recursiveTextCapacityBytes = inventory.MaximumRecursiveTextBytes;

        if (inventory.Definitions.Count == 0)
        {
            base.GenerateRootCommand(queryExpression);
            AppendLockingReadClause(queryExpression);

            return;
        }

        if (!Profile.Supports(ProviderCapability.CommonTableExpressions))
        {
            throw new InvalidOperationException(
                "Materialized sources require common table expression support on the configured engine.");
        }

        if (queryExpression is not SelectExpression)
        {
            ValidateCteMutation(queryExpression, inventory);
        }

        // WHY: Inventory includes bindings inside scalar subqueries. Root-table validation
        // alone cannot prevent the WITH generation path from losing the final locking clause.
        if (queryExpression is SelectExpression lockingSelect
            && MySqlLockingReadMetadata.GetMode(lockingSelect) != MySqlLockingReadMode.None)
        {
            throw new InvalidOperationException("Locking reads cannot target materialized sources.");
        }

        _materializedSourceNames = [];
        _recursiveCteShapes = null;

        foreach (var definition in inventory.Definitions)
        {
            var prefix = definition.Kind switch
                {
                    MySqlSourceBindingKind.Ordinary => "__doka_cte_",
                    MySqlSourceBindingKind.Recursive => "__doka_recursive_",
                    _ => "__doka_source_",
                }
                + definition.BindingId.ToString(CultureInfo.InvariantCulture);

            var name = prefix;
            var suffix = 0;

            while (inventory.PhysicalNames.Contains(name)
                || inventory.RawSql.Any(sql => sql.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                // WHY: Raw SQL may contain arbitrarily long underscore runs. Appending underscores
                // then exceeds the server's identifier limit; an integer suffix keeps names bounded.
                name = prefix + "_" + (++suffix).ToString(CultureInfo.InvariantCulture);
            }

            inventory.PhysicalNames.Add(name);
            _materializedSourceNames.Add(definition.BindingId, name);

            if (definition.Kind == MySqlSourceBindingKind.Recursive)
            {
                (_recursiveCteShapes ??= []).Add(definition.BindingId, MySqlRecursiveCteShape.Create(definition));
            }
        }

        // WHY: Only the explicit shared-value fence needs the unreachable recursive arm.
        // Ordinary CTEs retain the native optimizer's merge and evaluation decisions.
        Sql.AppendLine(inventory.Definitions.Any(definition => definition.Kind != MySqlSourceBindingKind.Ordinary)
            ? "WITH RECURSIVE"
            : "WITH");

        for (var index = 0; index < inventory.Definitions.Count; index++)
        {
            var definition = inventory.Definitions[index];
            var name = _materializedSourceNames[definition.BindingId];
            var projections = definition.Source.Projection;
            var columns = projections.Count == 0
                ? ["__empty"]
                : projections
                    .Select(projection => projection.Alias)
                    .ToArray();

            if (definition.Kind == MySqlSourceBindingKind.Recursive)
            {
                var shape = _recursiveCteShapes?.GetValueOrDefault(definition.BindingId)
                    ?? throw new InvalidOperationException("The recursive CTE definition has no qualified SQL shape.");

                columns = [.. columns, shape.DepthColumnName];
            }

            Sql
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                .Append(" (");

            Sql.Append(string.Join(", ", columns.Select(Dependencies.SqlGenerationHelper.DelimitIdentifier)));
            Sql.AppendLine(") AS (");

            using (Sql.Indent())
            {
                if (definition.Kind == MySqlSourceBindingKind.Recursive)
                {
                    GenerateRecursiveCteSource(_recursiveCteShapes![definition.BindingId]);
                }
                else if (definition.Kind == MySqlSourceBindingKind.Ordinary)
                {
                    GenerateOrdinaryCteSource(definition.Source, columns);
                }
                else
                {
                    // The derived seed keeps its completed ORDER BY/LIMIT legal before UNION ALL.
                    Sql.AppendLine("SELECT *");
                    Sql.Append("FROM ");
                    Visit(definition.Source);
                    Sql.AppendLine();
                    Sql.AppendLine("UNION ALL");
                    Sql.Append("SELECT ");
                    Sql.Append(string.Join(", ", columns.Select(Dependencies.SqlGenerationHelper.DelimitIdentifier)));
                    Sql.AppendLine();
                    Sql
                        .Append("FROM ")
                        .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name));

                    Sql.AppendLine();
                    Sql.AppendLine("WHERE 1 = 0");
                }
            }

            Sql
                .Append(
                    index + 1 == inventory.Definitions.Count
                        ? ")"
                        : "),")
                .AppendLine();
        }

        if (_recursiveCteShapes is { Count: > 0 }
            && queryExpression is SelectExpression recursiveSelect)
        {
            GenerateTagsHeaderComment(recursiveSelect.Tags);

            if (_isInsertSelectSource)
            {
                GenerateGuardedRecursiveInsertSource(recursiveSelect);
            }
            else
            {
                GenerateGuardedRecursiveSelect(recursiveSelect);
            }
        }
        else
        {
            base.GenerateRootCommand(queryExpression);
        }

        _materializedSourceNames = null;
        _recursiveCteShapes = null;
    }

    protected override Expression VisitExtension(
        Expression extensionExpression
    )
    {
        if (extensionExpression is MySqlRecursiveTextCastExpression textCast)
        {
            GenerateRecursiveTextCast(textCast);

            return textCast;
        }

        if (extensionExpression is MySqlWindowExpression window)
        {
            GenerateWindow(window);

            return window;
        }

        if (extensionExpression is MySqlRecursiveCteReferenceExpression frontier)
        {
            if (_materializedSourceNames is null
                || !_materializedSourceNames.TryGetValue(frontier.BindingId, out var recursiveName))
            {
                throw new InvalidOperationException("A recursive CTE frontier escaped its SQL statement.");
            }

            Sql
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(recursiveName))
                .Append(AliasSeparator)
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(frontier.Alias!));

            return frontier;
        }

        if (extensionExpression is MySqlMaterializedSourceExpression reference)
        {
            if (_materializedSourceNames is null
                || !_materializedSourceNames.TryGetValue(reference.BindingId, out var name))
            {
                throw new InvalidOperationException("A materialized-source reference escaped its SQL statement.");
            }

            Sql
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                .Append(AliasSeparator)
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(reference.Alias!));

            return reference;
        }

        return base.VisitExtension(extensionExpression);
    }

    /// <summary>
    /// Collects definitions in dependency order and avoids physical-name shadowing.
    /// </summary>
    private sealed class MaterializedSourceInventory : ExpressionVisitor
    {
        private readonly HashSet<int> _visited = [];
        private HashSet<int>? _visiting;

        public List<MySqlMaterializedSourceExpression> Definitions { get; } = [];

        public HashSet<string> PhysicalNames { get; } = [with(StringComparer.OrdinalIgnoreCase)];

        public List<string> RawSql { get; } = [];

        /// <summary>Gets the largest proved per-string byte capacity required by a recursive anchor cast.</summary>
        public long MaximumRecursiveTextBytes { get; private set; }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            switch (node)
            {
                case MySqlRecursiveTextCastExpression text:
                    var width = MySqlIndexByteBudget.GetBytesPerCharacter(text.CharSet)
                        ?? throw new InvalidOperationException(
                            "Recursive text requires a known native character-set byte width.");

                    MaximumRecursiveTextBytes =
                        Math.Max(MaximumRecursiveTextBytes, checked((long)text.Capacity * width));

                    break;

                case MySqlMaterializedSourceExpression reference:
                    // WHY: Ordinary queries must not allocate a cycle-tracking set when no binding exists.
                    var visiting = _visiting ??= [];

                    if (visiting.Contains(reference.BindingId))
                    {
                        throw new InvalidOperationException(
                            "Statement-local source definitions must form an acyclic graph.");
                    }

                    if (_visited.Add(reference.BindingId))
                    {
                        visiting.Add(reference.BindingId);
                        Visit(reference.Source);
                        visiting.Remove(reference.BindingId);
                        Definitions.Add(reference);
                    }

                    return node;

                case TableExpression table:
                    PhysicalNames.Add(table.Name);
                    break;

                case FromSqlExpression fromSql:
                    RawSql.Add(fromSql.Sql);
                    break;
            }

            return base.VisitExtension(node);
        }
    }
}
