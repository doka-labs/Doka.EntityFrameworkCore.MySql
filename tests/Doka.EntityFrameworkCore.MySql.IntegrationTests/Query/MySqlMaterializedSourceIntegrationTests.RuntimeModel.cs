namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    /// <summary>Verifies source metadata and navigation shaping with a supplied runtime model.</summary>
    private static async Task AssertSuppliedRuntimeModelPreservesSourceMetadataAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var fixture = await CreateFixtureAsync(target);
        var probe = new BindingCommandProbe();
        var connectionString = fixture.Context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The populated fixture must provide a connection string.");

        // WHY: Calling the target connection helper from this new file would select
        // another isolated schema instead of the fixture's populated database.
        var options = IntegrationTestDbContextOptions.Create<BindingContext>()
            .UseMySql(
                connectionString,
                IntegrationTestEnvironment.GetServerVersion(target))
            .UseModel(fixture.Context.Model)
            .AddInterceptors(probe)
            .Options;

        await using var context = new BindingContext(options);
        var maximum = 2;
        var query = context.Items.Where(item => item.Id <= maximum)
            .WithMaterializedSource(source => source.Select(item => new RuntimeModelRow
            {
                Id = item.Id,
                Token = item.Token,
                OptionalScore = item.OptionalScore,
                Code = item.Code,
                Children = item.Children.OrderBy(child => child.Id).Select(child => child.Name).ToArray(),
            }))
            .OrderBy(row => row.Id);

        var commandsDuringConstruction = probe.Count;

        var actual = await query.ToArrayAsync(CancellationToken.None);

        Assert.Same(fixture.Context.Model, context.Model);
        Assert.Equal([1, 2], actual.Select(row => row.Id));
        Assert.Equal([s_binaryToken, Guid.Empty], actual.Select(row => row.Token));
        Assert.Equal(new int?[] { null, 2 }, actual.Select(row => row.OptionalScore));
        Assert.Equal(["code-1", "code-2"], actual.Select(row => row.Code.Value));
        Assert.Equal(["child-1", "child-1b"], actual[0].Children);
        Assert.Equal(["child-2"], actual[1].Children);
        Assert.Contains(maximum, probe.ParameterValues);
        Assert.Equal(0, commandsDuringConstruction);
        Assert.Equal(1, probe.Count);
    }

    private sealed class RuntimeModelRow
    {
        public int Id { get; set; }

        public Guid Token { get; set; }

        public int? OptionalScore { get; set; }

        public BoundCode Code { get; set; } = new(string.Empty);

        public string[] Children { get; set; } = [];
    }
}
