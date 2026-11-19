namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>Reports normal statement-only rollback profiles and the two isolated full-rollback representatives.</summary>
    public static TheoryData<string, bool> NativeTimeoutCases
    {
        get
        {
            var data = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                if (Enum.Parse<IntegrationDatabaseTarget>(target) is IntegrationDatabaseTarget.MySql84
                    or IntegrationDatabaseTarget.MariaDb123)
                {
                    data.Add(target, false);
                    data.Add(target, true);
                }
                else
                {
                    data.Add(target, false);
                }
            }

            return data;
        }
    }

    /// <summary>A real UPSERT deadlock victim reports both the native abort and destroyed call-savepoint boundary.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_upsert_deadlock_reports_the_engine_aborted_caller_transaction_without_repair_or_retry(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", safety.Token);
        await using (var seed = store.CreateContext())
        {
            seed.AddRange(NewRow(1, "first"), NewRow(2, "second"));
            await seed.SaveChangesAsync(safety.Token);
        }

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        await context.Database.OpenConnectionAsync(safety.Token);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        context.Add(NewRow(5000, "prior-work"));
        await context.SaveChangesAsync(safety.Token);
        var inputs = Enumerable
            .Range(0, 1001)
            .Select(_ => NewRow(2, "must-not-persist"))
            .ToArray();

        // Act
        var error = await MakeCallerTheDeadlockVictimAsync(store,
            context,
            safety.Token,
            token => context.ExecuteUpsertRangeAsync(inputs, new[] { nameof(UpsertRow.Name) }, token));

        await using var reader = store.CreateContext();
        var survivingIds = await reader
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        var causes = Assert
            .IsType<AggregateException>(error)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(causes, cause => cause is MySqlException { Number: 1213 });
        Assert.Contains(causes, cause => cause is MySqlException { Number: 1305 });
        Assert.Single(capture.Statements);
        Assert.Equal(s_originalIds, survivingIds);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Open,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A real late native timeout distinguishes statement rollback from an engine-aborted caller transaction.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(NativeTimeoutCases))]
    public async Task Native_timeout_in_a_later_batch_preserves_only_the_work_the_engine_can_preserve(
        string target,
        bool fullRollback
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var policy = fullRollback
            ? await MySqlRollbackOnTimeoutFixture.StartAsync(Enum.Parse<IntegrationDatabaseTarget>(target),
                safety.Token)
            : null;

        await using var store =
            await UpsertStore.CreateAsync(target, "ordinary", safety.Token, policy?.ConnectionString);

        await using (var seed = store.CreateContext())
        {
            seed.AddRange(NewRow(1, "original"), NewRow(2, "other"));
            await seed.SaveChangesAsync(safety.Token);
        }

        await using var holder = new MySqlConnection(store.ConnectionString);
        await holder.OpenAsync(safety.Token);
        await using var holderTransaction = await holder.BeginTransactionAsync(safety.Token);
        await ExecuteNativeAsync(holder,
            "UPDATE `UpsertRows` SET `Unchanged`='held' WHERE `Id`=2;",
            safety.Token,
            holderTransaction);

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        context.Add(NewRow(5000, "prior-work"));
        await context.SaveChangesAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        await ExecuteNativeAsync(worker,
            "SET SESSION innodb_lock_wait_timeout=1;",
            safety.Token,
            (MySqlTransaction)transaction.GetDbTransaction());

        var inputs = Enumerable
            .Range(0, 1001)
            .Select(_ => NewRow(1, "must-roll-back"))
            .ToArray();

        inputs[^1] = NewRow(2, "blocked");

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertRangeAsync(inputs,
            new[] { nameof(UpsertRow.Name) },
            safety.Token));

        await holderTransaction.RollbackAsync(safety.Token);
        var values = await context
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToArrayAsync(safety.Token);

        await transaction.RollbackAsync(safety.Token);
        await using var reader = store.CreateContext();
        var finalIds = await reader
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.NotNull(error);
        IReadOnlyCollection<Exception> causes = error is AggregateException aggregate
            ? aggregate.Flatten()
                .InnerExceptions
            : new[] { error };

        Assert.Contains(causes, cause => cause is MySqlException { Number: 1205 });
        Assert.Equal(fullRollback
                ? s_originalIds
                : s_originalAndPriorWorkIds,
            values.Select(row => row.Id));

        Assert.Equal("original", values[0].Name);
        Assert.Equal("other", values[1].Name);
        Assert.Equal(s_originalIds, finalIds);
        Assert.Equal(2, capture.Statements.Count);

        if (fullRollback)
        {
            Assert.Contains(causes, cause => cause is MySqlException { Number: 1305 });
        }
        else
        {
            Assert.DoesNotContain(causes, cause => cause is MySqlException { Number: 1305 });
            Assert.Equal("prior-work", values[^1].Name);
        }
    }
}
