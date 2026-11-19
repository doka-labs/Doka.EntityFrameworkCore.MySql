namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Provides explicit native writes without change tracking or generated-value readback.</summary>
public static class MySqlDbContextExtensions
{
    /// <summary>Inserts one entity or updates the selected properties on any native unique-key conflict.</summary>
    /// <typeparam name="TEntity">The concrete mapped input entity type.</typeparam>
    /// <param name="context">The Doka context owning the mapping and connection.</param>
    /// <param name="entity">The stable input entity; this operation does not modify it.</param>
    /// <param name="updatePropertyNames">A nonempty selection of EF property names to update.</param>
    /// <param name="cancellationToken">The required token for forward database work.</param>
    /// <returns>The driver's native affected-row count, without insert/update classification.</returns>
    /// <remarks>
    /// Every primary or unique key may select the conflict. Query filters do not authorize this
    /// write. No generated values are read back, no tracked values are synchronized, and no
    /// automatic retry is performed. A connection loss during commit can leave the outcome unknown.
    /// An existing local transaction requires savepoint support; three transaction-control commands
    /// prove its live boundary before the data command without discarding earlier caller work.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The update selection is empty, duplicate, or unknown.</exception>
    /// <exception cref="InvalidOperationException">The provider, mapping, value, or transaction is unsupported.</exception>
    /// <exception cref="OperationCanceledException">The requested work is cancelled.</exception>
    // WHY: Preserve the public extension metadata and XML member layout; C#14 blocks add exported marker types.
    // ReSharper disable once ConvertToExtensionBlock
    public static Task<long> ExecuteUpsertAsync<TEntity>(
        this DbContext context,
        TEntity entity,
        IReadOnlyList<string> updatePropertyNames,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(updatePropertyNames);

        return MySqlUpsertExecutor.ExecuteAsync(context, new[] { entity }, updatePropertyNames, cancellationToken);
    }

    /// <summary>Executes an ordered range of native UPSERT candidates with bounded command payloads.</summary>
    /// <typeparam name="TEntity">The homogeneous mapped input entity type.</typeparam>
    /// <param name="context">The Doka context owning the mapping and connection.</param>
    /// <param name="entities">The stable ordered input range.</param>
    /// <param name="updatePropertyNames">A nonempty selection of EF property names to update.</param>
    /// <param name="cancellationToken">The required token for binding, writes, and an owned commit.</param>
    /// <returns>The checked sum of native command counts; a valid empty range returns zero.</returns>
    /// <remarks>
    /// Repeated input keys retain their order. A split invocation uses one owned transaction or a
    /// savepoint inside a live caller transaction. The caller's transaction is never committed.
    /// Native deadlocks or rollback-on-timeout can invalidate the whole caller transaction; a
    /// savepoint cannot restore it. Inputs and their tracking state are not changed.
    /// Caller-owned local transactions require a fresh savepoint and immediate rollback-to receipt;
    /// these bounded transaction-control commands are separate from the data-command count.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument or range element is null.</exception>
    /// <exception cref="ArgumentException">The update selection is empty, duplicate, or unknown.</exception>
    /// <exception cref="InvalidOperationException">A mapping, value, payload, or transaction cannot be safely written.</exception>
    /// <exception cref="OperationCanceledException">The requested work is cancelled, including an empty range.</exception>
    public static Task<long> ExecuteUpsertRangeAsync<TEntity>(
        this DbContext context,
        IReadOnlyList<TEntity> entities,
        IReadOnlyList<string> updatePropertyNames,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(updatePropertyNames);

        return MySqlUpsertExecutor.ExecuteAsync(context, entities, updatePropertyNames, cancellationToken);
    }
}
