namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Prunes a shared source only after all of its consuming references have contributed their column demands.</summary>
internal static partial class MySqlCteColumnPruner
{
    /// <summary>Reuses EF's pruning semantics while processing canonical definitions in consumer-before-dependency order.</summary>
    public static Expression Prune(
        Expression expression
    )
    {
        var graph = new DefinitionGraph();
        graph.Visit(expression);
        var root = new DemandPruner(graph, owner: null).Prune(expression);

        // WHY: Every nonrecursive consumer contributes before its dependencies are processed.
        // Shared definitions are not pruned independently for each alias or repeated dependency path.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = graph.DependencyOrder.Count - 1; index >= 0; index--)
        {
            var definition = graph.DependencyOrder[index];

            if (!definition.Used)
            {
                continue;
            }

            int before;

            do
            {
                // WHY: A recursive join or predicate can require an otherwise unreturned field.
                // Its finite self-demand fixpoint adds only original visible columns, never rows.
                definition.KeepRecursivePlaceholder();
                before = definition.Columns.Count;
                definition.PrunedSource = new DemandPruner(graph, definition).PruneDefinition(definition);
            } while (definition.Columns.Count != before);
        }

        return new DefinitionRewriter(graph).Visit(root);
    }

    /// <summary>Rebuilds each canonical definition once after every demand has been resolved.</summary>
    private sealed class DefinitionRewriter : MySqlShapedQueryTraversingExpressionVisitor
    {
        private readonly DefinitionGraph _graph;

        private readonly Dictionary<MySqlMaterializedSourceDefinition, MySqlMaterializedSourceDefinition> _rewritten =
            [];

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public DefinitionRewriter(
            DefinitionGraph graph
        )
        {
            _graph = graph;
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is not MySqlMaterializedSourceExpression reference)
            {
                return base.VisitExtension(node);
            }

            if (!_rewritten.TryGetValue(reference.Definition, out var definition))
            {
                var original = _graph.Get(reference);
                var source = (SelectExpression)Visit(original.PrunedSource ?? reference.Source);
                definition = ReferenceEquals(source, reference.Source)
                    ? reference.Definition
                    : new MySqlMaterializedSourceDefinition(source, reference.Kind);

                _rewritten.Add(reference.Definition, definition);
            }

            return reference.UpdateDefinition(definition);
        }
    }
}
