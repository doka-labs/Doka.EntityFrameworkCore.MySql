namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Preserves the common schema of a shared definition through EF's ordinary pruning pass.
/// </summary>
internal sealed class MySqlMaterializedSourcePruner
    : SqlTreePruner, IMySqlMaterializedSourceVisitor
{
    private readonly bool _pruneStatementColumns;

    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlMaterializedSourcePruner(
        bool pruneStatementColumns = false
    )
    {
        _pruneStatementColumns = pruneStatementColumns;
    }

#pragma warning disable EF1001 // Provider SQL pruning seam, qualified by the EF patch matrix; see D-024.
    /// <inheritdoc />
    public SelectExpression VisitMaterializedSource(
        SelectExpression source
    ) => PruneSelect(source, preserveProjection: true);
#pragma warning restore EF1001
    /// <inheritdoc />
    public override Expression Prune(
        Expression expression
    ) => base.Prune(_pruneStatementColumns
        ? MySqlCteColumnPruner.Prune(expression)
        : expression);

    /// <inheritdoc />
    protected override Expression VisitExtension(
        Expression node
    )
    {
        if (node is MySqlInsertSelectExpression insert)
        {
            // WHY: INSERT assignments consume the complete positional SELECT projection even
            // when no outer SQL column points at it and the SELECT retains a nonnull alias.
#pragma warning disable EF1001 // Exact EF11 top-level projection-pruning seam, qualified by the EF patch matrix.
            return insert.Update(PruneSelect(insert.Source, preserveProjection: true));
#pragma warning restore EF1001
        }

        return base.VisitExtension(node);
    }
}
