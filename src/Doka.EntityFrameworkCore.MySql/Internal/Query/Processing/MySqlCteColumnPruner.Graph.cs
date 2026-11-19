namespace Doka.EntityFrameworkCore.MySql;

internal static partial class MySqlCteColumnPruner
{
    /// <summary>Inventories definition identity and dependency order without expanding shared paths repeatedly.</summary>
    private sealed class DefinitionGraph : MySqlShapedQueryTraversingExpressionVisitor
    {
        private readonly HashSet<MySqlMaterializedSourceDefinition> _active = [];
        private readonly Dictionary<MySqlMaterializedSourceDefinition, DefinitionNode> _definitions = [];

        /// <summary>Gets canonical definitions in dependency-before-consumer order.</summary>
        public List<DefinitionNode> DependencyOrder { get; } = [];

        /// <summary>Gets the canonical node for an actual reference, rejecting a changed binding identity.</summary>
        public DefinitionNode Get(
            MySqlMaterializedSourceExpression reference
        )
        {
            if (!_definitions.TryGetValue(reference.Definition, out var definition)
                || definition.BindingId != reference.BindingId)
            {
                throw new InvalidOperationException("A CTE reference has inconsistent canonical definition identity.");
            }

            return definition;
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

            if (_active.Contains(reference.Definition))
            {
                throw new InvalidOperationException("CTE definitions require an acyclic dependency graph.");
            }

            if (!_definitions.ContainsKey(reference.Definition))
            {
                var definition = new DefinitionNode(reference);
                _definitions.Add(reference.Definition, definition);
                _active.Add(reference.Definition);
                Visit(reference.Source);
                _active.Remove(reference.Definition);
                DependencyOrder.Add(definition);
            }
            else
            {
                _ = Get(reference);
            }

            return node;
        }
    }

    /// <summary>Owns one original schema, its global demand union and its final pruned relation.</summary>
    private sealed class DefinitionNode
    {
        private readonly HashSet<string> _schema;

        /// <summary>Initializes a node from the canonical relation, not its CLR row type or SQL text.</summary>
        public DefinitionNode(
            MySqlMaterializedSourceExpression reference
        )
        {
            Definition = reference.Definition;
            BindingId = reference.BindingId;
            _schema = reference
                .Source
                .Projection
                .Select(projection => projection.Alias)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Gets the canonical immutable definition.</summary>
        public MySqlMaterializedSourceDefinition Definition { get; }

        /// <summary>Gets the lexical identity used only by this definition's finite recursive frontier.</summary>
        public int BindingId { get; }

        /// <summary>Gets all demanded visible column aliases across references and recursive dependencies.</summary>
        public HashSet<string> Columns { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets whether some consumed table occurrence requires this relation's bag of rows.</summary>
        public bool Used { get; private set; }

        /// <summary>Gets or sets the final source after its consumers' demands have been accumulated.</summary>
        public SelectExpression? PrunedSource { get; set; }

        /// <summary>Includes a consumed occurrence and its actual named columns.</summary>
        public void Include(
            HashSet<string>? columns
        )
        {
            Used = true;

            if (columns is null)
            {
                return;
            }

            foreach (var column in columns)
            {
                if (!_schema.Contains(column))
                {
                    throw new InvalidOperationException("A CTE column reference is absent from its canonical schema.");
                }

                Columns.Add(column);
            }
        }

        /// <summary>Retains one visible ordinal when only recursive row multiplicity is consumed.</summary>
        public void KeepRecursivePlaceholder()
        {
            if (Definition.Kind == MySqlSourceBindingKind.Recursive
                && Columns.Count == 0
                && Definition.Source.Projection.Count != 0)
            {
                // WHY: Both recursive arms and the declared header must keep one aligned visible
                // ordinal in addition to the hidden depth, even for an outer COUNT(*).
                Columns.Add(Definition.Source.Projection[0].Alias);
            }
        }
    }
}
