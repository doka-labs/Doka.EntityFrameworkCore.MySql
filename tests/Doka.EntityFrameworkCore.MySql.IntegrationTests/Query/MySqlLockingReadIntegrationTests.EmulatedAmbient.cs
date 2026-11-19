using IsolationLevel = System.Transactions.IsolationLevel;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlLockingReadIntegrationTests
{
    /// <summary>Reports actual automatic and explicit non-XA enlistment with independent native rollback outcomes.</summary>
    public static TheoryData<string, bool, string, bool> EmulatedAmbientCases => MySqlEmulatedAmbientProbe.Cases;

    /// <summary>A managed active ambient scope cannot authorize an unlocked read after the native transaction has ended.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EmulatedAmbientCases))]
    public async Task Emulated_ambient_locking_checks_native_liveness_and_preserves_borrowed_pool_ownership(
        string target,
        bool automatic,
        string mode,
        bool timeoutOn
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var policy = timeoutOn
            ? await MySqlRollbackOnTimeoutFixture.StartAsync(Enum.Parse<IntegrationDatabaseTarget>(target),
                safety.Token)
            : null;

        await using var store = await LockStore.CreateAsync(target, safety.Token, policy?.ConnectionString);
        await MySqlEmulatedAmbientProbe.PrepareAsync(store.Observer, safety.Token);
        var flag = await MySqlEmulatedAmbientProbe.CountAsync(store.Observer,
            "SELECT @@innodb_rollback_on_timeout;",
            safety.Token);

        // WHY: The first open of this unique database pool is a fresh physical session for native wait observation; reuse is tested afterward.
        await using var worker =
            new MySqlConnection(MySqlEmulatedAmbientProbe.GetConnectionString(store.ConnectionString, automatic));

        var commands = new LockCommands();
        var fatal = mode == "deadlock" || timeoutOn;

        try
        {
            if (!automatic)
            {
                await worker.OpenAsync(safety.Token);
            }

            int? nativeFailure;
            int priorCount;
            int priorVisibleBeforeAbort;
            var rowId = 0;
            int held;
            Exception? error;
            bool associated;
            TransactionStatus status;

            // WHY: EF forbids explicit enlistment inside an ambient scope; this route uses an independent caller-owned transaction instead.
            using var caller = automatic
                ? null
                : new CommittableTransaction(
                    new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted });

            using (var scope = automatic
                ? new TransactionScope(TransactionScopeOption.RequiresNew,
                    new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
                    TransactionScopeAsyncFlowOption.Enabled)
                : null)
            {
                if (automatic)
                {
                    await worker.OpenAsync(safety.Token);
                }

                await using var context = store.CreateContext(commands, worker);
                var managed = automatic
                    ? Transaction.Current!
                    : caller!;

                if (!automatic)
                {
                    context.Database.EnlistTransaction(managed);
                }

                await context.Database.OpenConnectionAsync(safety.Token);

                // WHY: EF keeps automatic ambient enlistment on a separate stack; its explicit-enlistment getter is not an oracle for that route.
                associated = (automatic || ReferenceEquals(managed, context.Database.GetEnlistedTransaction()))
                    && context.Database.CurrentTransaction is null;

                nativeFailure = await MySqlEmulatedAmbientProbe.PrepareCallerStateAsync(store.Observer,
                    worker,
                    store.ConnectionString,
                    store.Version.IsMariaDb,
                    mode,
                    safety.Token);

                priorCount = await MySqlEmulatedAmbientProbe.CountAsync(worker,
                    "SELECT COUNT(*) FROM EmulatedAmbientProbe WHERE Id=100;",
                    safety.Token);

                status = managed.TransactionInformation.Status;

                // Act
                error = await Record.ExceptionAsync(async () => rowId = (await context
                    .Set<LockRow>()
                    .Where(row => row.Id == 1)
                    .ForUpdate()
                    .SingleAsync(safety.Token)).Id);

                held = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
                priorVisibleBeforeAbort = await MySqlEmulatedAmbientProbe.CountAsync(store.Observer,
                    "SELECT COUNT(*) FROM EmulatedAmbientProbe WHERE Id=100;",
                    safety.Token);

                // WHY: The automatic scope is not completed; explicit caller rollback below must release the same native write and locks.
            }

            caller?.Rollback();
            var borrowedOpen = worker.State;
            var released = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
            var priorAfterAbort = await MySqlEmulatedAmbientProbe.CountAsync(store.Observer,
                "SELECT COUNT(*) FROM EmulatedAmbientProbe WHERE Id=100;",
                safety.Token);

            var featureCommands = commands.Statements.Count;
            await worker.CloseAsync();
            await worker.OpenAsync(safety.Token);
            await using var followup = store.CreateContext(connection: worker);
            await using var transaction = await followup.Database.BeginTransactionAsync(safety.Token);
            var reused = await followup
                .Set<LockRow>()
                .Where(row => row.Id == 1)
                .ForUpdate()
                .SingleAsync(safety.Token);

            var heldAgain = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);
            await transaction.RollbackAsync(safety.Token);
            var releasedAgain = await ProbePkNoWaitAsync(store, RowsTable, 1, safety.Token);

            // Assert
            Assert.Equal(timeoutOn
                    ? 1
                    : 0,
                flag);

            Assert.Equal(mode == "valid"
                    ? null
                    : mode == "deadlock"
                        ? 1213
                        : 1205,
                nativeFailure);

            Assert.True(associated);
            Assert.Equal(TransactionStatus.Active, status);
            Assert.Equal(fatal
                    ? 0
                    : 1,
                priorCount);

            if (fatal)
            {
                MySqlEmulatedAmbientProbe.AssertMissingReceipt(error);
                Assert.Equal(0, held);
            }
            else
            {
                Assert.Null(error);
                Assert.Equal(1, rowId);
                AssertNativeConflict(store, held);
            }

            Assert.Equal(fatal
                    ? 0
                    : 1,
                featureCommands);

            Assert.Equal(0, priorVisibleBeforeAbort);
            Assert.Equal(0, priorAfterAbort);
            Assert.Equal(0, released);
            Assert.Equal(ConnectionState.Open, borrowedOpen);
            Assert.Equal(1, reused.Id);
            AssertNativeConflict(store, heldAgain);
            Assert.Equal(0, releasedAgain);
        }
        finally
        {
            // WHY: This unique pool belongs only to this test; aborted or cancelled scopes cannot leave its sessions behind.
            await MySqlConnection.ClearPoolAsync(worker, CancellationToken.None);
        }
    }
}
