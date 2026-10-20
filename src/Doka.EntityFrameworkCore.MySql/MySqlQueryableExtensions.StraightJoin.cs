namespace Doka.EntityFrameworkCore.MySql;

public static partial class MySqlQueryableExtensions
{
    /// <summary>Identifies the exact public operator during preprocessing and extension composition.</summary>
    internal static readonly MethodInfo StraightJoinMethod =
        typeof(MySqlQueryableExtensions).GetMethod(nameof(StraightJoin))!;

    /// <summary>Joins two SQL sources while retaining the outer-before-inner direction of this one join edge.</summary>
    /// <typeparam name="TOuter">The outer source row type.</typeparam>
    /// <typeparam name="TInner">The inner source row type.</typeparam>
    /// <typeparam name="TKey">The common join-key type.</typeparam>
    /// <typeparam name="TResult">The projected result row type.</typeparam>
    /// <param name="outer">The source on the left of this directed edge.</param>
    /// <param name="inner">The source on the right of this directed edge.</param>
    /// <param name="outerKeySelector">The ordinary EF join key of each outer row.</param>
    /// <param name="innerKeySelector">The ordinary EF join key of each inner row.</param>
    /// <param name="resultSelector">The ordinary EF result projection for matching occurrences.</param>
    /// <returns>An unexecuted query with the bag, key and projection semantics of the corresponding EF Join.</returns>
    /// <remarks>
    /// Doka emits STRAIGHT_JOIN for this edge on MySQL and MariaDB. Other joins remain ordinary joins.
    /// This directive can also change available optimizer transformations, including MySQL semijoin
    /// and antijoin transformations, even though other join edges remain unmarked.
    /// The operator does not impose result order, a database snapshot, source materialization or a
    /// performance outcome. Filters, null handling, navigation shaping and paging follow EF's ordinary
    /// Join translation. Both sources must use the same EF query provider; other providers cannot
    /// translate this Doka operator. Query construction performs no I/O.
    /// </remarks>
    /// <exception cref="ArgumentNullException">One of the sources or selectors is null.</exception>
    /// <exception cref="InvalidOperationException">The inputs do not use the same asynchronous EF query provider.</exception>
    public static IQueryable<TResult> StraightJoin<TOuter, TInner, TKey, TResult>(
        this IQueryable<TOuter> outer,
        IQueryable<TInner> inner,
        Expression<Func<TOuter, TKey>> outerKeySelector,
        Expression<Func<TInner, TKey>> innerKeySelector,
        Expression<Func<TOuter, TInner, TResult>> resultSelector
    )
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(outerKeySelector);
        ArgumentNullException.ThrowIfNull(innerKeySelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        if (outer.Provider is not IAsyncQueryProvider
            || !ReferenceEquals(outer.Provider, inner.Provider))
        {
            throw new InvalidOperationException(
                "StraightJoin requires both inputs to use the same Doka EF query provider.");
        }

        // WHY: Keep the public operator in the deferred expression so compiled queries and other
        // extensions can recognize its exact public contract without inspecting provider-private markers.
        return outer.Provider.CreateQuery<TResult>(
            Expression.Call(
                StraightJoinMethodCache<TOuter, TInner, TKey, TResult>.Method,
                outer.Expression,
                inner.Expression,
                Expression.Quote(outerKeySelector),
                Expression.Quote(innerKeySelector),
                Expression.Quote(resultSelector)));
    }

    /// <summary>Closes the public operator once for each exact generic argument tuple.</summary>
    private static class StraightJoinMethodCache<TOuter, TInner, TKey, TResult>
    {
        internal static readonly MethodInfo Method =
            ((Func<IQueryable<TOuter>, IQueryable<TInner>, Expression<Func<TOuter, TKey>>,
                    Expression<Func<TInner, TKey>>, Expression<Func<TOuter, TInner, TResult>>, IQueryable<TResult>>)
                StraightJoin).Method;
    }
}
