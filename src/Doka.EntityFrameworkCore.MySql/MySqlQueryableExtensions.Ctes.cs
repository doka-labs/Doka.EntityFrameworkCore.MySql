namespace Doka.EntityFrameworkCore.MySql;

public static partial class MySqlQueryableExtensions
{
    /// <summary>Identifies the exact lexical ordinary-CTE binding operator during preprocessing.</summary>
    internal static readonly MethodInfo WithCteMethod = typeof(MySqlQueryableExtensions).GetMethod(nameof(WithCte))!;

    /// <summary>Identifies the exact bounded recursive-CTE binding operator during preprocessing.</summary>
    internal static readonly MethodInfo WithRecursiveCteMethod =
        typeof(MySqlQueryableExtensions).GetMethod(nameof(WithRecursiveCte))!;

    /// <summary>Binds a completed SQL source as an ordinary statement-local common table expression.</summary>
    /// <typeparam name="TSource">The mapped entity or flat SQL projection being bound.</typeparam>
    /// <typeparam name="TResult">The composed result row type.</typeparam>
    /// <param name="source">The complete source, including filters, duplicates and paging.</param>
    /// <param name="compose">The query composed against the statement-local relation.</param>
    /// <returns>An unexecuted query with a lexical CTE definition and its typed references.</returns>
    /// <remarks>
    /// Construction performs no I/O. Native optimizer rules determine whether an ordinary CTE is
    /// merged, materialized or evaluated separately per reference; volatile values are not guaranteed
    /// to be shared. Use <see cref="WithMaterializedSource{TSource,TResult}" /> when that sharing is required.
    /// Source paging is preserved; order the composed result explicitly when order matters.
    /// Deferred client or collection source projections and multi-command split queries cannot be bound.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The source is not an EF query.</exception>
    public static IQueryable<TResult> WithCte<TSource, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(compose);

        var provider = GetCteProvider(source);

        return provider.CreateQuery<TResult>(Expression.Call(
            null,
            CteMethods<TSource, TResult>.Ordinary,
            source.Expression,
            Expression.Quote(compose)));
    }

    /// <summary>Binds an anchor and a server-side UNION ALL recursive step with an explicit maximum depth.</summary>
    /// <typeparam name="TSource">The common mapped entity or flat SQL projection of both recursive arms.</typeparam>
    /// <typeparam name="TResult">The composed result row type.</typeparam>
    /// <param name="anchor">The complete depth-zero source.</param>
    /// <param name="recursiveStep">The flat SQL step consuming exactly one previous-iteration frontier.</param>
    /// <param name="compose">The query composed against the entire bounded recursive relation.</param>
    /// <param name="maximumDepth">The inclusive semantic depth; zero selects the anchor only.</param>
    /// <returns>An unexecuted query whose anchor and recursive step execute in one SQL statement.</returns>
    /// <remarks>
    /// UNION ALL preserves repeated paths and cycles up to the chosen depth; no implicit visited set or
    /// duplicate removal is added. The semantic limit does not increase the server's recursion limit or
    /// bound the number of rows in a branching relation. Native-limit exhaustion is a visible failure.
    /// Both arms require compatible physical scalar shapes; unsupported recursive arms fail before execution.
    /// No preloaded client data, session setting or extra diagnostic statement implements recursion.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The maximum depth is negative.</exception>
    /// <exception cref="InvalidOperationException">The source is not an EF query.</exception>
    public static IQueryable<TResult> WithRecursiveCte<TSource, TResult>(
        this IQueryable<TSource> anchor,
        Expression<Func<IQueryable<TSource>, IQueryable<TSource>>> recursiveStep,
        Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose,

        // WHY: A proved text-growth bound changes the SQL column schema. Ordinary calls retain
        // this structural input; explicit compiled-query parameters keep their runtime guard.
        [NotParameterized] int maximumDepth
    )
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(recursiveStep);
        ArgumentNullException.ThrowIfNull(compose);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);

        var provider = GetCteProvider(anchor);

        return provider.CreateQuery<TResult>(Expression.Call(
            null,
            CteMethods<TSource, TResult>.Recursive,
            anchor.Expression,
            Expression.Quote(recursiveStep),
            Expression.Quote(compose),
            Expression.Constant(maximumDepth)));
    }

    /// <summary>Rejects query providers that could silently discard an unknown server-side binding marker.</summary>
    private static IQueryProvider GetCteProvider<TSource>(
        IQueryable<TSource> source
    )
    {
        var provider = source.Provider;

        // WHY: An arbitrary asynchronous provider may discard an unknown marker. EF's actual provider
        // retains the operator so an unsupported translation cannot silently become an ordinary query.
#pragma warning disable EF1001
        var isEfProvider = provider is EntityQueryProvider;
#pragma warning restore EF1001

        if (!isEfProvider)
        {
            throw new InvalidOperationException("Common table expressions require a Doka EF Core query provider.");
        }

        return provider;
    }

    /// <summary>Retains closed operators without reflection-based generic construction on each API call.</summary>
    private static class CteMethods<TSource, TResult>
    {
        internal static readonly MethodInfo Ordinary =
            ((Func<IQueryable<TSource>, Expression<Func<IQueryable<TSource>, IQueryable<TResult>>>,
                IQueryable<TResult>>)WithCte).Method;

        internal static readonly MethodInfo Recursive =
            ((Func<IQueryable<TSource>, Expression<Func<IQueryable<TSource>, IQueryable<TSource>>>,
                Expression<Func<IQueryable<TSource>, IQueryable<TResult>>>, int,
                IQueryable<TResult>>)WithRecursiveCte).Method;
    }
}
