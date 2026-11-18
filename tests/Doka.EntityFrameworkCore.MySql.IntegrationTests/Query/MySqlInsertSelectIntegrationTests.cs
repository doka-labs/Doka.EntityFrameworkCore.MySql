namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Qualifies typed server-side insertion with native persisted values on all six supported pinned profiles.</summary>
[IntegrationDatabaseTest]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlInsertSelectIntegrationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains diagnostic counts without imposing hardware-dependent performance gates.</summary>
    public MySqlInsertSelectIntegrationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Reports every selected supported profile independently.</summary>
    public static IEnumerable<TheoryDataRow<string>> SelectedTargets => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => new TheoryDataRow<string>(target.ToString()));

    /// <summary>Separates empty and populated parameterized selections.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> EmptyCases => SelectedTargets.SelectMany(target => new[]
    {
        new TheoryDataRow<string, bool>(target.Data, false), new TheoryDataRow<string, bool>(target.Data, true),
    });

    /// <summary>Filters and escaped identifiers stay parameterized with one nonquery and no source reader.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EmptyCases))]
    public async Task Filtered_or_empty_source_uses_one_parameterized_statement(
        string target,
        bool empty
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        var threshold = empty
            ? 500
            : 15;

        var suffix = "'\\\"\0\u00e9\ud83d\ude00";

        // Act
        var count = await context
            .Set<InsertSource>()
            .Where(row => row.Value > threshold)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Code, source => source.Label + suffix)
                    .SetProperty(row => row.Value, source => source.Value),
                safety.Token);

        var rows = await ReadNativeAsync(store.Observer,
            $"SELECT `Value`,HEX(`order`) FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(empty
                ? 0
                : 2,
            count);

        Assert.Equal(empty
                ? 0
                : 2,
            rows.Length);

        if (!empty)
        {
            Assert.Equal([
                    "20|" + Convert.ToHexString(Encoding.UTF8.GetBytes("beta" + suffix)),
                    "30|" + Convert.ToHexString(Encoding.UTF8.GetBytes("gamma" + suffix)),
                ],
                rows);
        }

        var sql = Assert.Single(commands.Statements);
        Assert.Contains("INSERT INTO `Insert select target`", sql, StringComparison.Ordinal);
        Assert.Contains("`order`", sql, StringComparison.Ordinal);
        Assert.Contains("`quoted `` source`", sql, StringComparison.Ordinal);
        Assert.Contains(commands.Parameters.Single(), value => Equals(value, suffix));
        Assert.DoesNotContain(suffix, sql, StringComparison.Ordinal);
        Assert.Equal(0, commands.Readers);
        Assert.Equal(safety.Token, Assert.Single(commands.Tokens));
    }

    /// <summary>Ordinary SaveChanges retains normal generated-value readback after a direct insert.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Direct_insert_does_not_attach_rows_or_change_tracked_original_values(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var trackedSource = await context
            .Set<InsertSource>()
            .SingleAsync(row => row.Id == 1, safety.Token);

        var trackedTarget = new InsertTarget
        {
            Code = "tracked",
            Value = 7,
        };

        context.Add(trackedTarget);
        await context.SaveChangesAsync(safety.Token);
        var originalId = trackedTarget.Id;
        var originalValue = context
            .Entry(trackedSource)
            .Property(row => row.Value)
            .OriginalValue;

        // Act
        var count = await context
            .Set<InsertSource>()
            .Where(row => row.Id == 1)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Code, source => source.Label)
                    .SetProperty(row => row.Value, source => source.Value),
                safety.Token);

        var subsequentSave = await context.SaveChangesAsync(safety.Token);
        var native = await ReadNativeAsync(store.Observer,
            $"SELECT `Value`,`Computed` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(1, count);
        Assert.Equal(0, subsequentSave);
        Assert.Equal(2,
            context
                .ChangeTracker
                .Entries()
                .Count());

        Assert.Equal(originalId, trackedTarget.Id);
        Assert.Equal(14, trackedTarget.Computed);
        Assert.Equal(originalValue,
            context
                .Entry(trackedSource)
                .Property(row => row.Value)
                .OriginalValue);

        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.Equal(["7|14", "10|20"], native);
    }
}
