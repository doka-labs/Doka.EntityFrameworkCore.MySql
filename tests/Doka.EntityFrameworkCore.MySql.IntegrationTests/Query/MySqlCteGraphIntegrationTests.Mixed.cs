namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>CTE-P22/N13 keep a volatile shared-value fence distinct from ordinary binding and real recursion in one statement.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Ordinary_recursive_and_materialized_sources_preserve_their_distinct_native_contracts(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var commands = new GraphCommands();
        await using var context = store.CreateContext(commands);
        var source = context
            .Vertices
            .Where(row => row.Id <= 2)
            .Select(row => row.Id);

        // Act
        var actual = await source
            .WithCte(ordinary => ordinary
                .Where(id => id == 1)
                .WithRecursiveCte(
                    frontier => frontier.Select(id => id + 1),
                    recursive => ordinary
                        .Concat(ordinary)
                        .Select(id => new
                        {
                            Id = id,
                            Token = Guid.NewGuid(),
                        })
                        .WithMaterializedSource(shared =>
                            shared
                                .Join(shared,
                                    left => left.Token,
                                    right => right.Token,
                                    (left, right) => new
                                    {
                                        left.Id,
                                        First = left.Token,
                                        Second = right.Token,
                                    })
                                .Join(recursive, pair => pair.Id, id => id, (pair, _) => pair)),
                    1))
            .OrderBy(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([1, 1, 2, 2], actual.Select(row => row.Id));
        Assert.All(actual,
            row =>
            {
                Assert.NotEqual(Guid.Empty, row.First);
                Assert.Equal(row.First, row.Second);
            });

        var sql = Assert.Single(commands.Reads)
            .Sql;

        Assert.Equal(1, CountOrdinaryDefinitions(sql));
        Assert.Equal(1, Regex.Count(sql, @"`__doka_recursive_\d+(?:_\d+)?` \("));
        Assert.Equal(1, Regex.Count(sql, @"`__doka_source_\d+(?:_\d+)?` \("));
        Assert.Equal(1, Regex.Count(sql, "WHERE 1 = 0"));
    }
}
