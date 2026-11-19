namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Streams the visible recursive payload after checking its statement-local guard row.</summary>
internal sealed partial class MySqlRecursiveDataReader : DbDataReader, IDbColumnSchemaGenerator
{
    private readonly int _payloadCount;
    private readonly DbDataReader _reader;
    private bool _captureCleanupFailures;
    private Exception? _capturedCleanupFailure;
    private bool _disposed;
    private bool _firstReadPending;
    private bool _hasRows;
    private bool _primed;
    private bool _visibleReadStarted;

    /// <summary>Adopts a reader whose last two columns match the generated recursive guard.</summary>
    internal MySqlRecursiveDataReader(
        DbDataReader reader,
        MySqlRecursiveQueryPolicy policy
    )
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(policy);
        _reader = reader;
        _payloadCount = policy.ExpectedPayloadColumnCount;

        if (reader.FieldCount != _payloadCount + 2
            || !string.Equals(reader.GetName(_payloadCount), policy.PresenceColumnName, StringComparison.Ordinal)
            || !string.Equals(reader.GetName(_payloadCount + 1),
                policy.InvalidLimitColumnName,
                StringComparison.Ordinal))
        {
            throw MalformedGuard();
        }
    }

    /// <inheritdoc />
    public override int FieldCount
    {
        get
        {
            EnsureOpen();

            return _payloadCount;
        }
    }

    /// <inheritdoc />
    public override int VisibleFieldCount => FieldCount;

    /// <inheritdoc />
    public override bool HasRows
    {
        get
        {
            Prime();

            return _hasRows;
        }
    }

    /// <inheritdoc />
    public override int Depth => _reader.Depth;

    /// <inheritdoc />
    public override bool IsClosed => _disposed || _reader.IsClosed;

    /// <inheritdoc />
    public override int RecordsAffected => _reader.RecordsAffected;

    /// <summary>Defers input-disposal failures only while EF's buffer finally would otherwise replace a primary error.</summary>
    internal void BeginCleanupCapture()
    {
        _captureCleanupFailures = true;
        _capturedCleanupFailure = null;
    }

    /// <summary>Ends the bounded capture and returns every cleanup failure for immediate propagation by the command.</summary>
    internal Exception? EndCleanupCapture()
    {
        _captureCleanupFailures = false;
        var failure = _capturedCleanupFailure;
        _capturedCleanupFailure = null;

        return failure;
    }

    /// <summary>Checks exactly one cursor row and retains it for the first visible Read.</summary>
    internal void Prime()
    {
        EnsureOpen();

        if (_primed)
        {
            return;
        }

        ValidateGuardRow(_reader.Read());
    }

    /// <summary>Checks the first cursor row using the caller's cancellation token.</summary>
    internal async Task PrimeAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();

        if (_primed)
        {
            return;
        }

        ValidateGuardRow(await _reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    /// <inheritdoc />
    public override bool Read()
    {
        Prime();

        return _firstReadPending || !_hasRows
            ? ConsumeFirstRow()
            : _reader.Read();
    }

    /// <inheritdoc />
    public override Task<bool> ReadAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();

        if (!_primed)
        {
            return ReadFirstAsync(cancellationToken);
        }

        // WHY: Only the first row needs provider work. Subsequent streaming reads
        // retain the driver's actual task instead of allocating another async state machine.
        return _firstReadPending || !_hasRows
            ? Task.FromResult(ConsumeFirstRow())
            : _reader.ReadAsync(cancellationToken);
    }

    private async Task<bool> ReadFirstAsync(
        CancellationToken cancellationToken
    )
    {
        await PrimeAsync(cancellationToken)
            .ConfigureAwait(false);

        return ConsumeFirstRow();
    }

    private bool ConsumeFirstRow()
    {
        _firstReadPending = false;
        _visibleReadStarted = _hasRows;

        return _hasRows;
    }

    private void ValidateGuardRow(
        bool hasRow
    )
    {
        if (!hasRow
            || _reader.IsDBNull(_payloadCount + 1))
        {
            throw MalformedGuard();
        }

        var invalidLimit = ReadControl(_payloadCount + 1);

        if (invalidLimit == 1)
        {
            throw new InvalidOperationException(
                "The requested recursive depth must be below the active server recursion limit.");
        }

        if (invalidLimit == 2)
        {
            throw new InvalidOperationException(
                "The proved recursive text capacity exceeds the active server packet limit.");
        }

        if (invalidLimit != 0)
        {
            throw MalformedGuard();
        }

        _hasRows = !_reader.IsDBNull(_payloadCount);

        if (_hasRows && ReadControl(_payloadCount) != 1)
        {
            throw MalformedGuard();
        }

        _primed = true;
        _firstReadPending = true;
    }

    private long ReadControl(
        int ordinal
    )
    {
        try
        {
            return _reader.GetInt64(ordinal);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            // WHY: A replaced reader must not leak arbitrary replacement values into diagnostics.
            throw MalformedGuard();
        }
    }

    /// <inheritdoc />
    public override bool NextResult()
    {
        Prime();

        if (_reader.NextResult())
        {
            throw MalformedGuard();
        }

        return false;
    }

    /// <inheritdoc />
    public override async Task<bool> NextResultAsync(
        CancellationToken cancellationToken
    )
    {
        await PrimeAsync(cancellationToken)
            .ConfigureAwait(false);

        if (await _reader
            .NextResultAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            throw MalformedGuard();
        }

        return false;
    }

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <inheritdoc />
    public override void Close()
    {
        if (!_disposed)
        {
            try
            {
                _reader.Close();
            }
            catch (Exception exception)
            {
                RecordCleanupFailure(exception);
            }
        }
    }

    /// <inheritdoc />
    public override async Task CloseAsync()
    {
        if (!_disposed)
        {
            try
            {
                await _reader
                    .CloseAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                RecordCleanupFailure(exception);
            }
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (!_disposed)
            {
                _disposed = true;

                // WHY: Cleanup must drain/release the driver even after request cancellation.
                try
                {
                    await _reader
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    RecordCleanupFailure(exception);
                }
            }
        }
        finally
        {
            await base
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(
        bool disposing
    )
    {
        try
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                try
                {
                    _reader.Dispose();
                }
                catch (Exception exception)
                {
                    RecordCleanupFailure(exception);
                }
            }
        }
        finally
        {
            // WHY: Base disposal re-enters Close; the disposed flag prevents a second native close.
            base.Dispose(disposing);
        }
    }

    /// <summary>Enforces the ADO.NET column-index contract without exposing trailing controls.</summary>
    [SuppressMessage("Usage",
        "CA2201:Do not raise reserved exception types",
        Justification =
            "DbDataReader ordinal getters require IndexOutOfRangeException for indexes outside the visible result.")]
    private int VisibleOrdinal(
        int ordinal
    )
    {
        if ((uint)ordinal >= (uint)_payloadCount)
        {
            throw new IndexOutOfRangeException("The ordinal is outside the visible recursive payload.");
        }

        return ordinal;
    }

    private int ValueOrdinal(
        int ordinal
    )
    {
        EnsureVisibleRow();

        return VisibleOrdinal(ordinal);
    }

    private void EnsureVisibleRow()
    {
        EnsureOpen();

        if (!_visibleReadStarted)
        {
            throw new InvalidOperationException("No visible recursive query row is available.");
        }
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_disposed || _reader.IsClosed, this);

    private void RecordCleanupFailure(
        Exception exception
    )
    {
        if (!_captureCleanupFailures)
        {
            ExceptionDispatchInfo.Throw(exception);
        }

        // WHY: BufferedDataReader disposes its input in finally. Propagate these
        // failures immediately after that buffer scope instead of replacing its active read failure.
        _capturedCleanupFailure = _capturedCleanupFailure is null
            ? exception
            : new AggregateException(_capturedCleanupFailure, exception);
    }

    private static InvalidOperationException MalformedGuard()
        => new("The recursive query returned an invalid statement-local guard shape.");
}
