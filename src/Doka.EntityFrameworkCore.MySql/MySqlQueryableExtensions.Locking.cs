namespace Doka.EntityFrameworkCore.MySql;

public static partial class MySqlQueryableExtensions
{
    /// <summary>Identifies the exact deferred exclusive-lock operator during preprocessing.</summary>
    internal static readonly MethodInfo
        ForUpdateMethod = typeof(MySqlQueryableExtensions).GetMethod(nameof(ForUpdate))!;

    /// <summary>Identifies the exact deferred shared-lock operator during preprocessing.</summary>
    internal static readonly MethodInfo ForShareMethod = typeof(MySqlQueryableExtensions).GetMethod(nameof(ForShare))!;

    /// <summary>Identifies the exact deferred NOWAIT operator during preprocessing.</summary>
    internal static readonly MethodInfo ForUpdateNoWaitMethod =
        typeof(MySqlQueryableExtensions).GetMethod(nameof(ForUpdateNoWait))!;

    /// <summary>Identifies the exact deferred SKIP LOCKED operator during preprocessing.</summary>
    internal static readonly MethodInfo ForUpdateSkipLockedMethod =
        typeof(MySqlQueryableExtensions).GetMethod(nameof(ForUpdateSkipLocked))!;

    /// <summary>Requests an exclusive locking read for the executed direct SELECT block.</summary>
    /// <typeparam name="TEntity">The source row type.</typeparam>
    /// <param name="source">The deferred Doka query.</param>
    /// <returns>The unexecuted query with an explicit FOR UPDATE policy.</returns>
    /// <remarks>
    /// Execution requires an active transaction on the actual connection and transactional InnoDB tables.
    /// The clause covers directly joined tables in the final block; it does not recursively lock subqueries.
    /// Native indexes and isolation determine the lock scope. Query construction and SQL inspection perform no I/O.
    /// Unsupported query shapes and conflicting locking policies fail before the data command.
    /// The caller owns transaction completion; disposing a reader does not commit it.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="InvalidOperationException">The source is not an asynchronous EF query.</exception>
    // WHY: Preserve the public extension metadata and XML member layout; C#14 blocks add exported marker types.
    // ReSharper disable once ConvertToExtensionBlock
    public static IQueryable<TEntity> ForUpdate<TEntity>(
        this IQueryable<TEntity> source
    )
        where TEntity : class => ApplyLockingRead(source, LockingMethods<TEntity>.Update);

    /// <summary>Requests a shared locking read for the executed direct SELECT block.</summary>
    /// <typeparam name="TEntity">The source row type.</typeparam>
    /// <param name="source">The deferred Doka query.</param>
    /// <returns>The query using FOR SHARE on MySQL or LOCK IN SHARE MODE on MariaDB.</returns>
    /// <remarks>
    /// The transaction, InnoDB, direct-block scope and caller-ownership requirements of
    /// <see cref="ForUpdate{TEntity}" /> also apply. Shared locks do not exclude ordinary consistent readers.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="InvalidOperationException">The source is not an asynchronous EF query.</exception>
    public static IQueryable<TEntity> ForShare<TEntity>(
        this IQueryable<TEntity> source
    )
        where TEntity : class => ApplyLockingRead(source, LockingMethods<TEntity>.Share);

    /// <summary>Requests FOR UPDATE NOWAIT without automatic retry of its native lock conflict.</summary>
    /// <typeparam name="TEntity">The source row type.</typeparam>
    /// <param name="source">The deferred Doka query.</param>
    /// <returns>The unexecuted query with the native NOWAIT policy.</returns>
    /// <remarks>
    /// The transaction, InnoDB, direct-block scope and caller-ownership requirements of
    /// <see cref="ForUpdate{TEntity}" /> also apply. Native NOWAIT rules do not promise that every
    /// possible metadata or infrastructure wait is eliminated. No session timeout is changed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="InvalidOperationException">The source is not an asynchronous EF query.</exception>
    public static IQueryable<TEntity> ForUpdateNoWait<TEntity>(
        this IQueryable<TEntity> source
    )
        where TEntity : class => ApplyLockingRead(source, LockingMethods<TEntity>.NoWait);

    /// <summary>Requests FOR UPDATE SKIP LOCKED for independently reserved InnoDB candidates.</summary>
    /// <typeparam name="TEntity">The source row type.</typeparam>
    /// <param name="source">The deferred Doka query.</param>
    /// <returns>The unexecuted query that skips candidates excluded by native row locks.</returns>
    /// <remarks>
    /// The transaction, InnoDB, direct-block scope and caller-ownership requirements of
    /// <see cref="ForUpdate{TEntity}" /> also apply. The result can be incomplete or empty; this is
    /// not a general consistent-snapshot or completeness guarantee. Specify ordering explicitly.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The source is null.</exception>
    /// <exception cref="InvalidOperationException">The source is not an asynchronous EF query.</exception>
    public static IQueryable<TEntity> ForUpdateSkipLocked<TEntity>(
        this IQueryable<TEntity> source
    )
        where TEntity : class => ApplyLockingRead(source, LockingMethods<TEntity>.SkipLocked);

    /// <summary>Preserves the exact public marker without evaluating or altering the source tree.</summary>
    private static IQueryable<TEntity> ApplyLockingRead<TEntity>(
        IQueryable<TEntity> source,
        MethodInfo method
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var provider = source.Provider;

        // WHY: An arbitrary asynchronous provider can discard an unknown marker and return an
        // unlocked query. Require EF's actual provider so unsupported operators reach its translator.
#pragma warning disable EF1001
        var isEfProvider = provider is EntityQueryProvider;
#pragma warning restore EF1001

        if (!isEfProvider)
        {
            throw new InvalidOperationException("Locking reads require a Doka EF query provider.");
        }

        // WHY: Typed deferred markers survive query composition without allowing ordinary tags
        // to control SQL or mutating another query's cached expression tree.
        return provider.CreateQuery<TEntity>(Expression.Call(method, source.Expression));
    }

    /// <summary>Retains the closed marker methods without per-call reflection or dynamic generic construction.</summary>
    private static class LockingMethods<TEntity>
        where TEntity : class
    {
        internal static readonly MethodInfo Update = ((Func<IQueryable<TEntity>, IQueryable<TEntity>>)ForUpdate).Method;
        internal static readonly MethodInfo Share = ((Func<IQueryable<TEntity>, IQueryable<TEntity>>)ForShare).Method;

        internal static readonly MethodInfo NoWait = ((Func<IQueryable<TEntity>, IQueryable<TEntity>>)ForUpdateNoWait)
            .Method;

        internal static readonly MethodInfo SkipLocked =
            ((Func<IQueryable<TEntity>, IQueryable<TEntity>>)ForUpdateSkipLocked).Method;
    }
}
