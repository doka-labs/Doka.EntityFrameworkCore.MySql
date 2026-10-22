namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>Reads deterministic required and nullable computed GUIDs for each known-key insert.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultMappingCases))]
    public async Task Guid_computed_values_are_read_back_on_insert(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsComputedMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var rows = Enumerable.Range(1, 24)
            .Select(id => new GuidComputedValueRow { Id = id, Input = (id * 7) + 3 })
            .Reverse()
            .ToArray();

        context.AddRange(rows);
        capture.Enabled = true;

        // Act
        var written = await context.SaveChangesAsync(CancellationToken.None);

        capture.Enabled = false;
        var persisted = await context.Set<GuidComputedValueRow>().AsNoTracking()
            .ToDictionaryAsync(row => row.Id, CancellationToken.None);

        // Assert
        Assert.Equal(24, written);
        Assert.Equal(24, persisted.Count);
        AssertGuidComputedMetadata(context);
        Assert.All(rows, row =>
        {
            AssertGuidComputedValues(row);
            Assert.Equal(row.RequiredValue, persisted[row.Id].RequiredValue);
            Assert.Equal(row.NullableValue, persisted[row.Id].NullableValue);
        });
        Assert.NotEmpty(GuidDefaultInsertHeaders(capture.Commands, GuidComputedValueTable));
        Assert.All(GuidDefaultInsertHeaders(capture.Commands, GuidComputedValueTable), header =>
        {
            Assert.DoesNotContain("`RequiredValue`", header, StringComparison.Ordinal);
            Assert.DoesNotContain("`NullableValue`", header, StringComparison.Ordinal);
        });
    }

    /// <summary>Refreshes computed GUIDs and nullability when a known input changes.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultMappingCases))]
    public async Task Guid_computed_values_are_refreshed_on_update(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsComputedMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var rows = Enumerable.Range(1, 8)
            .Select(id => new GuidComputedValueRow { Id = id, Input = id })
            .Reverse()
            .ToArray();

        context.AddRange(rows);
        await context.SaveChangesAsync(CancellationToken.None);
        var previous = rows.ToDictionary(row => row.Id, row => row.RequiredValue);

        foreach (var row in rows)
        {
            // WHY: An odd offset changes both the generated UUID and which
            // nullable outputs are NULL, detecting stale insert-only readback.
            row.Input += 101;
        }

        capture.Enabled = true;

        // Act
        var written = await context.SaveChangesAsync(CancellationToken.None);

        capture.Enabled = false;
        var persisted = await context.Set<GuidComputedValueRow>().AsNoTracking()
            .ToDictionaryAsync(row => row.Id, CancellationToken.None);

        // Assert
        Assert.Equal(8, written);
        Assert.Equal(8, persisted.Count);
        AssertGuidComputedMetadata(context);
        Assert.All(rows, row =>
        {
            AssertGuidComputedValues(row);
            Assert.NotEqual(previous[row.Id], row.RequiredValue);
            Assert.Equal(row.RequiredValue, persisted[row.Id].RequiredValue);
            Assert.Equal(row.NullableValue, persisted[row.Id].NullableValue);
        });
        Assert.Equal(8, capture.Commands.Sum(sql => CountOccurrences(sql, "UPDATE ")));
    }

    private static void AssertGuidComputedMetadata(
        DbContext context
    )
    {
        var entity = context.Model.FindEntityType(typeof(GuidComputedValueRow))!;
        var required = entity.FindProperty(nameof(GuidComputedValueRow.RequiredValue))!;
        var nullable = entity.FindProperty(nameof(GuidComputedValueRow.NullableValue))!;

        Assert.Equal(ValueGenerated.OnAddOrUpdate, required.ValueGenerated);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, nullable.ValueGenerated);
        Assert.False(required.IsNullable);
        Assert.True(nullable.IsNullable);
    }

    private static void AssertGuidComputedValues(
        GuidComputedValueRow row
    )
    {
        Assert.Equal(ExpectedComputedGuid(row.Input), row.RequiredValue);
        Assert.Equal(row.Input % 2 == 0 ? (Guid?)null : ExpectedComputedGuid(row.Input), row.NullableValue);
    }
}
