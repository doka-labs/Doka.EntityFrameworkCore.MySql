namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Executes native UPSERT commands once inside an invocation-owned transaction boundary when needed.</summary>
internal static partial class MySqlUpsertExecutor
{
    private static readonly IReadOnlyDictionary<string, object?> s_emptyParameterValues =
        new Dictionary<string, object?>();

    /// <summary>Preserves input order, native counts and caller ownership without creating tracked entries.</summary>
    internal static async Task<long> ExecuteAsync<TEntity>(
        DbContext context,
        IReadOnlyList<TEntity> entities,
        IReadOnlyList<string> updatePropertyNames,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = GetConnection(context, out var engine);
        using var criticalSection = EnterCriticalSection(context);
        var shape = CreateShape(context, entities, updatePropertyNames, engine, out var sql);

        if (entities.Count == 0)
        {
            return 0;
        }

        var factory = context.GetService<IRelationalCommandBuilderFactory>();
        var logger = context.GetService<IRelationalCommandDiagnosticsLogger>();
        DbCommand? parameterFactory = null;
        IDbContextTransaction? ownedTransaction = null;
        IDbContextTransaction? callerTransaction = null;
        string? savepoint = null;
        var opened = false;
        var commitStarted = false;
        long affected = 0;
        Exception? failure = null;
        List<Exception>? cleanupFailures = null;

        // WHY: One async coordinator owns every resource handoff and the failure/cleanup
        // boundary. Extracted decisions and native-task forwarding need no extra per-call
        // owner object or async state machine, including when database I/O suspends.
        try
        {
            parameterFactory = connection.DbConnection.CreateCommand();
            var nextIndex = 0;
            MySqlUpsertCommandBuilder.BoundRow? pending = null;
            var batch = shape.BindNextBatch(context,
                parameterFactory,
                entities,
                ref nextIndex,
                ref pending,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            opened = true;

            callerTransaction = ValidateTransactionBoundary(connection, batch.HasMore);

            if (batch.HasMore)
            {
                if (callerTransaction is null)
                {
                    ownedTransaction = await connection
                        .BeginTransactionAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    // WHY: The driver can retain its transaction object after an engine-wide
                    // rollback. Only a successful rollback to a fresh marker proves it is live.
                    savepoint = await MySqlLocalTransactionGuard
                        .CreateValidatedSavepointAsync(connection, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else if (callerTransaction is not null)
            {
                await MySqlLocalTransactionGuard
                    .ValidateAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = await ExecuteBatchAsync(
                        context,
                        connection,
                        shape,
                        batch,
                        factory,
                        sql,
                        logger,
                        cancellationToken)
                    .ConfigureAwait(false);

                // WHY: A command has at most 1,000 candidates, each contributing at most two
                // native affected rows. The wide checked sum is real accounting across commands.
                affected = checked(affected + count);

                if (!batch.HasMore)
                {
                    break;
                }

                batch = shape.BindNextBatch(context,
                    parameterFactory,
                    entities,
                    ref nextIndex,
                    ref pending,
                    cancellationToken);
            }

            if (savepoint is not null)
            {
                await callerTransaction!
                    .ReleaseSavepointAsync(savepoint, cancellationToken)
                    .ConfigureAwait(false);

                savepoint = null;
            }

            if (ownedTransaction is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                commitStarted = true;
                await ownedTransaction
                    .CommitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = exception;

            try
            {
                // WHY: Rollback must remain possible after cancellation. Native victim/connection
                // failures may destroy the boundary; retaining that cleanup error makes this visible.
                if (savepoint is not null)
                {
                    await callerTransaction!
                        .RollbackToSavepointAsync(savepoint, CancellationToken.None)
                        .ConfigureAwait(false);

                    await callerTransaction
                        .ReleaseSavepointAsync(savepoint, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else if (ownedTransaction is not null
                    && !commitStarted)
                {
                    await ownedTransaction
                        .RollbackAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception cleanupFailure)
            {
                (cleanupFailures ??= []).Add(cleanupFailure);
            }
        }
        finally
        {
            // WHY: These resources are invocation-owned. Their uncancellable cleanup must finish
            // even when a write/commit failed; caller connections and transactions are never disposed.
            if (ownedTransaction is not null)
            {
                try
                {
                    await ownedTransaction
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    (cleanupFailures ??= []).Add(cleanupFailure);
                }
            }

            if (parameterFactory is not null)
            {
                try
                {
                    await parameterFactory
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    (cleanupFailures ??= []).Add(cleanupFailure);
                }
            }

            if (opened)
            {
                try
                {
                    await connection
                        .CloseAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    (cleanupFailures ??= []).Add(cleanupFailure);
                }
            }
        }

        ThrowFailure(failure, cleanupFailures);

        return affected;
    }

    /// <summary>Builds one bounded command and forwards its native task without another async state machine.</summary>
    private static Task<int> ExecuteBatchAsync(
        DbContext context,
        MySqlRelationalConnection connection,
        MySqlUpsertCommandBuilder shape,
        MySqlUpsertCommandBuilder.Batch batch,
        IRelationalCommandBuilderFactory factory,
        ISqlGenerationHelper sql,
        IRelationalCommandDiagnosticsLogger logger,
        CancellationToken cancellationToken
    )
    {
        var command = shape.BuildCommand(batch, factory, sql);

        return command.ExecuteNonQueryAsync(
            new RelationalCommandParameterObject(
                connection,
                s_emptyParameterValues,
                readerColumns: null,
                context,
                logger,
                CommandSource.ExecuteSqlRaw),
            cancellationToken);
    }

    /// <summary>Preserves the original stack and the ordering of any additional cleanup failures.</summary>
    private static void ThrowFailure(
        Exception? failure,
        List<Exception>? cleanupFailures
    )
    {
        if (failure is not null)
        {
            if (cleanupFailures is not null)
            {
                cleanupFailures.Insert(0, failure);

                throw new AggregateException("UPSERT failed and invocation cleanup also failed.", cleanupFailures);
            }

            ExceptionDispatchInfo.Throw(failure);
        }

        if (cleanupFailures is not null)
        {
            throw new AggregateException("UPSERT invocation cleanup failed.", cleanupFailures);
        }
    }
}
