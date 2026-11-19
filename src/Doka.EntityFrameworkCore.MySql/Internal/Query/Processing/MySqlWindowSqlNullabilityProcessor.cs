namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Preserves nullable window results while using EF's ordinary null semantics for every child.</summary>
internal sealed class MySqlWindowSqlNullabilityProcessor : SqlNullabilityProcessor
{
    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlWindowSqlNullabilityProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters
    ) : base(dependencies, parameters) { }

    /// <inheritdoc />
    protected override SqlExpression VisitCustomSqlExpression(
        SqlExpression sqlExpression,
        bool allowOptimizedExpansion,
        out bool nullable
    )
    {
        if (sqlExpression is MySqlRecursiveTextCastExpression textCast)
        {
            return textCast.Update(Visit(textCast.Operand, out nullable));
        }

        if (sqlExpression is not MySqlWindowExpression window)
        {
            return base.VisitCustomSqlExpression(sqlExpression, allowOptimizedExpansion, out nullable);
        }

        // WHY: LAG and empty frames may yield NULL even when every input column is required.
        // Argument-null propagation cannot describe missing-neighbor or empty-frame semantics.
        nullable = window.IsNullable;
        var arguments = VisitWindowItems(window.Arguments);
        var partitions = VisitWindowItems(window.Partitions);
        var orderings = new OrderingExpression[window.Orderings.Count];

        // WHY: The stored read-only wrapper would allocate an interface enumerator.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < orderings.Length; index++)
        {
            var ordering = window.Orderings[index];
            orderings[index] = ordering.Update(Visit(ordering.Expression, out _));
        }

        var frame = window.Frame;

        if (frame is not null)
        {
            frame = frame with
            {
                StartOffset = frame.StartOffset is null
                    ? null
                    : Visit(frame.StartOffset, out _),
                EndOffset = frame.EndOffset is null
                    ? null
                    : Visit(frame.EndOffset, out _),
            };
        }

        return window.Update(arguments, partitions, orderings, frame);
    }

    private SqlExpression[] VisitWindowItems(
        IReadOnlyList<SqlExpression> items
    )
    {
        var visited = new SqlExpression[items.Count];

        // WHY: Indexed traversal avoids allocating an enumerator for each window's read-only child list.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < visited.Length; index++)
        {
            visited[index] = Visit(items[index], out _);
        }

        return visited;
    }
}
