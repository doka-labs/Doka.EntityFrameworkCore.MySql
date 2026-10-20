using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Processes each complete source once while preserving EF's ordinary navigation and query semantics.
/// </summary>
internal sealed class MySqlQueryTranslationPreprocessor(
    QueryTranslationPreprocessorDependencies dependencies,
    RelationalQueryTranslationPreprocessorDependencies relationalDependencies,
    QueryCompilationContext queryCompilationContext
) : RelationalQueryTranslationPreprocessor(dependencies, relationalDependencies, queryCompilationContext)
{
    /// <inheritdoc />
    public override Expression Process(
        Expression query
    )
    {
        var operators = new MySqlStraightJoinLowering();
        query = operators.Visit(query);

        if (!operators.ContainsMaterializedSource)
        {
            return base.Process(query);
        }

        // Collect metadata globally in the same outer-to-inner order as ordinary EF before
        // extracting sources; per-source normalization must not change tracking/filter precedence.
        query = base.NormalizeQueryableMethod(query);
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
                compilation.MaterializedSourceQueries.Add(bindingId, ProcessCore(source, navigationVisitor));
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException(
                    "The materialized source cannot be processed as an independent SQL source.",
                    error);
            }
        }

        return definitions.Count == 0 ? base.Process(query) : ProcessCore(query, navigationVisitor);
    }

    /// <summary>
    /// Uses EF's preprocessing sequence with the one mapped-root filter seam required by completed sources.
    /// </summary>
    private Expression ProcessCore(
        Expression query,
        BoundNavigationExpandingExpressionVisitor navigationVisitor
    )
    {
        // WHY: EF constructs its navigation visitor inside Process without a factory hook. This
        // sequence mirrors EF 10 so bound entity roots can retain navigation metadata without
        // reapplying a global filter to occurrences already selected by their completed source.
#pragma warning disable EF1001 // Narrow provider preprocessing seam, qualified on the EF patch matrix; see D-024.
        query = new InvocationExpressionRemovingExpressionVisitor().Visit(query);
        query = base.NormalizeQueryableMethod(query);
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
