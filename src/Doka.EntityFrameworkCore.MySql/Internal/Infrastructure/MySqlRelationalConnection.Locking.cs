namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlRelationalConnection
{
    private DbConnection? _lockingReadEnlistedConnection;
    private Transaction? _lockingReadEnlistedTransaction;
    private StateChangeEventHandler? _lockingReadStateChanged;
    private bool _requiresEmulatedAmbientReceipt;
    private bool _validateLockingReadAfterOpen;

    /// <summary>Gets whether successful driver enlistment still belongs to this open physical connection.</summary>
    internal bool HasActiveEnlistment => DbConnection.State == ConnectionState.Open
        && ReferenceEquals(_lockingReadEnlistedConnection, DbConnection)
        && IsActiveLockingReadTransaction(_lockingReadEnlistedTransaction);

    /// <summary>Gets whether this still-confirmed binding uses the driver's non-XA ambient implementation.</summary>
    internal bool RequiresEmulatedAmbientReceipt => _requiresEmulatedAmbientReceipt && HasActiveEnlistment;

    /// <summary>Captures only the current managed transaction identity for one uncached native receipt.</summary>
    internal object CaptureEmulatedAmbientReceipt()
        => RequiresEmulatedAmbientReceipt && _lockingReadEnlistedTransaction is { } transaction
            ? transaction
            : throw new InvalidOperationException("An active confirmed emulated ambient transaction is required.");

    /// <summary>Checks both captured identities and current activity without caching native liveness.</summary>
    internal bool MatchesEmulatedAmbientReceipt(
        DbConnection physical,
        object transaction
    ) => RequiresEmulatedAmbientReceipt
        && ReferenceEquals(physical, DbConnection)
        && ReferenceEquals(transaction, _lockingReadEnlistedTransaction);

    /// <summary>Enables transaction validation for the current command and returns the previous nested scope.</summary>
    internal bool BeginLockingReadCommand()
    {
        var previous = _validateLockingReadAfterOpen;
        _validateLockingReadAfterOpen = true;

        return previous;
    }

    /// <summary>Restores the previous command scope after execution, failure or cancellation.</summary>
    internal void EndLockingReadCommand(
        bool previousGuard
    ) => _validateLockingReadAfterOpen = previousGuard;

    /// <inheritdoc />
    public override bool Open(
        bool errorsExpected = false
    )
    {
        var validateLockingRead = _validateLockingReadAfterOpen;
        var opened = base.Open(errorsExpected);

        if (validateLockingRead)
        {
            try
            {
                ValidateLockingReadTransaction();
            }
            catch (Exception guardFailure)
            {
                try
                {
                    // WHY: Only successful base.Open acquired this reference. Base open
                    // failures must retain EF's behavior and never consume a caller's scope.
                    Close();
                }
                catch (Exception closeFailure)
                {
                    throw new AggregateException(guardFailure, closeFailure);
                }

                throw;
            }
        }

        return opened;
    }

    /// <inheritdoc />
    public override Task<bool> OpenAsync(
        CancellationToken cancellationToken,
        bool errorsExpected = false
    ) => _validateLockingReadAfterOpen
        ? OpenLockingReadAsync(errorsExpected, cancellationToken)
        : base.OpenAsync(cancellationToken, errorsExpected);

    private async Task<bool> OpenLockingReadAsync(
        bool errorsExpected,
        CancellationToken cancellationToken
    )
    {
        var opened = await base
            .OpenAsync(cancellationToken, errorsExpected)
            .ConfigureAwait(false);

        try
        {
            await ValidateLockingReadTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception guardFailure)
        {
            try
            {
                // WHY: Cleanup is uncancellable so a cancelled request cannot retain
                // our successfully acquired reference and corrupt later pooled reuse.
                await CloseAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception closeFailure)
            {
                throw new AggregateException(guardFailure, closeFailure);
            }

            throw;
        }

        return opened;
    }

    /// <summary>Requires a live local transaction or confirmed enlistment on the physical connection.</summary>
    internal void ValidateLockingReadTransaction()
    {
        if (DbConnection.State == ConnectionState.Open)
        {
            if (CurrentTransaction is not null)
            {
                MySqlLocalTransactionGuard.Validate(this);

                return;
            }

            // WHY: Transaction.Current proves scope presence, not actual enlistment.
            // Only the successful driver-enlistment callback may establish this receipt.
            if (HasActiveEnlistment)
            {
                if (RequiresEmulatedAmbientReceipt)
                {
                    MySqlAmbientTransactionGuard.Validate(this);
                }

                return;
            }
        }

        throw new InvalidOperationException(
            "A locking read requires an active transaction enlisted on this database connection.");
    }

    private async Task ValidateLockingReadTransactionAsync(
        CancellationToken cancellationToken
    )
    {
        if (DbConnection.State == ConnectionState.Open)
        {
            if (CurrentTransaction is not null)
            {
                await MySqlLocalTransactionGuard
                    .ValidateAsync(this, cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            // WHY: The local savepoint proof is not valid inside an ambient XA transaction.
            // Preserve the separate confirmed-enlistment contract rather than assuming local ownership.
            if (HasActiveEnlistment)
            {
                if (RequiresEmulatedAmbientReceipt)
                {
                    await MySqlAmbientTransactionGuard
                        .ValidateAsync(this, cancellationToken)
                        .ConfigureAwait(false);
                }

                return;
            }
        }

        throw new InvalidOperationException(
            "A locking read requires an active transaction enlisted on this database connection.");
    }

    /// <summary>Discards enlistment proof and its handler when the physical connection changes lifecycle.</summary>
    internal void ClearLockingReadEnlistment()
    {
        if (_lockingReadEnlistedConnection is { } connection)
        {
            connection.StateChange -= _lockingReadStateChanged;
        }

        _lockingReadEnlistedConnection = null;
        _lockingReadEnlistedTransaction = null;
        _requiresEmulatedAmbientReceipt = false;
    }

    /// <inheritdoc />
    protected override void ConnectionEnlistTransaction(
        Transaction? transaction
    )
    {
        ClearLockingReadEnlistment();
        base.ConnectionEnlistTransaction(transaction);

        if (transaction is not null
            && DbConnection.State == ConnectionState.Open)
        {
            // WHY: Only driver configuration is cached after actual enlistment succeeds.
            // Native liveness is re-proved for each typed command, never stored in this receipt.
            var requiresEmulatedReceipt =
                !new MySqlConnectionStringBuilder(DbConnection.ConnectionString).UseXaTransactions;

            _lockingReadEnlistedTransaction = transaction;
            _requiresEmulatedAmbientReceipt = requiresEmulatedReceipt;
            _lockingReadEnlistedConnection = DbConnection;
            _lockingReadStateChanged ??= OnLockingReadConnectionStateChanged;
            DbConnection.StateChange += _lockingReadStateChanged;
        }
    }

    /// <inheritdoc />
    protected override void OpenDbConnection(
        bool errorsExpected
    )
    {
        ClearLockingReadEnlistment();
        base.OpenDbConnection(errorsExpected);
    }

    /// <inheritdoc />
    protected override Task OpenDbConnectionAsync(
        bool errorsExpected,
        CancellationToken cancellationToken
    )
    {
        ClearLockingReadEnlistment();

        return base.OpenDbConnectionAsync(errorsExpected, cancellationToken);
    }

    /// <inheritdoc />
    protected override void CloseDbConnection()
    {
        ClearLockingReadEnlistment();
        base.CloseDbConnection();
    }

    /// <inheritdoc />
    protected override Task CloseDbConnectionAsync()
    {
        // WHY: Physical cleanup is intentionally uncancellable, matching EF's connection lifecycle.
        ClearLockingReadEnlistment();

        return base.CloseDbConnectionAsync();
    }

    /// <inheritdoc />
    protected override void ResetState(
        bool disposeDbConnection
    )
    {
        _validateLockingReadAfterOpen = false;
        ClearLockingReadEnlistment();
        base.ResetState(disposeDbConnection);
    }

    /// <inheritdoc />
    protected override ValueTask ResetStateAsync(
        bool disposeDbConnection
    )
    {
        // WHY: Reset must discard proof even if disposing the caller's completed transaction fails.
        _validateLockingReadAfterOpen = false;
        ClearLockingReadEnlistment();

        return base.ResetStateAsync(disposeDbConnection);
    }

    private void OnLockingReadConnectionStateChanged(
        object? sender,
        StateChangeEventArgs args
    )
    {
        if (args.CurrentState is ConnectionState.Closed or ConnectionState.Broken)
        {
            ClearLockingReadEnlistment();
        }
    }

    private static bool IsActiveLockingReadTransaction(
        Transaction? transaction
    )
    {
        try
        {
            return transaction?.TransactionInformation.Status == TransactionStatus.Active;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
