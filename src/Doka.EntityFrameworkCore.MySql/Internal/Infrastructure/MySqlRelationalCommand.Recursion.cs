namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlRelationalCommand
{
    private MySqlRecursiveRelationalDataReader ExecuteRecursiveReader(
        RelationalCommandParameterObject parameterObject,
        MySqlRecursiveQueryPolicy policy
    )
    {
        var previous = ExecuteOriginalReader(WithoutBuffering(parameterObject));
        var owner = new MySqlRecursiveRelationalDataReader(previous, parameterObject.Logger);

        try
        {
            var guarded = new MySqlRecursiveDataReader(owner.DbDataReader, policy);

            if (parameterObject.ReaderColumns is { } columns)
            {
                BufferRecursiveReader(owner, guarded, columns, parameterObject);
            }
            else
            {
                InstallReader(owner, guarded, parameterObject);
                guarded.Prime();
            }

            return owner;
        }
        catch (Exception primaryFailure)
        {
            try
            {
                owner.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(primaryFailure, cleanupFailure);
            }

            throw;
        }
    }

    private async Task<RelationalDataReader> ExecuteRecursiveReaderAsync(
        RelationalCommandParameterObject parameterObject,
        MySqlRecursiveQueryPolicy policy,
        CancellationToken cancellationToken
    )
    {
        var previous = await ExecuteOriginalReaderAsync(WithoutBuffering(parameterObject), cancellationToken)
            .ConfigureAwait(false);

        var owner = new MySqlRecursiveRelationalDataReader(previous, parameterObject.Logger);

        try
        {
            var guarded = new MySqlRecursiveDataReader(owner.DbDataReader, policy);

            if (parameterObject.ReaderColumns is { } columns)
            {
                await BufferRecursiveReaderAsync(owner, guarded, columns, parameterObject, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                InstallReader(owner, guarded, parameterObject);
                await guarded
                    .PrimeAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return owner;
        }
        catch (Exception primaryFailure)
        {
            try
            {
                // WHY: Failed setup still owns the reader/open reference. Request cancellation
                // must not prevent the normal EF owner from draining and releasing resources.
                await owner
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(primaryFailure, cleanupFailure);
            }

            throw;
        }
    }

    private static void BufferRecursiveReader(
        MySqlRecursiveRelationalDataReader owner,
        MySqlRecursiveDataReader guarded,
        IReadOnlyList<ReaderColumn?> columns,
        RelationalCommandParameterObject parameters
    )
    {
        Exception? failure = null;
        guarded.BeginCleanupCapture();

        try
        {
            // WHY: Preserve EF11's actual retry buffer after the guard can skip the
            // synthetic row, with ownership installed before initialization can fail.
#pragma warning disable EF1001 // Narrow EF11 buffering seam; original ReaderColumns and patch qualification remain required.
            var buffered = new BufferedDataReader(guarded, parameters.DetailedErrorsEnabled);
            InstallReader(owner, buffered, parameters);
            buffered.Initialize(columns);
#pragma warning restore EF1001
            InstallReader(owner, buffered, parameters);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            MergeDeferredCleanup(ref failure, guarded.EndCleanupCapture());
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    private static async Task BufferRecursiveReaderAsync(
        MySqlRecursiveRelationalDataReader owner,
        MySqlRecursiveDataReader guarded,
        IReadOnlyList<ReaderColumn?> columns,
        RelationalCommandParameterObject parameters,
        CancellationToken cancellationToken
    )
    {
        Exception? failure = null;
        guarded.BeginCleanupCapture();

        try
        {
#pragma warning disable EF1001 // Same EF11 buffer only for originally buffered queries; no streaming query gains a full buffer.
            var buffered = new BufferedDataReader(guarded, parameters.DetailedErrorsEnabled);
            InstallReader(owner, buffered, parameters);
            await buffered
                .InitializeAsync(columns, cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore EF1001
            InstallReader(owner, buffered, parameters);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            MergeDeferredCleanup(ref failure, guarded.EndCleanupCapture());
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    private static void MergeDeferredCleanup(
        ref Exception? failure,
        Exception? cleanupFailure
    )
    {
        if (cleanupFailure is not null)
        {
            failure = failure is null
                ? cleanupFailure
                : new AggregateException(failure, cleanupFailure);
        }
    }

    private static RelationalCommandParameterObject WithoutBuffering(
        RelationalCommandParameterObject parameters
    ) => new(
        parameters.Connection,
        parameters.ParameterValues,
        readerColumns: null,
        parameters.Context,
        parameters.Logger,
        parameters.DetailedErrorsEnabled,
        parameters.CommandSource);

    private static void InstallReader(
        MySqlRecursiveRelationalDataReader owner,
        DbDataReader reader,
        RelationalCommandParameterObject parameters
    )
    {
        // WHY: Initialize transfers the top reader reference without disposing it. Installing
        // before buffering keeps failure ownership intact; repeating after success restores
        // EF's normal post-buffer read-count/timing boundary without replacing command/logging state.
        owner.Initialize(parameters.Connection, owner.DbCommand, reader, owner.CommandId, parameters.Logger);
    }
}
