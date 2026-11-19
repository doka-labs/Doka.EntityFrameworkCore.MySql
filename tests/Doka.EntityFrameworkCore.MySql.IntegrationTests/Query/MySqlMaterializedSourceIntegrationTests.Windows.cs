namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    /// <summary>Temporal source instants survive window projection and preserve historical navigated values.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(MySqlWindowFunctionIntegrationTests.WindowFlagTargets),
        MemberType = typeof(MySqlWindowFunctionIntegrationTests))]
    public async Task Temporal_source_window_preserves_historical_rows_and_navigation(
        string target,
        bool materialized
    )
    {
        // Arrange
        using var safety = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var fixture =
            await CreateTemporalLineageFixtureAsync(Enum.Parse<IntegrationDatabaseTarget>(target), safety.Token);

        var source = fixture.Context.Parents.TemporalAsOf(fixture.Instant);
        var query = materialized
            ? source.WithMaterializedSource(shared => shared.Select(parent => new
            {
                parent.Id,
                ChildId = parent.Child.Id,
                ChildName = parent.Child.Name,
                Position = EF
                    .Functions
                    .Window()
                    .OrderBy(parent.Id)
                    .RowNumber(),
            }))
            : source.Select(parent => new
            {
                parent.Id,
                ChildId = parent.Child.Id,
                ChildName = parent.Child.Name,
                Position = EF
                    .Functions
                    .Window()
                    .OrderBy(parent.Id)
                    .RowNumber(),
            });

        // Act
        var row = await query.SingleAsync(safety.Token);

        // Assert
        Assert.Equal(1, row.Id);
        Assert.Equal(1, row.ChildId);
        Assert.Equal("historical-child", row.ChildName);
        Assert.Equal(1L, row.Position);
        Assert.Equal(1, fixture.Probe.Count);
        Assert.Equal(safety.Token, fixture.Probe.Token);
    }
}
