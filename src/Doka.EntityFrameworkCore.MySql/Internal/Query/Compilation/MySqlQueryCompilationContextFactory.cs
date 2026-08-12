namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Creates provider-aware query-compilation contexts.
/// </summary>
internal sealed class MySqlQueryCompilationContextFactory : IQueryCompilationContextFactory
{
    private readonly QueryCompilationContextDependencies _dependencies;
    private readonly RelationalQueryCompilationContextDependencies _relationalDependencies;

    /// <summary>
    /// Initializes a new query-compilation-context factory.
    /// </summary>
    /// <param name="dependencies">The core query-compilation dependencies.</param>
    /// <param name="relationalDependencies">The relational query-compilation dependencies.</param>
    public MySqlQueryCompilationContextFactory(
        QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies
    )
    {
        _dependencies = dependencies;
        _relationalDependencies = relationalDependencies;
    }

    /// <summary>
    /// Creates a context for a regular query compilation.
    /// </summary>
    /// <param name="async">Whether the query executes asynchronously.</param>
    /// <returns>The provider query-compilation context.</returns>
    public QueryCompilationContext Create(
        bool async
    ) => new MySqlQueryCompilationContext(_dependencies, _relationalDependencies, async);

    /// <summary>
    /// Creates a context for precompiled query generation.
    /// </summary>
    /// <param name="async">Whether the generated query executes asynchronously.</param>
    /// <returns>The provider query-compilation context.</returns>
    [Experimental("EF9100")]
    public QueryCompilationContext CreatePrecompiled(
        bool async
    ) => new MySqlQueryCompilationContext(_dependencies, _relationalDependencies, async, precompiling: true);
}
