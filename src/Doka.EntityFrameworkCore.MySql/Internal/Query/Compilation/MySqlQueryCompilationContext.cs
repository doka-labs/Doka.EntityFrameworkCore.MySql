using System.Diagnostics.CodeAnalysis;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Carries MySQL-specific state through relational query compilation.
/// </summary>
internal sealed class MySqlQueryCompilationContext : RelationalQueryCompilationContext
{
    private const string LazyLoadingProxyAnnotation = "Proxies:LazyLoading";

    /// <summary>
    /// Initializes a context for a regular query compilation.
    /// </summary>
    /// <param name="dependencies">The core query-compilation dependencies.</param>
    /// <param name="relationalDependencies">The relational query-compilation dependencies.</param>
    /// <param name="async">Whether the query executes asynchronously.</param>
    public MySqlQueryCompilationContext(
        QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies,
        bool async
    ) : base(dependencies, relationalDependencies, async) { }

    /// <summary>
    /// Initializes a context for a regular or precompiled query.
    /// </summary>
    /// <param name="dependencies">The core query-compilation dependencies.</param>
    /// <param name="relationalDependencies">The relational query-compilation dependencies.</param>
    /// <param name="async">Whether the query executes asynchronously.</param>
    /// <param name="precompiling">Whether EF Core is generating a precompiled query.</param>
    [Experimental("EF9100")]
    public MySqlQueryCompilationContext(
        QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies,
        bool async,
        bool precompiling
    ) : base(dependencies, relationalDependencies, async, precompiling) { }

    /// <summary>
    /// Gets whether the current query must buffer its result before issuing another command.
    /// </summary>
    /// <remarks>
    /// MySqlConnector permits only one active reader per connection. Split queries buffer
    /// each result set before EF Core executes the next command. Lazy-loading proxy queries
    /// also buffer because relationship fixup or a client projection can re-enter the
    /// connection while the root entity is being materialized. Other single queries retain
    /// EF Core's streaming behavior.
    /// </remarks>
    public override bool IsBuffering =>
        base.IsBuffering
        || QuerySplittingBehavior == global::Microsoft.EntityFrameworkCore.QuerySplittingBehavior.SplitQuery
        || Model.FindAnnotation(LazyLoadingProxyAnnotation)?.Value is true;

    /// <summary>
    /// Gets whether the provider can generate precompiled queries.
    /// </summary>
    public override bool SupportsPrecompiledQuery => true;
}
