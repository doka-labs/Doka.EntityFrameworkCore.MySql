namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Creates provider-specific relational SQL translation visitors.
/// </summary>
internal sealed class MySqlSqlTranslatingExpressionVisitorFactory : IRelationalSqlTranslatingExpressionVisitorFactory
{
    private readonly RelationalSqlTranslatingExpressionVisitorDependencies _dependencies;

    public MySqlSqlTranslatingExpressionVisitorFactory(
        RelationalSqlTranslatingExpressionVisitorDependencies dependencies
    )
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public RelationalSqlTranslatingExpressionVisitor Create(
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor
    ) => new MySqlSqlTranslatingExpressionVisitor(
        _dependencies,
        queryCompilationContext,
        queryableMethodTranslatingExpressionVisitor);
}
