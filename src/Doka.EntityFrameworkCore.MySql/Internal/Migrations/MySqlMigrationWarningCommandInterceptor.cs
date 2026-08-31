namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Rejects migration DDL that the server accepted only after changing the
/// requested index semantics.
/// </summary>
internal sealed class MySqlMigrationWarningCommandInterceptor : DbCommandInterceptor
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbCommand, WarningCapture> _captures = new();

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        StartCapture(command, eventData.CommandSource);

        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        StartCapture(command, eventData.CommandSource);

        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result
    )
    {
        CompleteCapture(command);

        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        CompleteCapture(command);

        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(
        DbCommand command,
        CommandErrorEventData eventData
    ) => DiscardCapture(command);

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        DiscardCapture(command);

        return Task.CompletedTask;
    }

    private void StartCapture(
        DbCommand command,
        CommandSource commandSource
    )
    {
        if (commandSource != CommandSource.Migrations
            || command.Connection is not MySqlConnection connection)
        {
            return;
        }

        DiscardCapture(command);
        _captures.Add(command, new WarningCapture(connection));
    }

    private void CompleteCapture(
        DbCommand command
    )
    {
        var capture = RemoveCapture(command);
        if (capture is null)
        {
            return;
        }

        using (capture)
        {
            if (capture.TooLongKeyMessage is not { } serverMessage)
            {
                return;
            }

            throw new InvalidOperationException(
                "The server accepted a migration command only after reducing an index key (server code 1071). "
                + "Doka rejects the changed physical semantics and will not advance migration history. "
                + $"Configure an explicit prefix or reduce the indexed column lengths. Server message: {serverMessage}");
        }
    }

    private void DiscardCapture(
        DbCommand command
    ) => RemoveCapture(command)
        ?.Dispose();

    private WarningCapture? RemoveCapture(
        DbCommand command
    )
    {
        if (!_captures.TryGetValue(command, out var capture))
        {
            return null;
        }

        _captures.Remove(command);

        return capture;
    }

    private sealed class WarningCapture : IDisposable
    {
        private readonly MySqlConnection _connection;
        private readonly MySqlInfoMessageEventHandler _handler;

        public WarningCapture(
            MySqlConnection connection
        )
        {
            _connection = connection;
            _handler = OnInfoMessage;
            _connection.InfoMessage += _handler;
        }

        public string? TooLongKeyMessage { get; private set; }

        public void Dispose() => _connection.InfoMessage -= _handler;

        private void OnInfoMessage(
            object sender,
            MySqlInfoMessageEventArgs args
        )
        {
            foreach (var error in args.Errors)
            {
                if (error.ErrorCode == MySqlErrorCode.TooLongKey)
                {
                    TooLongKeyMessage ??= error.Message;
                }
            }
        }
    }
}
