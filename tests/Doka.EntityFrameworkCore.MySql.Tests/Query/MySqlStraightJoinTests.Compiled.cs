namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlStraightJoinTests
{
    /// <summary>Compiled synchronous lambdas use the same early public-operator lowering as ordinary queries.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compiled_query_retains_the_public_join_operator(bool mariaDb)
    {
        // Arrange
        var commands = new List<string>();
        using var services = CreateRecordingServices(commands);
        using var context = CreateContext(mariaDb, services: services);
        var query = EF.CompileQuery((JoinContext db, int minimum) => db.Nodes.Where(value => value.Id >= minimum)
            .StraightJoin(db.Nodes, left => left.Id, right => right.Id, (left, right) => new { left.Id, Child = right.Id }));

        Action act = () => _ = query(context, 17).ToArray();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var error = Assert.IsType<ConnectionBlockedException>(recordedException);
        Assert.NotNull(error);
        Assert.Equal(1, context.Blocker.Openings);
        Assert.Single(Regex.Matches(Assert.Single(commands), @"\bSTRAIGHT_JOIN\b"));
    }

    /// <summary>Compiled asynchronous lambdas translate without executing the public operator body.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compiled_async_query_retains_the_public_join_operator(bool mariaDb)
    {
        // Arrange
        var commands = new List<string>();
        await using var services = CreateRecordingServices(commands);
        await using var context = CreateContext(mariaDb, services: services);
        var query = EF.CompileAsyncQuery((JoinContext db, int minimum) => db.Nodes.Where(value => value.Id >= minimum)
            .StraightJoin(db.Nodes, left => left.Id, right => right.Id, (left, right) => new { left.Id, Child = right.Id }));

        Func<Task> act = async () =>
        {
            await foreach (var unused in query(context, 17).WithCancellation(CancellationToken.None))
            {
                _ = unused;
            }
        };

        // Act
        var recordedException = await Record.ExceptionAsync(act);

        // Assert
        var error = Assert.IsType<ConnectionBlockedException>(recordedException);
        Assert.NotNull(error);
        Assert.Equal(1, context.Blocker.Openings);
        Assert.Single(Regex.Matches(Assert.Single(commands), @"\bSTRAIGHT_JOIN\b"));
    }

    /// <summary>Dependent completed sources and a directed edge share their original lexical source definitions.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Directed_join_composes_with_dependent_materialized_sources(bool mariaDb)
    {
        // Arrange
        await using var context = CreateContext(mariaDb);
        var source = context.Nodes.Select(value => new { value.Id, value.TenantId, value.OptionalKey });
        var query = source.WithMaterializedSource(selected =>
            context.Nodes.Where(owner => selected.Any(value => value.TenantId == owner.TenantId))
                .Select(owner => new { owner.Id, owner.TenantId, owner.OptionalKey })
                .WithMaterializedSource(candidates => selected.StraightJoin(candidates,
                        left => new { left.TenantId, left.OptionalKey }, right => new { right.TenantId, right.OptionalKey },
                        (left, right) => new { SourceId = left.Id, OwnerId = right.Id, left.TenantId })
                    .Join(context.Nodes, pair => new { pair.TenantId, Id = pair.OwnerId },
                        owner => new { owner.TenantId, owner.Id }, (pair, owner) => new
                        {
                            pair.SourceId,
                            owner.Id,
                            owner.Details.Text,
                            Tags = owner.Tags.Select(tag => tag.Text).ToArray(),
                            Items = owner.Items.Select(item => item.Text).ToArray(),
                        })));

        var sql = query.ToQueryString();
        Func<Task> act = () => query.ToArrayAsync(CancellationToken.None);

        // Act
        var recordedException = await Record.ExceptionAsync(act);

        // Assert
        var error = Assert.IsType<ConnectionBlockedException>(recordedException);
        Assert.NotNull(error);
        Assert.Single(Regex.Matches(sql, @"\bSTRAIGHT_JOIN\b"));
        Assert.Contains("INNER JOIN", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE 1 = 0", sql, StringComparison.Ordinal);
        Assert.Equal(1, context.Blocker.Openings);
    }
}
