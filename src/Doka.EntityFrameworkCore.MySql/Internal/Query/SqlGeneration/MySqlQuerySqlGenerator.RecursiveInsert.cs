namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    private ReadOnlyMemory<string> _recursiveWriteDepthParameters;

    /// <summary>Fails an unsafe recursive INSERT even when its completed source has no visible rows.</summary>
    private void GenerateGuardedRecursiveInsertSource(
        SelectExpression select
    )
    {
        var shapes = _recursiveCteShapes
            ?? throw new InvalidOperationException("A recursive INSERT requires qualified recursive definitions.");

        if (select.Projection.Count == 0)
        {
            throw new InvalidOperationException("A recursive INSERT requires at least one assigned target value.");
        }

        var engine = Profile.Engine;
        var ceiling = engine.Has(EngineCapability.RecursiveCteLimitUsesMaxRecursiveIterations)
            ? "@@max_recursive_iterations"
            : engine.Has(EngineCapability.RecursiveCteLimitUsesCteMaxRecursionDepth)
                ? "@@cte_max_recursion_depth"
                : throw new InvalidOperationException("The configured engine has no qualified recursive-depth guard.");

        var projections = new ProjectionExpression[select.Projection.Count];

        // WHY: Scalar and entity projections can repeat or omit labels. This private
        // derived scope needs distinct names but must retain the target's ordinal order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < projections.Length; index++)
        {
            projections[index] = new ProjectionExpression(select.Projection[index].Expression,
                "__doka_write_col_" + index.ToString(CultureInfo.InvariantCulture));
        }

        const string alias = "__doka_recursive_insert";
        var payload = select
            .Update(select.Tables,
                select.Predicate,
                select.GroupBy,
                select.Having,
                projections,
                select.Orderings,
                select.Offset,
                select.Limit)
            .WithAlias(alias);

        var parameters = new HashSet<string>(StringComparer.Ordinal);
        Sql.Append("SELECT ");

        // WHY: Reader control columns cannot enter an INSERT's positional target list.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < projections.Length; index++)
        {
            if (index != 0)
            {
                Sql.Append(", ");
            }

            AppendRecursiveColumn(alias, projections[index].Alias);
        }

        Sql
            .AppendLine()
            .Append("FROM ");

        Visit(payload);
        Sql
            .AppendLine()
            .Append("WHERE NOT (");

        GenerateRecursiveInsertLimitPredicate(shapes, ceiling, parameters);
        Sql.AppendLine(")");
        Sql.AppendLine("UNION ALL");

        // WHY: A predicate on the payload alone disappears for empty/filtered/Take(0)
        // sources. This independent scalar-subquery error arm raises native error 1242
        // before a target row is inserted. Untyped NULL avoids store-specific cast rules;
        // its UNION metadata is deliberately never consumed by an EF result materializer.
        Sql.Append("SELECT (SELECT NULL UNION ALL SELECT NULL)");

        // WHY: The unreachable-on-success arm must have exactly the target's arity.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 1; index < projections.Length; index++)
        {
            Sql.Append(", NULL");
        }

        Sql
            .AppendLine()
            .Append("WHERE ");

        GenerateRecursiveInsertLimitPredicate(shapes, ceiling, parameters);
        _recursiveWriteDepthParameters = parameters.ToArray();
    }

    /// <summary>Checks every bound recursive budget against the current physical session's native ceiling.</summary>
    private void GenerateRecursiveInsertLimitPredicate(
        IReadOnlyDictionary<int, MySqlRecursiveCteShape> shapes,
        string ceiling,
        HashSet<string> parameters
    )
    {
        var first = true;

        foreach (var shape in shapes.Values)
        {
            if (!first)
            {
                Sql.Append(" OR ");
            }

            // WHY: Both engines require a final empty iteration, so equality is unsafe.
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

        if (_recursiveTextCapacityBytes != 0)
        {
            // WHY: The same independent error arm must protect empty sources from
            // successful NULL/truncated writes caused by an unsafe recursive text cast.
            Sql
                .Append(" OR ")
                .Append(_recursiveTextCapacityBytes.ToString(CultureInfo.InvariantCulture))
                .Append(" > @@max_allowed_packet");
        }
    }
}
