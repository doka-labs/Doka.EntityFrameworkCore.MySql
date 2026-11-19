namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Compiles only the provider's explicit insert terminal; ordinary queries retain EF's compiler.</summary>
internal sealed class MySqlShapedQueryCompilingExpressionVisitor : RelationalShapedQueryCompilingExpressionVisitor
{
    private static readonly MethodInfo s_executeAsyncMethod = typeof(MySqlInsertSelectExecutor).GetMethod(
        nameof(MySqlInsertSelectExecutor.ExecuteAsync))!;

    private static readonly PropertyInfo s_cancellationTokenProperty =
        typeof(QueryContext).GetProperty(nameof(QueryContext.CancellationToken))!;

    private static readonly PropertyInfo s_parametersProperty =
        typeof(QueryContext).GetProperty(nameof(QueryContext.Parameters))!;

    private static readonly PropertyInfo s_parameterIndexer = typeof(Dictionary<string, object?>).GetProperty("Item")!;

    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlShapedQueryCompilingExpressionVisitor(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
        QueryCompilationContext queryCompilationContext
    ) : base(dependencies, relationalDependencies, queryCompilationContext) { }

    /// <inheritdoc />
    protected override Expression VisitExtension(
        Expression extensionExpression
    )
    {
        if (extensionExpression is not MySqlInsertSelectExpression insert)
        {
            return base.VisitExtension(extensionExpression);
        }

        if (QueryCompilationContext.IsPrecompiling)
        {
            throw new InvalidOperationException(
                "INSERT SELECT pregenerated query execution has not been qualified; use regular runtime EF query compilation.");
        }

        var options = RelationalOptionsExtension.Extract(QueryCompilationContext.ContextOptions);
        Expression cancellationToken = insert.CancellationTokenParameter is { } tokenName
            ? Expression.Convert(Expression.MakeIndex(
                    Expression.Property(QueryCompilationContext.QueryContextParameter, s_parametersProperty),
                    s_parameterIndexer,
                    [Expression.Constant(tokenName)]),
                typeof(CancellationToken))
            : Expression.Property(QueryCompilationContext.QueryContextParameter, s_cancellationTokenProperty);

        // WHY: EF10's resolver-expression builder is private. Reuse its command cache
        // through this narrow adapter, not reflection or a parallel query/SQL cache. The
        // ordinary compiler path above remains untouched; no context is retained in this cache.
#pragma warning disable EF1001 // Narrow EF10 command-resolution seam, covered by direct-write compilation tests.
        var commandCache = new RelationalCommandCache(
            Dependencies.MemoryCache,
            RelationalDependencies.QuerySqlGeneratorFactory,
            RelationalDependencies.RelationalParameterBasedSqlProcessorFactory,
            insert.ApplyTags(QueryCompilationContext.Tags),
            options.UseRelationalNulls,
            options.ParameterizedCollectionMode);
#pragma warning restore EF1001

        return Expression.Call(
            s_executeAsyncMethod,
            Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(RelationalQueryContext)),
            Expression.Constant(commandCache),
            Expression.Constant(QueryCompilationContext.ContextType),
            Expression.Constant(Dependencies.CoreSingletonOptions.AreThreadSafetyChecksEnabled),
            cancellationToken);
    }
}
