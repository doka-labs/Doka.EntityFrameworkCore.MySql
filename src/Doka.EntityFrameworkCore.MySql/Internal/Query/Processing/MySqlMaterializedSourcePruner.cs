namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Preserves the common schema of a shared definition through EF's ordinary pruning pass.
/// </summary>
internal sealed class MySqlMaterializedSourcePruner : SqlTreePruner, IMySqlMaterializedSourceVisitor
{
#pragma warning disable EF1001 // Provider SQL pruning seam, qualified by the EF patch matrix; see D-024.
    /// <inheritdoc />
    public SelectExpression VisitMaterializedSource(
        SelectExpression source
    ) => PruneSelect(source, preserveProjection: true);
#pragma warning restore EF1001
}
