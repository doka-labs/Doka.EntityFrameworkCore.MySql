namespace Doka.EntityFrameworkCore.MySql;

// WHY: EF funcletization replaces DbSet arguments with IQueryable roots before
// rebuilding method calls. A DbSet-typed terminal therefore needs metadata extraction
// before that rewrite; every ordinary query still uses EF's complete compiler unchanged.
#pragma warning disable EF1001 // Exact ExtractParameters seam; remove when EF supports DbSet terminal arguments.
/// <summary>Preserves the typed INSERT target during compiled-query parameter extraction.</summary>
internal sealed class MySqlQueryCompiler : QueryCompiler
{
    private static readonly MethodInfo s_readTarget = typeof(MySqlQueryCompiler).GetMethod(
        nameof(ReadTarget),
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly ICurrentDbContext _currentContext;
    private readonly IEvaluatableExpressionFilter _evaluatableFilter;
    private readonly IModel _model;

    /// <summary>Uses EF's existing compiler, cache, database, and parameter extraction dependencies.</summary>
    public MySqlQueryCompiler(
        IQueryContextFactory queryContextFactory,
        ICompiledQueryCache compiledQueryCache,
        ICompiledQueryCacheKeyGenerator compiledQueryCacheKeyGenerator,
        IDatabase database,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger,
        ICurrentDbContext currentContext,
        IEvaluatableExpressionFilter evaluatableExpressionFilter,
        IModel model
    ) : base(queryContextFactory,
        compiledQueryCache,
        compiledQueryCacheKeyGenerator,
        database,
        logger,
        currentContext,
        evaluatableExpressionFilter,
        model)
    {
        _currentContext = currentContext;
        _evaluatableFilter = evaluatableExpressionFilter;
        _model = model;
    }

    /// <inheritdoc />
    public override Expression ExtractParameters(
        Expression query,
        Dictionary<string, object?> parameters,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger,
        bool compiledQuery = false,
        bool generateContextAccessors = false
    )
    {
        if (!compiledQuery
            || query is not MethodCallExpression call
            || call.Method.DeclaringType != typeof(MySqlQueryableExtensions)
            || call.Method.Name != nameof(MySqlQueryableExtensions.ExecuteInsertFromQueryAsync))
        {
            return base.ExtractParameters(query, parameters, logger, compiledQuery, generateContextAccessors);
        }

        // WHY: Evaluate the whole scalar receipt, so EF never rebuilds a method with
        // a query root in a DbSet slot. The receipt is invocation-local and discarded
        // before caching; owner identity cannot be inferred from a shared IModel.
        var receiptExpression = Expression.Call(s_readTarget, call.Arguments[1]);
        var extracted = base.ExtractParameters(receiptExpression,
            parameters,
            logger,
            compiledQuery: true,
            generateContextAccessors);

        if (extracted is not ConstantExpression { Value: TargetReceipt receipt }
            || !ReferenceEquals(receipt.Context, _currentContext.Context)
            || receipt.Provider is not EntityQueryProvider)
        {
            throw new InvalidOperationException(
                "A compiled INSERT SELECT requires a target set owned by the current EF context.");
        }

        var target = new EntityQueryRootExpression(receipt.Provider, receipt.EntityType);
        var lowered = MySqlInsertSelectLowering.Lower(call, target, _model, _evaluatableFilter);

        // WHY: Visiting both roots in one normal extraction preserves EF's provider
        // identity check and detaches providers before its existing cache sees them.
        return base.ExtractParameters(lowered,
            parameters,
            logger,
            compiledQuery: true,
            generateContextAccessors);
    }

    /// <summary>Reads only the mapped root and its owner; it never enumerates or constructs an entity.</summary>
    private static TargetReceipt ReadTarget(
        IQueryable target
    )
    {
        if (target.Expression is not EntityQueryRootExpression root
            || target.Provider is not IAsyncQueryProvider provider
            || target is not IInfrastructure<IServiceProvider> infrastructure)
        {
            throw new InvalidOperationException("A compiled INSERT SELECT requires an EF target set.");
        }

        return new TargetReceipt(root.EntityType,
            provider,
            infrastructure.Instance.GetRequiredService<ICurrentDbContext>()
                .Context);
    }

    /// <summary>Holds owner identity only during the current extraction, outside every cache.</summary>
    private sealed record TargetReceipt(
        IEntityType EntityType,
        IAsyncQueryProvider Provider,
        DbContext Context
    );
}
#pragma warning restore EF1001
