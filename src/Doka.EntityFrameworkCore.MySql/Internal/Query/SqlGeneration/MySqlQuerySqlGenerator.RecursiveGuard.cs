namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    private MySqlRecursiveQueryPolicy? _recursiveQueryPolicy;
    private long _recursiveTextCapacityBytes;

    /// <summary>Preserves a control row even when the completed recursive result is empty.</summary>
    private void GenerateGuardedRecursiveSelect(
        SelectExpression select
    )
    {
        var shapes = _recursiveCteShapes
            ?? throw new InvalidOperationException("A recursive result requires its qualified definitions.");

        var engine = Profile.Engine;
        var ceiling = engine.Has(EngineCapability.RecursiveCteLimitUsesMaxRecursiveIterations)
            ? "@@max_recursive_iterations"
            : engine.Has(EngineCapability.RecursiveCteLimitUsesCteMaxRecursionDepth)
                ? "@@cte_max_recursion_depth"
                : throw new InvalidOperationException("The configured engine has no qualified recursive-depth guard.");

        var names = select
            .Projection
            .Select(projection => projection.Alias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projections = new List<ProjectionExpression>(select.Projection.Count + select.Orderings.Count + 2);
        var integerMapping = shapes.Values.First()
                .Frontier.MaximumDepth.TypeMapping
            ?? throw new InvalidOperationException("Recursive depth requires an integer type mapping.");

        // WHY: A final EF SELECT can have empty or repeated labels, especially scalar
        // calculations and two entity projections. Derived tables need distinct real names;
        // preserve visible ordinal order while giving this private scope its own aliases.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < select.Projection.Count; index++)
        {
            projections.Add(new ProjectionExpression(select.Projection[index].Expression,
                ReserveRecursiveName(names, "__doka_recursive_col_" + index.ToString(CultureInfo.InvariantCulture))));
        }

        if (projections.Count == 0)
        {
            // WHY: EF prints SELECT 1 for an empty projection. Keep that visible placeholder
            // rather than changing its reader shape when the control columns are appended.
            projections.Add(new ProjectionExpression(new SqlConstantExpression(1, integerMapping),
                ReserveRecursiveName(names, "__doka_empty")));
        }

        var payloadCount = projections.Count;
        var orderingNames = AddRecursiveOrderingProjections(select, projections, names);
        var presenceName = ReserveRecursiveName(names, "__doka_recursive_row");
        var limitName = ReserveRecursiveName(names, "__doka_recursive_limit");
        projections.Add(new ProjectionExpression(new SqlConstantExpression(1, integerMapping), presenceName));
        var payload = select
            .Update(select.Tables,
                select.Predicate,
                select.GroupBy,
                select.Having,
                projections,
                select.Orderings,
                select.Offset,
                select.Limit)
            .WithAlias("__doka_recursive_values");

        const string valuesAlias = "__doka_recursive_values";
        const string guardAlias = "__doka_recursion_guard";
        Sql.Append("SELECT ");

        // WHY: Only original visible columns precede the two private controls. Ordering-only
        // projections stay inside the derived result and never alter EF's materializer ordinals.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < payloadCount; index++)
        {
            AppendRecursiveColumn(valuesAlias, projections[index].Alias);

            if (index < select.Projection.Count
                && select.Projection[index].Alias.Length != 0)
            {
                Sql
                    .Append(" AS ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(select.Projection[index].Alias));
            }

            Sql.Append(", ");
        }

        AppendRecursiveColumn(valuesAlias, presenceName);
        Sql.Append(", ");
        AppendRecursiveColumn(guardAlias, limitName);
        Sql.AppendLine();
        Sql.AppendLine("FROM (");
        var parameters = new HashSet<string>(StringComparer.Ordinal);

        using (Sql.Indent())
        {
            Sql.Append("SELECT CASE WHEN ");
            var first = true;

            foreach (var shape in shapes.Values)
            {
                if (!first)
                {
                    Sql.Append(" OR ");
                }

                // WHY: Both engines need a final empty iteration. Equality to their ceiling
                // is therefore unsafe even when the semantic depth counts the anchor as zero.
                Visit(shape.Frontier.MaximumDepth);
                Sql
                    .Append(" >= ")
                    .Append(ceiling);

                first = false;

                if (shape.Frontier.MaximumDepth is SqlParameterExpression parameter)
                {
                    parameters.Add(parameter.Name);
                }
            }

            Sql.Append(" THEN 1");

            if (_recursiveTextCapacityBytes != 0)
            {
                // WHY: A finite CHAR bound can still return NULL when its declared or
                // actual encoded size exceeds the native packet limit. Worst-case charset
                // bytes give a conservative per-string proof; equality is natively allowed.
                Sql
                    .Append(" WHEN ")
                    .Append(_recursiveTextCapacityBytes.ToString(CultureInfo.InvariantCulture))
                    .Append(" > @@max_allowed_packet THEN 2");
            }

            Sql
                .Append(" ELSE 0 END AS ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(limitName));

            Sql.AppendLine();
        }

        Sql
            .Append(") AS ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(guardAlias))
            .AppendLine();

        Sql.Append("LEFT JOIN ");
        Visit(payload);
        Sql.Append(" ON ");
        AppendRecursiveColumn(guardAlias, limitName);
        Sql.AppendLine(" = 0");

        if (orderingNames.Length != 0)
        {
            Sql.Append("ORDER BY ");

            // WHY: Native derived-table ordering alone is not an outer result-order guarantee.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < orderingNames.Length; index++)
            {
                if (index != 0)
                {
                    Sql.Append(", ");
                }

                AppendRecursiveColumn(valuesAlias, orderingNames[index]);

                if (!select.Orderings[index].IsAscending)
                {
                    Sql.Append(" DESC");
                }
            }
        }

        _recursiveQueryPolicy =
            new MySqlRecursiveQueryPolicy(parameters.ToArray(), presenceName, limitName, payloadCount);
    }

    /// <summary>Carries sort keys without changing a DISTINCT result's visible equivalence classes.</summary>
    private static string[] AddRecursiveOrderingProjections(
        SelectExpression select,
        List<ProjectionExpression> projections,
        HashSet<string> names
    )
    {
        var result = new string[select.Orderings.Count];

        // WHY: Paired indexed access keeps each actual ordering direction aligned with its alias.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < result.Length; index++)
        {
            var expression = select.Orderings[index].Expression;
            var projection = projections.Find(candidate => candidate.Expression.Equals(expression));

            if (projection is null)
            {
                if (select.IsDistinct)
                {
                    throw new InvalidOperationException(
                        "A recursive DISTINCT result must project its outer ordering keys.");
                }

                projection =
                    new ProjectionExpression(expression, ReserveRecursiveName(names, "__doka_recursive_order"));

                projections.Add(projection);
            }

            result[index] = projection.Alias;
        }

        return result;
    }

    /// <summary>Emits qualified generated identifiers without interpreting SQL text.</summary>
    private void AppendRecursiveColumn(
        string alias,
        string name
    ) => Sql
        .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(alias))
        .Append(".")
        .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name));

    /// <summary>Reserves a short statement-local output name outside the user's projected aliases.</summary>
    private static string ReserveRecursiveName(
        HashSet<string> names,
        string prefix
    )
    {
        var name = prefix;
        var suffix = 0;

        while (!names.Add(name))
        {
            name = prefix + "_" + (++suffix).ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }
}
