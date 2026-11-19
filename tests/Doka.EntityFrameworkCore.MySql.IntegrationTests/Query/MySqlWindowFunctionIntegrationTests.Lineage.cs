namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Navigation windows retain nullable GUID representations, temporal precision and converted partition keys.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Window_values_preserve_guid_temporal_and_converter_lineage(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        var first = new Guid("00112233-4455-6677-8899-000000000001");
        var fourth = new Guid("00112233-4455-6677-8899-000000000004");
        var timestamp = new DateTime(2026, 1, 1);
        var query = context
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => new
            {
                Text = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.ConvertedGroup)
                    .OrderBy(row.Id)
                    .Lag(row.CharGuid),
                Binary = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.ConvertedGroup)
                    .OrderBy(row.Id)
                    .Lag(row.BinaryGuid),
                Native = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.ConvertedGroup)
                    .OrderBy(row.Id)
                    .Lag(row.NativeGuid),
                Time = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.ConvertedGroup)
                    .OrderBy(row.Id)
                    .Lag(row.Timestamp),
                Position = EF
                    .Functions
                    .Window()
                    .PartitionBy(row.ConvertedGroup)
                    .OrderBy(row.Id)
                    .RowNumber(),
            });

        // Act
        var rows = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([null, first, null, null, fourth, null], rows.Select(row => row.Text));
        Assert.Equal([null, first, null, null, fourth, null], rows.Select(row => row.Binary));
        Assert.Equal([null, first, null, null, fourth, null], rows.Select(row => row.Native));
        Assert.Equal([null, timestamp.AddTicks(10), timestamp.AddTicks(20), null, timestamp.AddTicks(40), null],
            rows.Select(row => row.Time));

        Assert.Equal([1L, 2L, 3L, 1L, 2L, 1L], rows.Select(row => row.Position));
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
