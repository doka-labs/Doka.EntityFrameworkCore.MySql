namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Creates the scoped source-binding query normalizer.
/// </summary>
internal sealed class MySqlQueryTranslationPreprocessorFactory(
    QueryTranslationPreprocessorDependencies dependencies,
    RelationalQueryTranslationPreprocessorDependencies relationalDependencies
) : IQueryTranslationPreprocessorFactory
{
    /// <inheritdoc />
    public QueryTranslationPreprocessor Create(
        QueryCompilationContext queryCompilationContext
    ) => new MySqlQueryTranslationPreprocessor(dependencies, relationalDependencies, queryCompilationContext);
}
