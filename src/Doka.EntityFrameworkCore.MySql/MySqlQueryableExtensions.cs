namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Composes statement-local sources using the Doka MySQL provider.
/// </summary>
public static partial class MySqlQueryableExtensions
{
    internal static readonly MethodInfo WithMaterializedSourceMethod =
        typeof(MySqlQueryableExtensions).GetMethod(nameof(WithMaterializedSource))!;

    /// <summary>
    /// Binds a completed SQL source once for all references inside a composed query.
    /// </summary>
    /// <typeparam name="TSource">The mapped entity or flat SQL projection being bound.</typeparam>
    /// <typeparam name="TResult">The composed result row type.</typeparam>
    /// <param name="source">The complete source, including filters, duplicates and paging.</param>
    /// <param name="compose">The query composed against the statement-local source.</param>
    /// <returns>An unexecuted query whose references share the selected occurrences and values.</returns>
    /// <remarks>
    /// Construction performs no I/O. Enumeration uses one SQL statement per execution attempt.
    /// Source paging is preserved; order the composed result explicitly when order matters.
    /// Deferred client or collection source projections and multi-command split queries cannot
    /// be bound. Ordinary EF translation rules still apply to the composed result.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The source is not an EF query.</exception>
    public static IQueryable<TResult> WithMaterializedSource<TSource, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(compose);

        if (source.Provider is not IAsyncQueryProvider)
        {
            throw new InvalidOperationException("Materialized sources require a Doka EF Core query provider.");
        }

        return source.Provider.CreateQuery<TResult>(
            Expression.Call(null, ClosedMethod<TSource, TResult>.Info, source.Expression, Expression.Quote(compose)));
    }

    /// <summary>
    /// Captures a closed method without reflection-based generic construction on each API call.
    /// </summary>
    private static class ClosedMethod<TSource, TResult>
    {
        public static readonly MethodInfo Info =
            ((Func<IQueryable<TSource>, Expression<Func<IQueryable<TSource>, IQueryable<TResult>>>,
                IQueryable<TResult>>)WithMaterializedSource<TSource, TResult>).Method;
    }
}
