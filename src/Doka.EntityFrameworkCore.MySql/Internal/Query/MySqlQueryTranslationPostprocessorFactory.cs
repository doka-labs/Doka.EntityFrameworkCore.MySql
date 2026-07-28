namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Creates the provider query postprocessor that applies engine-specific relational
/// tree rewrites after EF Core has finalized projections.
/// </summary>
internal sealed class MySqlQueryTranslationPostprocessorFactory : IQueryTranslationPostprocessorFactory
{
    private readonly QueryTranslationPostprocessorDependencies _dependencies;
    private readonly RelationalQueryTranslationPostprocessorDependencies _relationalDependencies;
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlQueryTranslationPostprocessorFactory(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _relationalDependencies = relationalDependencies
            ?? throw new ArgumentNullException(nameof(relationalDependencies));
        _singletonOptions = (singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions)))
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    public QueryTranslationPostprocessor Create(
        QueryCompilationContext queryCompilationContext
    ) => new MySqlQueryTranslationPostprocessor(
        _dependencies,
        _relationalDependencies,
        (RelationalQueryCompilationContext)queryCompilationContext,
        _singletonOptions);
}
