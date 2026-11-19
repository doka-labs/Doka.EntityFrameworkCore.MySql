namespace Doka.EntityFrameworkCore.MySql;

internal static partial class MySqlUpsertExecutor
{
    /// <summary>Resolves the provider connection before any input binding or native work.</summary>
    private static MySqlRelationalConnection GetConnection(
        DbContext context,
        out EngineProfile engine
    )
    {
        var options = context
            .GetService<IDbContextOptions>()
            .FindExtension<MySqlOptionsExtension>();

        if (options?.ServerVersion?.Profile.Engine is not { } configuredEngine
            || context.GetService<IRelationalConnection>() is not MySqlRelationalConnection connection)
        {
            throw new InvalidOperationException("Native UPSERT requires a Doka MySQL-family context.");
        }

        engine = configuredEngine;

        return connection;
    }

    /// <summary>Enters the existing context concurrency scope only when EF thread-safety checks are enabled.</summary>
    private static ConcurrencyDetectorCriticalSectionDisposer? EnterCriticalSection(
        DbContext context
    ) => context.GetService<ICoreSingletonOptions>()
        .AreThreadSafetyChecksEnabled
        ? context
            .GetService<IConcurrencyDetector>()
            .EnterCriticalSection()
        : null;

    /// <summary>Validates mapping and update selection even for an empty input range.</summary>
    private static MySqlUpsertCommandBuilder CreateShape<TEntity>(
        DbContext context,
        IReadOnlyList<TEntity> entities,
        IReadOnlyList<string> updatePropertyNames,
        EngineProfile engine,
        out ISqlGenerationHelper sql
    )
        where TEntity : class
    {
        var entityType = ValidateInputTypes(context.Model, entities);
        sql = context.GetService<ISqlGenerationHelper>();

        return MySqlUpsertCommandBuilder.Create(entityType, updatePropertyNames, engine, sql);
    }

    /// <summary>Checks the actual opened connection's ambient or local transaction boundary before writing.</summary>
    private static IDbContextTransaction? ValidateTransactionBoundary(
        MySqlRelationalConnection connection,
        bool requiresMultipleCommands
    )
    {
        var enlisted = connection.HasActiveEnlistment;
        var ambientPresent = Transaction.Current is not null || connection.EnlistedTransaction is not null;

        if (ambientPresent && !enlisted)
        {
            throw new InvalidOperationException(
                "UPSERT requires confirmed transaction enlistment on this physical connection.");
        }

        var callerTransaction = connection.CurrentTransaction;

        if (callerTransaction is not null
            && !ReferenceEquals(callerTransaction.GetDbTransaction()
                    .Connection,
                connection.DbConnection))
        {
            throw new InvalidOperationException("UPSERT requires a live caller transaction on the active connection.");
        }

        if (requiresMultipleCommands && (enlisted || ambientPresent))
        {
            // WHY: Ambient enlistment has no qualified local savepoint handle for an atomic multi-command call.
            throw new InvalidOperationException(
                "Multi-command UPSERT requires a local transaction with a qualified savepoint boundary.");
        }

        if (requiresMultipleCommands && callerTransaction is { SupportsSavepoints: false })
        {
            throw new InvalidOperationException(
                "Multi-command UPSERT requires savepoint support in the caller transaction.");
        }

        return callerTransaction;
    }

    /// <summary>Validates every concrete mapped input type without snapshotting values or traversing graphs.</summary>
    private static IEntityType ValidateInputTypes<TEntity>(
        IModel model,
        IReadOnlyList<TEntity> entities
    )
        where TEntity : class
    {
        // WHY: Runtime lookup consumes the existing model dictionary and supports proxy types;
        // FindEntityType(Type) imposes reflection-preservation requirements this operation does not need.
        var selected = entities.Count == 0
            ? model.FindRuntimeEntityType(typeof(TEntity))
            : null;

        // WHY: A range's metadata cannot be inferred only from its first entity. This cheap
        // prepass rejects heterogeneous/unmapped/null inputs before any value binding or write.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index]
                ?? throw new ArgumentNullException(nameof(entities), "An UPSERT range cannot contain null elements.");

            var mapped = model.FindRuntimeEntityType(entity.GetType())
                ?? throw new InvalidOperationException("An UPSERT input type is not mapped in the active model.");

            if (selected is not null
                && !ReferenceEquals(selected, mapped))
            {
                throw new InvalidOperationException("An UPSERT range must contain one concrete mapped entity type.");
            }

            selected = mapped;
        }

        return selected
            ?? throw new InvalidOperationException("The UPSERT entity type is not mapped in the active model.");
    }
}
