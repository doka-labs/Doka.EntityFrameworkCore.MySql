namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Processes each complete source once while preserving EF's ordinary navigation and query semantics.
/// </summary>
internal sealed class MySqlQueryTranslationPreprocessor : RelationalQueryTranslationPreprocessor
{
    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlQueryTranslationPreprocessor(
        QueryTranslationPreprocessorDependencies dependencies,
        RelationalQueryTranslationPreprocessorDependencies relationalDependencies,
        QueryCompilationContext queryCompilationContext
    ) : base(dependencies, relationalDependencies, queryCompilationContext) { }

    /// <inheritdoc />
    public override Expression NormalizeQueryableMethod(
        Expression expression
    )
    {
        var normalized =
            MySqlIndexedSelectLowering.Lower(base.NormalizeQueryableMethod(expression), out var containsWindows);

        ((MySqlQueryCompilationContext)QueryCompilationContext).UsesWindowFunctions |= containsWindows;

        return normalized;
    }

    /// <inheritdoc />
    public override Expression Process(
        Expression query
    )
    {
        if (query is MySqlInsertSelectQueryExpression insert)
        {
            return insert.Update(Process(insert.Source));
        }

        query = MySqlLockingReadLowering.Process(query, (MySqlQueryCompilationContext)QueryCompilationContext);
        var operators = new MySqlStraightJoinLowering();
        query = operators.Visit(query);

        if (!operators.ContainsMaterializedSource)
        {
            return base.Process(query);
        }

        // Collect metadata globally in the same outer-to-inner order as ordinary EF before
        // extracting sources; per-source normalization must not change tracking/filter precedence.
        query = NormalizeQueryableMethod(query);
        var compilation = (MySqlQueryCompilationContext)QueryCompilationContext;
        var definitions = new Dictionary<int, Expression>();
        query = new MySqlMaterializedSourceBinding(compilation.Model, definitions).Visit(query);

#pragma warning disable EF1001 // One navigation visitor preserves EF's statement-wide filter parameter namespace; see D-024.
        var navigationVisitor = new BoundNavigationExpandingExpressionVisitor(
            this,
            compilation,
            Dependencies.EvaluatableExpressionFilter,
            Dependencies.NavigationExpansionExtensibilityHelper);
#pragma warning restore EF1001

        foreach (var (bindingId, source) in definitions)
        {
            try
            {
                // WHY: A recursive step contains a typed frontier leaf, not the anchor's source tree.
                // Expand each finite arm once with the same filter-parameter namespace before translation.
                var processedSource = source is MySqlRecursiveSourceQueryExpression recursive
                    ? recursive.Update(
                        ProcessCore(recursive.Anchor, navigationVisitor),
                        ProcessCore(recursive.RecursiveStep, navigationVisitor),
                        recursive.MaximumDepth)
                    : ProcessCore(source, navigationVisitor);

                compilation.MaterializedSourceQueries.Add(bindingId, processedSource);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException(
                    "The materialized source cannot be processed as an independent SQL source.",
                    error);
            }
        }

        return definitions.Count == 0
            ? base.Process(query)
            : ProcessCore(query, navigationVisitor);
    }

    /// <summary>
    /// Uses EF's preprocessing sequence with the one mapped-root filter seam required by completed sources.
    /// </summary>
    private Expression ProcessCore(
        Expression query,
        BoundNavigationExpandingExpressionVisitor navigationVisitor
    )
    {
        // WHY: EF Core 11 creates its navigation visitor inside Process without a factory hook.
        // Preserve its visitor order and inherited relational metadata/root normalization,
        // but reuse one bound-root visitor so completed sources keep navigation metadata
        // without applying their root filters twice.
#pragma warning disable EF1001 // Narrow provider preprocessing seam, qualified on the EF patch matrix; see D-024.
        query = new InvocationExpressionRemovingExpressionVisitor().Visit(query);
        query = NormalizeQueryableMethod(query);
        query = new CallForwardingExpressionVisitor().Visit(query);
        query = new NullCheckRemovingExpressionVisitor().Visit(query);
        query = new SubqueryMemberPushdownExpressionVisitor(QueryCompilationContext.Model).Visit(query);
        query = navigationVisitor.Expand(query);
        query = new QueryOptimizingExpressionVisitor().Visit(query);
        query = new NullCheckRemovingExpressionVisitor().Visit(query);
#pragma warning restore EF1001

        return query;
    }

#pragma warning disable EF1001 // The public provider visitor seam is used only to preserve completed-source filter semantics.
    private sealed class BoundNavigationExpandingExpressionVisitor : NavigationExpandingExpressionVisitor
    {
        private readonly MySqlQueryCompilationContext _compilation;

        public BoundNavigationExpandingExpressionVisitor(
            QueryTranslationPreprocessor preprocessor,
            MySqlQueryCompilationContext compilation,
            IEvaluatableExpressionFilter evaluatableExpressionFilter,
            INavigationExpansionExtensibilityHelper extensibilityHelper
        ) : base(preprocessor, compilation, evaluatableExpressionFilter, extensibilityHelper)
        {
            _compilation = compilation;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is not MySqlMaterializedEntityQueryRootExpression)
            {
                return base.VisitExtension(node);
            }

            var previous = _compilation.VisitingMaterializedEntityRoot;
            _compilation.VisitingMaterializedEntityRoot = true;

            try
            {
                return base.VisitExtension(node);
            }
            finally
            {
                _compilation.VisitingMaterializedEntityRoot = previous;
            }
        }
    }
#pragma warning restore EF1001
}
