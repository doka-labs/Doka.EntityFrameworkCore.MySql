namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides MySQL-family temporal query operators for queries rooted in a <see cref="DbSet{TEntity}"/>.
/// </summary>
public static class MySqlDbSetExtensions
{
    /// <summary>
    /// Restricts a MariaDB application-time update or delete to the specified valid-time portion.
    /// </summary>
    /// <remarks>
    /// The returned query can only terminate in <c>ExecuteUpdate</c> or <c>ExecuteDelete</c>.
    /// MariaDB requires constant boundaries for portion deletes, so the provider captures both
    /// values in the query root and emits SQL literals rather than command parameters.
    /// </remarks>
    /// <typeparam name="TEntity">The application-time entity type.</typeparam>
    /// <param name="source">The application-time entity set.</param>
    /// <param name="from">The inclusive start of the affected valid-time portion.</param>
    /// <param name="to">The exclusive end of the affected valid-time portion.</param>
    /// <returns>A query root for an application-time update or delete.</returns>
    public static IQueryable<TEntity> ForPortionOf<TEntity>(
        this DbSet<TEntity> source,
        DateTime from,
        DateTime to
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        if (from >= to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(to),
                to,
                "The application-time range end must follow its start.");
        }

        var queryableSource = (IQueryable)source;
        var queryRoot = (EntityQueryRootExpression)queryableSource.Expression;

        return queryableSource.Provider.CreateQuery<TEntity>(
            new MySqlApplicationTimeQueryRootExpression(queryRoot.QueryProvider!, queryRoot.EntityType, from, to));
    }

    /// <summary>
    /// Returns the entity versions that were current at the specified UTC instant.
    /// </summary>
    /// <remarks>
    /// Temporal timestamps are persisted and compared as UTC values. Temporal queries are
    /// always no-tracking because a result can contain a historical version of an entity.
    /// </remarks>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The temporal entity set.</param>
    /// <param name="utcPointInTime">The UTC instant to query.</param>
    /// <returns>A no-tracking temporal query.</returns>
    public static IQueryable<TEntity> TemporalAsOf<TEntity>(
        this DbSet<TEntity> source,
        DateTime utcPointInTime
    )
        where TEntity : class
    {
        ValidateUtc(utcPointInTime, nameof(utcPointInTime));

        return CreateTemporalQuery(source, MySqlTemporalQueryOperation.AsOf, pointInTime: utcPointInTime);
    }

    /// <summary>
    /// Returns every entity version whose lifetime overlaps the half-open UTC range.
    /// </summary>
    /// <remarks>
    /// Versions created exactly at <paramref name="utcTo"/> or removed exactly at
    /// <paramref name="utcFrom"/> are excluded. Multiple versions of one key can be returned,
    /// so the query is always no-tracking.
    /// </remarks>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The temporal entity set.</param>
    /// <param name="utcFrom">The inclusive UTC lower boundary.</param>
    /// <param name="utcTo">The exclusive UTC upper boundary.</param>
    /// <returns>A no-tracking temporal query.</returns>
    public static IQueryable<TEntity> TemporalFromTo<TEntity>(
        this DbSet<TEntity> source,
        DateTime utcFrom,
        DateTime utcTo
    )
        where TEntity : class
    {
        ValidateRange(utcFrom, utcTo);

        return CreateTemporalQuery(source, MySqlTemporalQueryOperation.FromTo, from: utcFrom, to: utcTo);
    }

    /// <summary>
    /// Returns every entity version whose lifetime overlaps the UTC range, including its upper boundary.
    /// </summary>
    /// <remarks>
    /// A version removed exactly at <paramref name="utcTo"/> is included. Multiple versions
    /// of one key can be returned, so the query is always no-tracking.
    /// </remarks>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The temporal entity set.</param>
    /// <param name="utcFrom">The UTC lower boundary.</param>
    /// <param name="utcTo">The inclusive UTC upper boundary.</param>
    /// <returns>A no-tracking temporal query.</returns>
    public static IQueryable<TEntity> TemporalBetween<TEntity>(
        this DbSet<TEntity> source,
        DateTime utcFrom,
        DateTime utcTo
    )
        where TEntity : class
    {
        ValidateRange(utcFrom, utcTo);

        return CreateTemporalQuery(source, MySqlTemporalQueryOperation.Between, from: utcFrom, to: utcTo);
    }

    /// <summary>
    /// Returns every entity version whose complete lifetime is contained in the UTC range.
    /// </summary>
    /// <remarks>
    /// Both boundaries are inclusive. Multiple versions of one key can be returned, so the
    /// query is always no-tracking.
    /// </remarks>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The temporal entity set.</param>
    /// <param name="utcFrom">The inclusive UTC lower boundary.</param>
    /// <param name="utcTo">The inclusive UTC upper boundary.</param>
    /// <returns>A no-tracking temporal query.</returns>
    public static IQueryable<TEntity> TemporalContainedIn<TEntity>(
        this DbSet<TEntity> source,
        DateTime utcFrom,
        DateTime utcTo
    )
        where TEntity : class
    {
        ValidateRange(utcFrom, utcTo);

        return CreateTemporalQuery(source, MySqlTemporalQueryOperation.ContainedIn, from: utcFrom, to: utcTo);
    }

    /// <summary>
    /// Returns current and historical versions of every entity in the temporal table.
    /// </summary>
    /// <remarks>
    /// Multiple versions of one key can be returned, so the query is always no-tracking.
    /// </remarks>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The temporal entity set.</param>
    /// <returns>A no-tracking temporal query.</returns>
    public static IQueryable<TEntity> TemporalAll<TEntity>(
        this DbSet<TEntity> source
    )
        where TEntity : class => CreateTemporalQuery(source, MySqlTemporalQueryOperation.All);

    private static IQueryable<TEntity> CreateTemporalQuery<TEntity>(
        DbSet<TEntity> source,
        MySqlTemporalQueryOperation operation,
        DateTime? pointInTime = null,
        DateTime? from = null,
        DateTime? to = null
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var queryableSource = (IQueryable)source;
        var queryRoot = (EntityQueryRootExpression)queryableSource.Expression;

        return queryableSource
            .Provider.CreateQuery<TEntity>(
                new MySqlTemporalQueryRootExpression(
                    queryRoot.QueryProvider!,
                    queryRoot.EntityType,
                    operation,
                    pointInTime,
                    from,
                    to))
            .AsNoTracking();
    }

    private static void ValidateRange(
        DateTime utcFrom,
        DateTime utcTo
    )
    {
        ValidateUtc(utcFrom, nameof(utcFrom));
        ValidateUtc(utcTo, nameof(utcTo));

        if (utcFrom > utcTo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(utcTo),
                utcTo,
                "The temporal range end must not precede its start.");
        }
    }

    private static void ValidateUtc(
        DateTime value,
        string parameterName
    )
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Temporal query boundaries must use DateTimeKind.Utc.", parameterName);
        }
    }
}
