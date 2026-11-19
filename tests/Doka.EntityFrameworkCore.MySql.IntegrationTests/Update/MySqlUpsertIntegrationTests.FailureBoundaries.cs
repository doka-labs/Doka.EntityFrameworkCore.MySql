namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>A late oversized candidate cannot leave the first bounded command committed independently.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TransactionCases))]
    public async Task Late_oversized_row_rolls_back_earlier_batches_and_preserves_prior_caller_work(
        string target,
        bool borrowed
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
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

        var inputs = Enumerable
            .Range(1, 1002)
            .Select(id => NewRow(id))
            .ToArray();

        var oversized = new string('x', checked((int)MySqlUpsertCommandBuilder.MaximumPayloadBytes));
        inputs[^1].Name = oversized;

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertRangeAsync(inputs, new[] { nameof(UpsertRow.Name) }, CancellationToken.None));

        var remaining = await context
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Single(capture.Statements);
        Assert.Equal(borrowed
                ? s_priorWorkIds
                : Array.Empty<int>(),
            remaining);

        Assert.Same(oversized, inputs[^1].Name);
        Assert.DoesNotContain(oversized, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(borrowed, context.Database.CurrentTransaction is not null);
    }

    /// <summary>A failure reported after the native commit is never retried or misreported as a rolled-back call.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Exception_after_server_commit_remains_visible_without_replaying_the_range(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        var capture = new Writes();
        var commit = new FailureAfterNativeCommit();
        await using var context = store.CreateContext(capture, additionalInterceptor: commit);
        var inputs = Enumerable
            .Range(1, 1001)
            .Select(id => NewRow(id))
            .ToArray();

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertRangeAsync(inputs, new[] { nameof(UpsertRow.Name) }, CancellationToken.None));

        await using var reader = store.CreateContext();
        var persistedCount = await reader.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(error);
        IEnumerable<Exception> causes = error is AggregateException aggregate
            ? aggregate.Flatten()
                .InnerExceptions
            : new[] { error };

        Assert.Contains(causes, cause => cause is ControlledCommitFailure);
        Assert.Equal(1, commit.CommitEvents);
        Assert.Equal(2, capture.Statements.Count);
        Assert.Equal(1001, persistedCount);
        Assert.Equal(1, inputs[0].Id);
        Assert.Equal(1001, inputs[^1].Id);
    }

    private sealed class FailureAfterNativeCommit : DbTransactionInterceptor
    {
        internal int CommitEvents { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            // WHY: This callback runs after the native commit. Throwing here reproduces
            // an uncertain reported outcome without inventing a rollback or a retry policy.
            CommitEvents++;

            throw new ControlledCommitFailure();
        }
    }

    private sealed class ControlledCommitFailure : Exception { }
}
