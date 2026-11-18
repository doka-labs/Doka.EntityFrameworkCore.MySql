namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteIntegrationTests
{
    /// <summary>Separates update and delete behavior on each supported server profile.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> MutationCases => SelectedTargets.SelectMany(target => new[]
    {
        new TheoryDataRow<string, string>(target.Data, "update"),
        new TheoryDataRow<string, string>(target.Data, "delete"),
    });

    /// <summary>CTE mutation targets physical rows, preserves native counts and follows caller rollback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(MutationCases))]
    public async Task Ordinary_cte_mutation_preserves_physical_target_count_and_transaction_scope(
        string target,
        string operation
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var version = IntegrationTestEnvironment.GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target));
        var supported = version.Profile.Engine.Has(EngineCapability.CteUpdateDelete);
        await using var transaction = supported
            ? await context.Database.BeginTransactionAsync(safety.Token)
            : null;

        var query = context
            .Rows
            .Where(row => row.Id <= 2)
            .Select(row => row.Id)
            .WithCte(ids => context.Targets.Where(row => ids.Contains(row.Id)));

        var affected = -1;
        int[] expectedIds = operation == "update"
            ? [1, 2, 3, 4]
            : [3, 4];

        int[] expectedValues = operation == "update"
            ? [11, 12, 3, 4]
            : [3, 4];

        // Act
        var failure = await Record.ExceptionAsync(async () =>
        {
            affected = operation == "update"
                ? await query.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Value, row => row.Value + 10),
                    safety.Token)
                : await query.ExecuteDeleteAsync(safety.Token);
        });

        var changed = failure is null
            ? await context
                .Targets
                .OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.Value,
                })
                .ToArrayAsync(safety.Token)
            : [];

        if (transaction is not null)
        {
            // WHY: This is the test-owned caller boundary. Its rollback must remove
            // the complete mutation independently of any subsequent forward-work cancellation.
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using var observer = store.CreateContext();
        var restored = await observer
            .Targets
            .OrderBy(row => row.Id)
            .Select(row => row.Value)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 2, 3, 4], restored);

        if (supported)
        {
            Assert.Null(failure);
            Assert.Equal(2, affected);
            Assert.Equal(expectedIds, changed.Select(row => row.Id));
            Assert.Equal(expectedValues, changed.Select(row => row.Value));
        }
        else
        {
            Assert.Contains("CTE",
                Assert.IsType<InvalidOperationException>(failure)
                    .Message,
                StringComparison.Ordinal);

            Assert.Equal(-1, affected);
            Assert.Empty(changed);
            Assert.Equal(ConnectionState.Closed,
                context.Database.GetDbConnection()
                    .State);
        }

        Assert.Empty(context.ChangeTracker.Entries());
    }
}
