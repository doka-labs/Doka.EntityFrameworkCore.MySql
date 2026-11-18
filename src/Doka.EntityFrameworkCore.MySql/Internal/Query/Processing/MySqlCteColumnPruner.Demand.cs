namespace Doka.EntityFrameworkCore.MySql;

internal static partial class MySqlCteColumnPruner
{
    /// <summary>Uses EF's projection, DISTINCT, set and join rules while stopping at shared definition boundaries.</summary>
    private sealed class DemandPruner : SqlTreePruner
    {
        // WHY: EF's protected pruning contract keys aliases by reference identity. Each pass owns
        // its map; canonical definition identity unions separate reference occurrences afterward.
        private readonly Dictionary<string, HashSet<string>> _columns = new(ReferenceEqualityComparer.Instance);
        private readonly DefinitionGraph _graph;
        private readonly DefinitionNode? _owner;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public DemandPruner(
            DefinitionGraph graph,
            DefinitionNode? owner
        )
        {
            _graph = graph;
            _owner = owner;
        }

        /// <inheritdoc />
        protected override IReadOnlyDictionary<string, HashSet<string>> ReferencedColumnMap => _columns;

        /// <summary>Seeds one complete definition with its global demand union before standard EF pruning.</summary>
        public SelectExpression PruneDefinition(
            DefinitionNode definition
        )
        {
            var source = definition.Definition.Source;
            var alias = source.Alias
                ?? throw new InvalidOperationException("A CTE definition requires its own projection alias scope.");

            _columns.Add(alias, new HashSet<string>(definition.Columns, StringComparer.Ordinal));

#pragma warning disable EF1001 // Exact EF11 protected projection-pruning seam; qualified on the supported EF patch matrix.
            return PruneSelect(source,
                preserveProjection: definition.Definition.Kind == MySqlSourceBindingKind.Materialized);
#pragma warning restore EF1001
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is MySqlInsertSelectExpression insert)
            {
                // WHY: Target assignments consume every source ordinal; they are not ordinary
                // outer column references which may be absent from this demand map.
#pragma warning disable EF1001 // Exact EF11 top-level projection-pruning seam, qualified by the EF patch matrix.
                return insert.Update(PruneSelect(insert.Source, preserveProjection: true));
#pragma warning restore EF1001
            }

            if (node is ColumnExpression column)
            {
                if (!_columns.TryGetValue(column.TableAlias, out var names))
                {
                    names = new HashSet<string>(StringComparer.Ordinal);
                    _columns.Add(column.TableAlias, names);
                }

                names.Add(column.Name);

                return node;
            }

            if (node is MySqlMaterializedSourceExpression reference)
            {
                _graph
                    .Get(reference)
                    .Include(_columns.GetValueOrDefault(reference.Alias!));

                return reference;
            }

            if (node is MySqlRecursiveCteReferenceExpression frontier)
            {
                if (_owner is null
                    || _owner.BindingId != frontier.BindingId
                    || _owner.Definition.Kind != MySqlSourceBindingKind.Recursive)
                {
                    throw new InvalidOperationException("A recursive frontier escaped its owning pruning scope.");
                }

                _owner.Include(_columns.GetValueOrDefault(frontier.Alias!));

                return frontier;
            }

            return base.VisitExtension(node);
        }
    }
}
