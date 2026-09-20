namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    private Dictionary<int, string>? _materializedSourceNames;

    protected override void GenerateRootCommand(
        Expression queryExpression
    )
    {
        var inventory = new MaterializedSourceInventory();
        inventory.Visit(queryExpression);

        if (inventory.Definitions.Count == 0)
        {
            base.GenerateRootCommand(queryExpression);

            return;
        }

        if (!Profile.Supports(ProviderCapability.CommonTableExpressions))
        {
            throw new InvalidOperationException(
                "Materialized sources require common table expression support on the configured engine.");
        }

        if (queryExpression is not SelectExpression)
        {
            throw new InvalidOperationException("Materialized sources compose read queries only.");
        }

        _materializedSourceNames = [];

        foreach (var definition in inventory.Definitions)
        {
            var prefix = "__doka_source_" + definition.BindingId.ToString(CultureInfo.InvariantCulture);
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
        }

        // WHY: MariaDB clones ordinary CTEs per reference. An unreachable recursive arm forces
        // one logical source evaluation on both families, including volatile selection and values.
        Sql.AppendLine("WITH RECURSIVE");

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

            Sql
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                .Append(" (");
            Sql.Append(string.Join(", ", columns.Select(Dependencies.SqlGenerationHelper.DelimitIdentifier)));
            Sql.AppendLine(") AS (");

            using (Sql.Indent())
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

            Sql
                .Append(index + 1 == inventory.Definitions.Count ? ")" : "),")
                .AppendLine();
        }

        base.GenerateRootCommand(queryExpression);
        _materializedSourceNames = null;
    }

    protected override Expression VisitExtension(
        Expression extensionExpression
    )
    {
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

        public List<MySqlMaterializedSourceExpression> Definitions { get; } = [];

        public HashSet<string> PhysicalNames { get; } = [with(StringComparer.OrdinalIgnoreCase)];

        public List<string> RawSql { get; } = [];

        protected override Expression VisitExtension(
            Expression node
        )
        {
            switch (node)
            {
                case MySqlMaterializedSourceExpression reference:
                    if (_visited.Add(reference.BindingId))
                    {
                        Visit(reference.Source);
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
