namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// EF Core service factory for <see cref="MySqlQueryableMethodTranslatingExpressionVisitor"/>.
/// Registered as <see cref="IQueryableMethodTranslatingExpressionVisitorFactory"/> via
/// <c>MySqlServiceCollectionExtensions</c> so the provider's JSON_TABLE-aware translator is
/// chosen in place of the relational default.
/// </summary>
internal sealed class MySqlQueryableMethodTranslatingExpressionVisitorFactory
    : IQueryableMethodTranslatingExpressionVisitorFactory
{
    private readonly QueryableMethodTranslatingExpressionVisitorDependencies _dependencies;
    private readonly RelationalQueryableMethodTranslatingExpressionVisitorDependencies _relationalDependencies;

    public MySqlQueryableMethodTranslatingExpressionVisitorFactory(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies
    )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _relationalDependencies = relationalDependencies ?? throw new ArgumentNullException(nameof(relationalDependencies));
    }

    public QueryableMethodTranslatingExpressionVisitor Create(
        QueryCompilationContext queryCompilationContext
    ) => new MySqlQueryableMethodTranslatingExpressionVisitor(
        _dependencies,
        _relationalDependencies,
        (RelationalQueryCompilationContext)queryCompilationContext);
}
