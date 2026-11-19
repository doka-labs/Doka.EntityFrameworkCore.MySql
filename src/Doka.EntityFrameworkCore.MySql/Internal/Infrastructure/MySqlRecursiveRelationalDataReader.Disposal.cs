namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlRecursiveRelationalDataReader
{
    /// <inheritdoc />

    // WHY: EF Core 10.0.8 requires RelationalDataReader but hides ownership fields; base.Dispose would double-close or use uninitialized state.
    [SuppressMessage("Usage",
        "CA2215:Dispose methods should call base class dispose",
        Justification =
            "EF Core 10.0.8 return-type adapter owns separate state; base cleanup is unsafe. See D-024 and the recursive reader disposal regression.")]
    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? failure = null;

        try
        {
            try
            {
                Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (!DisposeSuppressed(ref failure))
            {
                try
                {
                    DbDataReader.Dispose();
                }
                catch (Exception exception)
                {
                    AddFailure(ref failure, exception);
                }

                try
                {
                    DbCommand.Parameters.Clear();
                }
                catch (Exception exception)
                {
                    AddFailure(ref failure, exception);
                }

                try
                {
                    DbCommand.Dispose();
                }
                catch (Exception exception)
                {
                    AddFailure(ref failure, exception);
                }

                if (!_referenceReleaseAttempted)
                {
                    _referenceReleaseAttempted = true;

                    try
                    {
                        // WHY: Release only the one successful base execution reference, once even when native close fails.
                        RelationalConnection.Close();
                    }
                    catch (Exception exception)
                    {
                        AddFailure(ref failure, exception);
                    }
                }
            }
        }
        finally
        {
            ClearOwnedReferences();
        }

        ThrowFailure(failure);
    }

    /// <inheritdoc />

    // WHY: EF Core 10.0.8 base.DisposeAsync owns inaccessible private fields; calling it would duplicate this owner's reference release.
    [SuppressMessage("Usage",
        "CA2215:Dispose methods should call base class dispose",
        Justification =
            "EF Core 10.0.8 imposes this base return type; actual baseline proof skips command/connection after reader failure. See D-024.")]
    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? failure = null;

        try
        {
            try
            {
                await CloseAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (!DisposeSuppressed(ref failure))
            {
                try
                {
                    // WHY: Cleanup is intentionally uncancellable so request cancellation cannot retain owned resources.
                    await DbDataReader
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AddFailure(ref failure, exception);
                }

                try
                {
                    DbCommand.Parameters.Clear();
                }
                catch (Exception exception)
                {
                    AddFailure(ref failure, exception);
                }

                try
                {
                    await DbCommand
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AddFailure(ref failure, exception);
                }

                if (!_referenceReleaseAttempted)
                {
                    _referenceReleaseAttempted = true;

                    try
                    {
                        await RelationalConnection
                            .CloseAsync()
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        AddFailure(ref failure, exception);
                    }
                }
            }
        }
        finally
        {
            ClearOwnedReferences();
        }

        ThrowFailure(failure);
    }

    private bool DisposeSuppressed(
        ref Exception? failure
    )
    {
        try
        {
            if (_logger?.ShouldLogDataReaderDispose(DateTimeOffset.UtcNow) == true)
            {
                return _logger.DataReaderDisposing(RelationalConnection,
                        DbCommand,
                        DbDataReader,
                        _commandId,
                        DbDataReader.RecordsAffected,
                        _readCount,
                        _startTime,
                        Stopwatch.GetElapsedTime(_startTimestamp))
                    .IsSuppressed;
            }
        }
        catch (Exception exception)
        {
            AddFailure(ref failure, exception);
        }

        return false;
    }

    private void ClearOwnedReferences()
    {
        _reader = null;
        _command = null;
        _connection = null;
        _logger = null;
    }
}
