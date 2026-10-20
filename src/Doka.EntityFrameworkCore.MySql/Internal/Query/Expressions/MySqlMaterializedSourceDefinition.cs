namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Owns an immutable statement definition and visits its dependency graph once per EF visitor.
/// </summary>
internal sealed class MySqlMaterializedSourceDefinition(SelectExpression source)
{
    private readonly ConditionalWeakTable<ExpressionVisitor, MySqlMaterializedSourceDefinition> _visited = new();

    /// <summary>Gets the immutable, fully projected SQL definition.</summary>
    public SelectExpression Source { get; } = source;

    /// <summary>Reuses one rewrite of this definition for the current EF visitor.</summary>
    public MySqlMaterializedSourceDefinition Visit(
        ExpressionVisitor visitor
    ) => _visited.GetValue(visitor, VisitCore);

    private MySqlMaterializedSourceDefinition VisitCore(
        ExpressionVisitor visitor
    )
    {
        // WHY: References form a DAG, not a tree. Rewriting each path repeats complete definitions
        // exponentially. This memo contains expression rewrites only, never query plans or rows.
        // Weak visitor keys do not retain a compiler or a parameter-processing attempt. EF creates
        // new nullability/parameter visitors per execution; reuse is qualified in the patch matrix.
        var visited = visitor is IMySqlMaterializedSourceVisitor sourceVisitor
            ? sourceVisitor.VisitMaterializedSource(Source)
            : (SelectExpression)visitor.Visit(Source);

        return ReferenceEquals(visited, Source) ? this : new MySqlMaterializedSourceDefinition(visited);
    }
}

/// <summary>Allows an EF visitor to preserve the schema of a statement-owned definition.</summary>
internal interface IMySqlMaterializedSourceVisitor
{
    /// <summary>Visits the complete schema without pruning values needed by other references.</summary>
    SelectExpression VisitMaterializedSource(
        SelectExpression source
    );
}
