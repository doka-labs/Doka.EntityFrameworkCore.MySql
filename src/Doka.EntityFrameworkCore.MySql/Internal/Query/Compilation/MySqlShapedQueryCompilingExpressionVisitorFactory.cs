namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Creates the narrow explicit-write compiler on the existing relational service graph.</summary>
internal sealed class MySqlShapedQueryCompilingExpressionVisitorFactory : IShapedQueryCompilingExpressionVisitorFactory
{
    private readonly ShapedQueryCompilingExpressionVisitorDependencies _dependencies;
    private readonly RelationalShapedQueryCompilingExpressionVisitorDependencies _relationalDependencies;

    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies
    )
    {
        _dependencies = dependencies;
        _relationalDependencies = relationalDependencies;
    }

    /// <inheritdoc />
    public ShapedQueryCompilingExpressionVisitor Create(
        QueryCompilationContext queryCompilationContext
    ) => new MySqlShapedQueryCompilingExpressionVisitor(_dependencies,
        _relationalDependencies,
        queryCompilationContext);
}
