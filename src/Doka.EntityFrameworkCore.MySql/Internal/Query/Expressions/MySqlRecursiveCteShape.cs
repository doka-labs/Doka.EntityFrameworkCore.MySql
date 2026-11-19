namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Qualifies the finite UNION ALL tree and owns the collision-free hidden depth column.</summary>
internal sealed class MySqlRecursiveCteShape
{
    private MySqlRecursiveCteShape(
        SelectExpression anchor,
        SelectExpression step,
        MySqlRecursiveCteReferenceExpression frontier,
        string depthColumnName
    )
    {
        Anchor = anchor;
        Step = step;
        Frontier = frontier;
        DepthColumnName = depthColumnName;
    }

    /// <summary>Gets the completed depth-zero arm.</summary>
    public SelectExpression Anchor { get; }

    /// <summary>Gets the qualified direct previous-iteration arm.</summary>
    public SelectExpression Step { get; }

    /// <summary>Gets the unique finite frontier leaf and actual maximum-depth parameter identity.</summary>
    public MySqlRecursiveCteReferenceExpression Frontier { get; }

    /// <summary>Gets the hidden column name after excluding every visible projection alias.</summary>
    public string DepthColumnName { get; }

    /// <summary>Validates the completed SQL shape before emitting a native recursive definition.</summary>
    public static MySqlRecursiveCteShape Create(
        MySqlMaterializedSourceExpression reference
    )
    {
        if (reference.Kind != MySqlSourceBindingKind.Recursive
            || reference.Source is not
            {
                Tables: [UnionExpression { IsDistinct: false } union],
                Predicate: null,
                GroupBy.Count: 0,
                Having: null,
                IsDistinct: false,
                Limit: null,
                Offset: null,
            })
        {
            throw new InvalidOperationException("A recursive CTE requires exactly one anchor/step UNION ALL shape.");
        }

        var anchor = union.Source1;
        var step = union.Source2;

        if (anchor.Alias is not null
            || step.Alias is not null
            || step.IsDistinct
            || step.GroupBy.Count != 0
            || step.Having is not null
            || step.Limit is not null
            || step.Offset is not null
            || step.Orderings.Count != 0)
        {
            throw new InvalidOperationException("A recursive CTE step must be a direct unordered, ungrouped SQL arm.");
        }

        MySqlRecursiveCteReferenceExpression? frontier = null;

        foreach (var source in step.Tables)
        {
            var table = source;

            while (table is InnerJoinExpression or CrossJoinExpression)
            {
                table = ((JoinExpressionBase)table).Table;
            }

            if (table is MySqlRecursiveCteReferenceExpression candidate
                && candidate.BindingId == reference.BindingId)
            {
                if (frontier is not null)
                {
                    throw new InvalidOperationException(
                        "A recursive CTE step cannot contain multiple frontier tables.");
                }

                frontier = candidate;
            }
            else if (table is not TableExpression and not MySqlMaterializedSourceExpression and not SelectExpression)
            {
                throw new InvalidOperationException(
                    "Recursive CTE steps do not support derived or outer-joined frontier scopes.");
            }
        }

        if (frontier is null)
        {
            throw new InvalidOperationException("A recursive CTE step lost its direct frontier reference.");
        }

        new FrontierScopeValidator(reference.BindingId, frontier, step).Visit(step);
        ValidateColumns(anchor, step, frontier, reference.Source.Projection.Count);
        var names = new HashSet<string>(reference.Source.Projection.Select(projection => projection.Alias),
            StringComparer.OrdinalIgnoreCase);

        var depthName = "__doka_depth";
        var suffix = 0;

        while (names.Contains(depthName))
        {
            depthName = "__doka_depth_" + (++suffix).ToString(CultureInfo.InvariantCulture);
        }

        return new MySqlRecursiveCteShape(anchor, step, frontier, depthName);
    }

    /// <summary>Rejects implicit store-type or converter changes before the engine can truncate a recursive value.</summary>
    private static void ValidateColumns(
        SelectExpression anchor,
        SelectExpression step,
        MySqlRecursiveCteReferenceExpression frontier,
        int visibleColumnCount
    )
    {
        if (anchor.Projection.Count != step.Projection.Count
            || anchor.Projection.Count != visibleColumnCount)
        {
            throw new InvalidOperationException("Recursive CTE arms must have the same visible column schema.");
        }

        // WHY: These actual read-only projection lists are traversed once during SQL qualification,
        // retaining exact paired ordinals without allocating an interface enumerator or a zipped list.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < anchor.Projection.Count; index++)
        {
            var left = anchor.Projection[index].Expression.TypeMapping;
            var right = step.Projection[index].Expression.TypeMapping;

            if (left is null
                || right is null
                || !string.Equals(left.StoreType, right.StoreType, StringComparison.OrdinalIgnoreCase)
                || left.ClrType != right.ClrType
                || left.DbType != right.DbType
                || left.Size != right.Size
                || left.Precision != right.Precision
                || left.Scale != right.Scale
                || left.IsUnicode != right.IsUnicode
                || left.IsFixedLength != right.IsFixedLength
                || !ReferenceEquals(left.Converter, right.Converter))
            {
                throw new InvalidOperationException(
                    "Recursive CTE arms require compatible physical types and converters.");
            }

            if (MySqlRuntimeColumnTextFacets.IsText(left))
            {
                var source = anchor.Projection[index];
                var value = step.Projection[index].Expression;
                var preservesFrontier = value is ColumnExpression column
                    && column.TableAlias == frontier.Alias
                    && column.Name == source.Alias;

                // WHY: Equal EF mappings do not establish a native literal/function anchor width.
                // Preserve unchanged frontier text or equally bounded mapped columns; transformations
                // need a separately qualified anchor cast before permissive MySQL can be allowed.
                var anchorColumn = FindPhysicalTextColumn(anchor, source.Expression);
                var stepColumn = FindPhysicalTextColumn(step, value);
                var compatiblePhysicalCopy = anchorColumn is not null
                    && stepColumn is not null
                    && MySqlRuntimeColumnTextFacets.AreCompatible(anchorColumn, stepColumn);

                if (!preservesFrontier
                    && value is not SqlConstantExpression { Value: null }
                    && !compatiblePhysicalCopy)
                {
                    // WHY: A physical LONGTEXT declaration still does not bound CONCAT bytes below
                    // max_allowed_packet. Only proved bounded growth receives the one-statement
                    // capacity guard; unbounded transformations must not silently return native NULL.
                    if (!MySqlRecursiveTextCapacity.IsProvedCastGrowth(anchor,
                        source.Expression,
                        value,
                        frontier,
                        source.Alias))
                    {
                        throw new InvalidOperationException(
                            "Recursive CTE text transformations require a qualified native anchor capacity.");
                    }
                }
            }
        }
    }

    /// <summary>Follows completed column lineage rather than trusting the CLR string fallback's nominal store type.</summary>
    private static IColumn? FindPhysicalTextColumn(
        SelectExpression scope,
        SqlExpression expression
    )
    {
        if (expression is not ColumnExpression column)
        {
            return null;
        }

        foreach (var source in scope.Tables)
        {
            var table = source;

            while (table is JoinExpressionBase join)
            {
                table = join.Table;
            }

            if (table.Alias != column.TableAlias)
            {
                continue;
            }

            if (table is TableExpression physical)
            {
                return physical.Table.FindColumn(column.Name) as IColumn;
            }

            var nested = table switch
            {
                SelectExpression select => select,
                MySqlMaterializedSourceExpression reference => reference.Source,
                _ => null,
            };

            if (nested is null)
            {
                return null;
            }

            var projection = nested.Projection.FirstOrDefault(candidate => candidate.Alias == column.Name);

            return projection is null
                ? null
                : FindPhysicalTextColumn(nested, projection.Expression);
        }

        return null;
    }

    /// <summary>Rejects frontier occurrences in scalar subqueries rather than assuming alias text implies a direct scope.</summary>
    private sealed class FrontierScopeValidator : ExpressionVisitor
    {
        private readonly int _bindingId;
        private readonly MySqlRecursiveCteReferenceExpression _expected;
        private readonly SelectExpression _root;

        private bool _insideNestedSelect;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public FrontierScopeValidator(
            int bindingId,
            MySqlRecursiveCteReferenceExpression expected,
            SelectExpression root
        )
        {
            _bindingId = bindingId;
            _expected = expected;
            _root = root;
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is MySqlMaterializedSourceExpression)
            {
                return node;
            }

            if (node is MySqlWindowExpression)
            {
                throw new InvalidOperationException("A recursive CTE step cannot contain window calculations.");
            }

            if (node is MySqlRecursiveCteReferenceExpression reference
                && reference.BindingId == _bindingId)
            {
                if (_insideNestedSelect || !ReferenceEquals(reference, _expected))
                {
                    throw new InvalidOperationException(
                        "The recursive frontier must occur once in the direct step scope.");
                }

                return node;
            }

            var previous = _insideNestedSelect;

            if (node is SelectExpression select)
            {
                _insideNestedSelect = previous || !ReferenceEquals(select, _root);
            }

            try
            {
                return base.VisitExtension(node);
            }
            finally
            {
                _insideNestedSelect = previous;
            }
        }
    }
}
