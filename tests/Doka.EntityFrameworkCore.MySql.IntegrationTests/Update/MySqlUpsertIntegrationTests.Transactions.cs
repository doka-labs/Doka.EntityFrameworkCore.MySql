namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>Reports owned versus caller-owned invocation protection independently.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> TransactionCases => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, bool>(target.Data, false), new TheoryDataRow<string, bool>(target.Data, true),
        });

    /// <summary>An externally supplied native transaction retains ownership across multiple data commands.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task UseTransaction_keeps_the_native_owner_and_its_outer_rollback_effective(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var connection = new MySqlConnection(store.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);
        var capture = new Writes();
        var inputs = Enumerable
            .Range(1, 1001)
            .Select(id => NewRow(id))
            .ToArray();

        long affected;
        int insideCount;
        bool ownerRemainsAssociated;

        // Act
        await using (var context = store.CreateContext(capture, connection))
        {
            await context.Database.UseTransactionAsync(transaction, CancellationToken.None);
            affected = await context.ExecuteUpsertRangeAsync(inputs,
                new[] { nameof(UpsertRow.Name) },
                CancellationToken.None);

            insideCount = await context.Rows.CountAsync(CancellationToken.None);
        }

        ownerRemainsAssociated = ReferenceEquals(transaction.Connection, connection);
        await transaction.RollbackAsync(CancellationToken.None);
        await using var reader = store.CreateContext();
        var outsideCount = await reader.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1001, affected);
        Assert.Equal(1001, insideCount);
        Assert.True(ownerRemainsAssociated);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(0, outsideCount);
        Assert.Equal(2, capture.Statements.Count);
    }

    /// <summary>A range split into multiple statements commits only after all ordered candidates succeed.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Multi_batch_range_keeps_repeated_key_order_and_native_count_sum(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var rows = Enumerable
            .Range(1, 1000)
            .Select(id => NewRow(id))
            .Append(NewRow(1, "last-candidate"))
            .ToArray();

        // Act
        var affected =
            await context.ExecuteUpsertRangeAsync(rows, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var count = await reader.Rows.CountAsync(CancellationToken.None);
        var firstName = await reader
            .Rows
            .Where(row => row.Id == 1)
            .Select(row => row.Name)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1002, affected);
        Assert.Equal(1000, count);
        Assert.Equal("last-candidate", firstName);
        Assert.Equal(2, capture.Statements.Count);
        Assert.All(capture.ParameterCounts,
            countValue => Assert.InRange(countValue, 1, MySqlUpsertCommandBuilder.MaximumParameters));

        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>A borrowed transaction remains owned by the caller, whose later rollback removes the whole invocation.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Caller_transaction_is_not_committed_by_a_multi_batch_upsert(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(CancellationToken.None);
        var rows = Enumerable
            .Range(1, 1001)
            .Select(id => NewRow(id))
            .ToArray();

        // Act
        var affected =
            await context.ExecuteUpsertRangeAsync(rows, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        var stillOwned = ReferenceEquals(transaction, context.Database.CurrentTransaction);
        await transaction.RollbackAsync(CancellationToken.None);
        await using var reader = store.CreateContext();
        var count = await reader.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1001, affected);
        Assert.True(stillOwned);
        Assert.Equal(0, count);
    }

    /// <summary>A converter discovered after the first statement rolls back only the invocation boundary.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TransactionCases))]
    public async Task Late_converter_failure_rolls_back_call_batches_and_preserves_caller_prior_work(
        string target,
        bool borrowed
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "throw-converter", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        await using var transaction = borrowed
            ? await context.Database.BeginTransactionAsync(CancellationToken.None)
            : null;

        if (borrowed)
        {
            context.Add(NewRow(5000, "prior-work"));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var rows = Enumerable
            .Range(1, 1002)
            .Select(id => NewRow(id))
            .ToArray();

        rows[^1].Name = "controlled-converter-failure";

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertRangeAsync(rows, new[] { nameof(UpsertRow.Name) }, CancellationToken.None));

        var remaining = await context
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.IsType<ControlledBindFailure>(error);
        Assert.Single(capture.Statements);
        Assert.Equal(borrowed
                ? s_priorWorkIds
                : Array.Empty<int>(),
            remaining);

        Assert.Equal(borrowed, context.Database.CurrentTransaction is not null);
    }

    /// <summary>A late native constraint failure rolls back the first statement under the already established boundary.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TransactionCases))]
    public async Task Later_batch_unique_failure_cannot_leave_an_autocommitted_first_batch(
        string target,
        bool borrowed
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        context.AddRange(NewRow(1, "original"), NewRow(2, "other"));
        await context.SaveChangesAsync(CancellationToken.None);
        await using var transaction = borrowed
            ? await context.Database.BeginTransactionAsync(CancellationToken.None)
            : null;

        if (borrowed)
        {
            context.Add(NewRow(5000, "prior-work"));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var rows = Enumerable
            .Range(0, 1001)
            .Select(_ => NewRow(1, "changed"))
            .ToArray();

        rows[^1].UniqueKey = "key-2";

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertRangeAsync(rows,
            new[] { nameof(UpsertRow.Name), nameof(UpsertRow.UniqueKey) },
            CancellationToken.None));

        var values = await context
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.Name,
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1062,
            Assert.IsType<MySqlException>(error)
                .Number);

        Assert.Equal(2, capture.Statements.Count);
        Assert.Equal(borrowed
                ? s_originalAndPriorWorkIds
                : s_originalIds,
            values.Select(row => row.Id));

        Assert.Equal("original", values[0].Name);
        Assert.Equal("other", values[1].Name);
        Assert.Equal(borrowed, context.Database.CurrentTransaction is not null);
    }

    /// <summary>Cancellation after a completed first statement is not converted into a successful partial result.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Cancellation_between_batches_rolls_back_owned_work_and_keeps_the_connection_usable(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var capture = new Writes
        {
            AfterWrite = attempt => attempt == 1
                ? cancellation.CancelAsync()
                : Task.CompletedTask,
        };

        await using var context = store.CreateContext(capture);
        var rows = Enumerable
            .Range(1, 1001)
            .Select(id => NewRow(id))
            .ToArray();

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertRangeAsync(rows, new[] { nameof(UpsertRow.Name) }, cancellation.Token));

        var count = await context.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(cancellation.Token,
            Assert.IsType<OperationCanceledException>(error)
                .CancellationToken);

        Assert.Single(capture.Statements);
        Assert.Equal(0, count);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>The server, not the input object, supplies an identity value.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Identity_default_is_generated_without_input_readback_or_mutation(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "identity", CancellationToken.None);
        await using var context = store.CreateContext();
        var input = NewRow(0);

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, affected);
        Assert.True(persisted.Id > 0);
        Assert.Equal(0, input.Id);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
