namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQueryableMethodTranslatingExpressionVisitor
{
    /// <summary>Translates one complete source once and returns a composable reference clone.</summary>
    private ShapedQueryExpression TranslateMaterializedSource(
        IMySqlMaterializedQueryRoot reference
    )
    {
        var compilation = (MySqlQueryCompilationContext)_queryCompilationContext;
        var bindingId = reference.BindingId;

        if (reference.IsRecursiveFrontier)
        {
            return compilation.RecursiveCteFrontiers.TryGetValue(bindingId, out var frontier)
                ? CloneSourceShape(frontier)
                : throw new InvalidOperationException("A recursive CTE frontier escaped its owning recursive step.");
        }

        if (!compilation.MaterializedSources.TryGetValue(bindingId, out var bound))
        {
            var query = compilation.MaterializedSourceQueries[bindingId];
            var source = query is MySqlRecursiveSourceQueryExpression recursive
                ? TranslateRecursiveCteSource(recursive)
                : TranslateIndependentSource(query);

            bound = BindCompletedSource(source, bindingId, reference.Kind);
            compilation.MaterializedSources.Add(bindingId, bound);
            compilation.HasPrunableSources |=
                reference.Kind is MySqlSourceBindingKind.Ordinary or MySqlSourceBindingKind.Recursive;
        }

        return CloneSourceShape(bound);
    }

    /// <summary>Translates one independently scoped flat relation without evaluating client values.</summary>
    private ShapedQueryExpression TranslateIndependentSource(
        Expression query
    )
    {
        try
        {
            return Visit(query) as ShapedQueryExpression
                ?? throw new InvalidOperationException("The statement-local source cannot be translated to SQL.");
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                "The materialized or CTE source cannot be translated to independent SQL.",
                error);
        }
    }

    /// <summary>Reuses EF's completed projection and identifiers for a relation or finite recursive frontier.</summary>
    private ShapedQueryExpression BindCompletedSource(
        ShapedQueryExpression source,
        int bindingId,
        MySqlSourceBindingKind kind,
        SqlExpression? frontierMaximumDepth = null
    )
    {
        source = NormalizeSourceShaper(source);
        SourceScopeValidator.Validate(source.QueryExpression);
        var select = (SelectExpression)source.QueryExpression;
        select.PushdownIntoSubquery();
        var definition = (SelectExpression)select.Tables.Single();
        var referenceAlias = definition.Alias!;

        // WHY: The definition and its reference occupy different alias scopes. Sharing their
        // alias lets EF's cloning visitor overwrite the reference's column remapping.
        definition = definition.WithAlias(_queryCompilationContext.SqlAliasManager.GenerateTableAlias("seed"));

        TableExpressionBase table = frontierMaximumDepth is not null
            ? new MySqlRecursiveCteReferenceExpression(referenceAlias, bindingId, frontierMaximumDepth)
            : new MySqlMaterializedSourceExpression(referenceAlias,
                bindingId,
                new MySqlMaterializedSourceDefinition(definition, kind),
                annotations: null);

        // WHY: Pushdown retains EF's structural projections, discriminator, native mappings and
        // correlation identifiers. Reconstructing a key-only select would lose bag occurrences.
#pragma warning disable EF1001 // EF's mutable projection/identifier-preserving table replacement seam; see D-024.
        select.SetTables([table]);
#pragma warning restore EF1001
        select.ClearOrdering();

        if (frontierMaximumDepth is not null)
        {
            // WHY: Later recursive iterations may produce NULL even when every anchor value is
            // nonnull. Keeping anchor-only facts folds a valid frontier IS NULL test to false.
            // Visit the mutable reference projection/identifiers, without changing its anchor,
            // CLR mapping, entity-shaper identity or the ordinary source's nullability contract.
            _ = new MySqlRecursiveFrontierNullabilityExpressionVisitor(referenceAlias).Visit(select);
        }

        return source;
    }

    /// <summary>Restores member projections required for subsequent server-side composition.</summary>
    private static ShapedQueryExpression NormalizeSourceShaper(
        ShapedQueryExpression source
    )
    {
        SourceProjectionValidator.Validate(source);
        source = source.UpdateShaperExpression(new SourceReferenceIncludeUnwrapper().Visit(source.ShaperExpression));
        var select = (SelectExpression)source.QueryExpression;

        if (source.ShaperExpression is StructuralTypeShaperExpression
            {
                ValueBufferExpression: ProjectionBindingExpression { Index: not null } binding,
            } entityShaper
            && select.GetProjection(binding) is StructuralTypeProjectionExpression projection)
        {
            // WHY: A completed owned Include may already use client projection indexes. A new
            // bound root needs member projections so EF finalizes its later scalar projections.
            var member = new ProjectionMember();
            select.ReplaceProjection(Array.Empty<Expression>());
            select.ReplaceProjection(new Dictionary<ProjectionMember, Expression> { [member] = projection });
            source = source.UpdateShaperExpression(
                entityShaper.Update(new ProjectionBindingExpression(select, member, typeof(ValueBuffer))));
        }

        return source;
    }

    /// <summary>Copies a canonical source without sharing mutable relational projection or alias state.</summary>
    private static ShapedQueryExpression CloneSourceShape(
        ShapedQueryExpression bound
    )
    {
        var original = (SelectExpression)bound.QueryExpression;
#pragma warning disable EF1001 // EF cloning preserves identifiers and alias remapping across references; see D-024.
        var clone = original.Clone();
#pragma warning restore EF1001
        var shaper = new SourceShaperRebindingVisitor(original, clone).Visit(bound.ShaperExpression);

        return new ShapedQueryExpression(clone, shaper);
    }

    /// <summary>Leaves reference navigation loading to the continuation's ordinary entity expansion.</summary>
    private sealed class SourceReferenceIncludeUnwrapper : ExpressionVisitor
    {
        protected override Expression VisitExtension(
            Expression node
        )
        {
            // WHY: A completed source is translated before the continuation. Its reference Includes
            // are already represented by navigation projections, but an Include shaper cannot be
            // remapped as the entity parameter of a later OrderBy or Select. The bound entity root
            // expands the continuation's reference Includes against those captured projections.
            return node is IncludeExpression { Navigation: INavigation { ForeignKey.IsOwnership: true } } include
                ? Visit(include.EntityExpression)
                : base.VisitExtension(node);
        }
    }

    /// <summary>
    /// Rejects row-correlated inputs that cannot be hoisted into a statement-level definition.
    /// </summary>
    private sealed class SourceScopeValidator : ExpressionVisitor
    {
        private readonly HashSet<string> _declared = new(StringComparer.Ordinal);
        private readonly HashSet<string> _referenced = new(StringComparer.Ordinal);

        public static void Validate(
            Expression expression
        )
        {
            var visitor = new SourceScopeValidator();
            visitor.Visit(expression);

            // WHY: A lexical binding can depend on another complete bound source, but a CTE cannot
            // capture an enclosing query row. Rejecting free aliases avoids invalid or partial SQL.
            if (!visitor._referenced.IsSubsetOf(visitor._declared))
            {
                throw new InvalidOperationException(
                    "A materialized source cannot capture a row from an enclosing query.");
            }
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is TableExpressionBase { Alias: { } alias })
            {
                _declared.Add(alias);
            }

            if (node is MySqlMaterializedSourceExpression)
            {
                // Dependencies were independently validated when translated. Their internal aliases
                // belong to their own definition, not this reference's lexical row scope.
                return node;
            }

            if (node is ColumnExpression column)
            {
                _referenced.Add(column.TableAlias);
            }

            return base.VisitExtension(node);
        }
    }

    /// <summary>
    /// Rebinds projection leaves, which deliberately do not visit their query-expression owner.
    /// </summary>
    private sealed class SourceShaperRebindingVisitor : ExpressionVisitor
    {
        private readonly SelectExpression _clone;
        private readonly SelectExpression _original;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public SourceShaperRebindingVisitor(
            SelectExpression original,
            SelectExpression clone
        )
        {
            _original = original;
            _clone = clone;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ProjectionBindingExpression binding
                && ReferenceEquals(binding.QueryExpression, _original))
            {
                // WHY: ReplacingExpressionVisitor treats structural shapers and projection leaves
                // as opaque. Retaining their old owner would compare both references as one row.
                return binding.ProjectionMember is { } member
                    ? new ProjectionBindingExpression(_clone, member, binding.Type)
                    : new ProjectionBindingExpression(_clone, binding.Index!.Value, binding.Type);
            }

            return base.VisitExtension(node);
        }
    }

    /// <summary>
    /// Rejects deferred client work that cannot be frozen inside the SQL source.
    /// </summary>
    private sealed class SourceProjectionValidator : ExpressionVisitor
    {
        private readonly SelectExpression _select;

        private SourceProjectionValidator(
            SelectExpression select
        )
        {
            _select = select;
        }

        public static void Validate(
            ShapedQueryExpression source
        ) => new SourceProjectionValidator((SelectExpression)source.QueryExpression).Visit(source.ShaperExpression);

        protected override Expression VisitMethodCall(
            MethodCallExpression node
        ) => throw new InvalidOperationException(
            "A materialized source must be a flat SQL projection; deferred client methods cannot be bound.");

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ProjectionBindingExpression binding)
            {
                if (_select.GetProjection(binding) is ShapedQueryExpression)
                {
                    throw new InvalidOperationException(
                        "A materialized source cannot contain a deferred collection projection.");
                }

                return node;
            }

            if (node is IncludeExpression { Navigation: INavigation { ForeignKey.IsOwnership: false } })
            {
                // WHY: Related entities are not flat source values. Removing this Include would
                // silently drop requested data; reloading it later would break the source fence.
                throw new InvalidOperationException(
                    "A materialized source cannot contain a non-owned reference Include. "
                    + "Bind a flat source and include the reference on the composed output entity.");
            }

            if (node is ShapedQueryExpression
                or CollectionResultExpression
                or IncludeExpression { Navigation.IsCollection: true })
            {
                throw new InvalidOperationException(
                    "A materialized source cannot contain a deferred collection projection.");
            }

            return base.VisitExtension(node);
        }
    }
}
