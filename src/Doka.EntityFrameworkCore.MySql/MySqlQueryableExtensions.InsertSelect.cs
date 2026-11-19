namespace Doka.EntityFrameworkCore.MySql;

public static partial class MySqlQueryableExtensions
{
    /// <summary>Inserts a typed SQL source into one mapped physical target with one native INSERT SELECT statement.</summary>
    /// <typeparam name="TSource">The SQL source row type.</typeparam>
    /// <typeparam name="TTarget">The mapped target entity type.</typeparam>
    /// <param name="source">The query whose occurrences supply the inserted rows.</param>
    /// <param name="target">The target set from the same context and model.</param>
    /// <param name="setters">Records the target-property and source-value assignments.</param>
    /// <param name="cancellationToken">The caller's required command cancellation token.</param>
    /// <returns>The native affected-row count. An empty source produces zero.</returns>
    /// <remarks>
    /// Omitted server-generated/defaulted columns retain database semantics; explicit SQL NULL
    /// remains explicit. The operation performs no source preload, target construction, readback,
    /// SaveChanges or change-tracker synchronization. Target query filters are not insert policies.
    /// No implicit execution-strategy retry is performed. Cancellation, connection loss or native
    /// count overflow after execution does not prove that an autocommitted write was rolled back.
    /// A borrowed local transaction additionally uses SAVEPOINT, immediate ROLLBACK TO and RELEASE
    /// controls to prove that the server still owns it before the one INSERT data statement.
    /// These controls neither roll back prior caller work nor commit, fully roll back or dispose
    /// the caller's transaction. Autocommit inserts require no transaction-control receipt.
    /// Confirmed non-XA ambient enlistment uses the same three controls in one native batch
    /// on the opened physical connection, without an EF local transaction object. Default XA
    /// enlistment keeps its existing behavior; neither route completes the caller's ambient scope.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The contexts, mappings, assignments or source shape are incompatible.</exception>
    /// <exception cref="OperationCanceledException">The supplied token is canceled.</exception>
    public static Task<int> ExecuteInsertFromQueryAsync<TSource, TTarget>(
        this IQueryable<TSource> source,
        DbSet<TTarget> target,
        Action<MySqlInsertSettersBuilder<TSource, TTarget>> setters,
        CancellationToken cancellationToken
    )
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(setters);
        cancellationToken.ThrowIfCancellationRequested();

        IQueryable<TTarget> targetQuery = target;

#pragma warning disable EF1001 // The actual EF provider preserves the terminal instead of silently ignoring a custom expression.
        var isEfProvider = source.Provider is EntityQueryProvider;
#pragma warning restore EF1001

        if (!isEfProvider
            || !ReferenceEquals(source.Provider, targetQuery.Provider)
            || source.Provider is not IAsyncQueryProvider provider)
        {
            throw new InvalidOperationException(
                "INSERT SELECT requires source and target from the same Doka EF Core context.");
        }

        var filter = ((IInfrastructure<IServiceProvider>)target).Instance
            .GetRequiredService<IEvaluatableExpressionFilter>();

        MySqlInsertQuerySourceGuard.Validate(source.Expression, target.EntityType.Model, filter);
        var builder = new MySqlInsertSettersBuilder<TSource, TTarget>(target.EntityType, filter);
        setters(builder);
        var assignments = builder.Build();
        var parameter = Expression.Parameter(typeof(TSource), "sourceRow");
        var properties = new IProperty[assignments.Length];
        var values = new Expression[assignments.Length];

        // WHY: Only the projection expression is built; none of these lambdas is compiled or
        // invoked for source rows. EF subsequently parameterizes captured scalar values normally.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < assignments.Length; index++)
        {
            var assignment = assignments[index];
            properties[index] = assignment.Property;
            var value = ReplacingExpressionVisitor.Replace(
                assignment.ValueSelector.Parameters[0],
                parameter,
                assignment.ValueSelector.Body);

            values[index] = Expression.Convert(Expression.Call(assignment.ProjectionMethod, value), typeof(object));
        }

        var projection = Expression.Lambda<Func<TSource, object[]>>(

            // WHY: The typed template roots object[] statically for AOT analysis. Only its
            // elements change; the operation never constructs an array with a runtime CLR type.
            MySqlInsertProjectionMarker.CreateValueArray(values),
            parameter);

        var query = new MySqlInsertSelectQueryExpression(source.Select(projection)
                .Expression,
            target.EntityType,
            properties);

        return provider.ExecuteAsync<Task<int>>(query, cancellationToken);
    }
}
