namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Owns only recursive readers and attempts each cleanup stage independently after a failure.</summary>
internal sealed partial class MySqlRecursiveRelationalDataReader : RelationalDataReader
{
    private bool _closed;
    private DbCommand? _command;
    private Guid _commandId;
    private IRelationalConnection? _connection;
    private bool _disposed;
    private IRelationalCommandDiagnosticsLogger? _logger;
    private int _readCount;
    private DbDataReader? _reader;
    private bool _referenceReleaseAttempted;
    private DateTimeOffset _startTime;
    private long _startTimestamp;

    /// <summary>Adopts the successful base execution's one reader/open reference without changing its cached owner.</summary>
    internal MySqlRecursiveRelationalDataReader(
        RelationalDataReader previous,
        IRelationalCommandDiagnosticsLogger? logger
    )
    {
        Initialize(previous.RelationalConnection,
            previous.DbCommand,
            previous.DbDataReader,
            previous.CommandId,
            logger);
    }

    /// <inheritdoc />
    public override IRelationalConnection RelationalConnection
        => _connection ?? throw new ObjectDisposedException(nameof(MySqlRecursiveRelationalDataReader));

    /// <inheritdoc />
    public override DbCommand DbCommand
        => _command ?? throw new ObjectDisposedException(nameof(MySqlRecursiveRelationalDataReader));

    /// <inheritdoc />
    public override DbDataReader DbDataReader
        => _reader ?? throw new ObjectDisposedException(nameof(MySqlRecursiveRelationalDataReader));

    /// <inheritdoc />
    public override Guid CommandId => _commandId;

    /// <inheritdoc />
    public override void Initialize(
        IRelationalConnection relationalConnection,
        DbCommand command,
        DbDataReader reader,
        Guid commandId,
        IRelationalCommandDiagnosticsLogger? logger
    )
    {
        // WHY: EF11's base state is private and its disposal skips later resources after
        // a reader/command failure. This opt-in owner keeps explicit progress and no shared registry.
        // EF also permits a fresh execution after disposal. Reset its disposal progress;
        // the caller supplies the acquired open reference, which Initialize never acquires itself.
        _connection = relationalConnection;
        _command = command;
        _reader = reader;
        _commandId = commandId;
        _logger = logger;
        _readCount = 0;
        _closed = false;
        _disposed = false;
        _referenceReleaseAttempted = false;
        _startTime = DateTimeOffset.UtcNow;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    /// <inheritdoc />
    public override bool Read()
    {
        var reader = DbDataReader;
        _readCount++;

        return reader.Read();
    }

    /// <inheritdoc />
    public override Task<bool> ReadAsync(
        CancellationToken cancellationToken = default
    )
    {
        var reader = DbDataReader;
        _readCount++;

        return reader.ReadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        var reader = DbDataReader;
        Exception? failure = null;
        var suppressed = false;

        try
        {
            if (_logger?.ShouldLogDataReaderClose(DateTimeOffset.UtcNow) == true)
            {
                suppressed = _logger.DataReaderClosing(RelationalConnection,
                        DbCommand,
                        reader,
                        _commandId,
                        reader.RecordsAffected,
                        _readCount,
                        _startTime)
                    .IsSuppressed;
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (!suppressed)
        {
            try
            {
                reader.Close();
            }
            catch (Exception exception)
            {
                AddFailure(ref failure, exception);
            }
        }

        ThrowFailure(failure);
    }

    /// <inheritdoc />
    public override async ValueTask CloseAsync()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        var reader = DbDataReader;
        Exception? failure = null;
        var suppressed = false;

        try
        {
            if (_logger?.ShouldLogDataReaderClose(DateTimeOffset.UtcNow) == true)
            {
                var result = await _logger
                    .DataReaderClosingAsync(RelationalConnection,
                        DbCommand,
                        reader,
                        _commandId,
                        reader.RecordsAffected,
                        _readCount,
                        _startTime)
                    .ConfigureAwait(false);

                suppressed = result.IsSuppressed;
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (!suppressed)
        {
            try
            {
                // WHY: Closing a cancelled reader must still release its protocol state.
                await reader
                    .CloseAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AddFailure(ref failure, exception);
            }
        }

        ThrowFailure(failure);
    }

    private static void AddFailure(
        ref Exception? failure,
        Exception next
    ) => failure = failure is null
        ? next
        : new AggregateException(failure, next);

    private static void ThrowFailure(
        Exception? failure
    )
    {
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }
}
