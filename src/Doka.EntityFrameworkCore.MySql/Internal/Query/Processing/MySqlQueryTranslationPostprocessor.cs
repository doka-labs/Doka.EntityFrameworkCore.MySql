namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Applies MySQL-family relational rewrites after projection pruning and before
/// EF Core finalizes SQL aliases.
/// </summary>
internal sealed class MySqlQueryTranslationPostprocessor : RelationalQueryTranslationPostprocessor
{
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlQueryTranslationPostprocessor(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext,
        MySqlSingletonOptions singletonOptions
    ) : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _singletonOptions = singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions));
    }

    /// <summary>
    /// Normalizes only unambiguous fallback-versus-model string mappings before EF Core
    /// applies its standard relational type-inference and validation pass.
    /// </summary>
    protected override Expression ProcessTypeMappings(
        Expression expression
    )
    {
        var normalized = MySqlValuesTypeMappingNormalizingExpressionVisitor.Normalize(
            expression,
            RelationalDependencies.TypeMappingSource);

        return base.ProcessTypeMappings(normalized);
    }

    /// <summary>
    /// Rewrites only MariaDB trees. MySQL supports the original LATERAL form and
    /// therefore retains EF Core's relational tree unchanged.
    /// </summary>
    protected override Expression Prune(
        Expression query
    )
    {
        var pruned = base.Prune(query);

        return _singletonOptions.ServerVersion?.IsMariaDb == true
            ? new MySqlMariaDbApplyRewritingExpressionVisitor(RelationalDependencies.SqlExpressionFactory).Visit(pruned)
            : new MySqlLateralProjectionDecorrelationExpressionVisitor().Visit(pruned);
    }
}
