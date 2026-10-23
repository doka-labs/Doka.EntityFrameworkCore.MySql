namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>Reads secondary SQL-default GUIDs without replacing explicitly supplied application values.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultMappingCases))]
    public async Task Entity_split_guid_sql_defaults_are_read_back_and_supplied_values_are_preserved(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsSplitValueMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var rows = Enumerable.Range(1, 8)
            .Select(id => new GuidSplitGeneratedRow { Id = id, Input = id, Payload = $"row-{id}" })
            .Reverse()
            .ToArray();

        var supplied = new GuidSplitGeneratedRow
        {
            Id = 100,
            Input = 100,
            Payload = "supplied",
            RequiredValue = CreateGuid(101),
            NullableValue = CreateGuid(102),
        };

        context.Set<GuidSplitGeneratedRow>().AddRange(rows);
        context.Add(supplied);
        capture.Enabled = true;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        capture.Enabled = false;
        var persisted = await context.Set<GuidSplitGeneratedRow>().AsNoTracking()
            .ToDictionaryAsync(row => row.Id, CancellationToken.None);

        // Assert
        Assert.Equal(9, persisted.Count);
        Assert.Equal(8, rows.Select(row => row.RequiredValue).Distinct().Count());
        Assert.Equal(8, rows.Select(row => row.NullableValue).Distinct().Count());
        Assert.All(rows.Append(supplied), row =>
        {
            Assert.NotEqual(Guid.Empty, row.RequiredValue);
            Assert.NotNull(row.NullableValue);
            Assert.NotEqual(Guid.Empty, row.NullableValue);
            Assert.Equal(persisted[row.Id].RequiredValue, row.RequiredValue);
            Assert.Equal(persisted[row.Id].NullableValue, row.NullableValue);
            Assert.Equal(persisted[row.Id].Payload, row.Payload);
        });
        Assert.Equal(CreateGuid(101), supplied.RequiredValue);
        Assert.Equal(CreateGuid(102), supplied.NullableValue);
        Assert.Contains(GuidDefaultInsertHeaders(capture.Commands, GuidSplitGeneratedSecondaryTable), header =>
            !header.Contains("`RequiredValue`", StringComparison.Ordinal)
            && !header.Contains("`NullableValue`", StringComparison.Ordinal));
        AssertSplitGuidGeneratedMetadata(context, ValueGenerated.OnAdd);
    }

    /// <summary>Reads required and nullable computed GUIDs from their secondary fragment after insertion.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultMappingCases))]
    public async Task Entity_split_guid_computed_values_are_read_back_on_insert(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsSplitComputedMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var rows = Enumerable.Range(1, 8)
            .Select(id => new GuidSplitGeneratedRow { Id = id, Input = id, Payload = $"row-{id}" })
            .Reverse()
            .ToArray();

        context.Set<GuidSplitGeneratedRow>().AddRange(rows);
        capture.Enabled = true;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        capture.Enabled = false;
        var persisted = await context.Set<GuidSplitGeneratedRow>().AsNoTracking()
            .ToDictionaryAsync(row => row.Id, CancellationToken.None);

        // Assert
        Assert.Equal(8, persisted.Count);
        AssertSplitGuidGeneratedMetadata(context, ValueGenerated.OnAddOrUpdate);
        Assert.All(rows, row =>
        {
            AssertSplitGuidComputedValues(row);
            Assert.Equal(row.RequiredValue, persisted[row.Id].RequiredValue);
            Assert.Equal(row.NullableValue, persisted[row.Id].NullableValue);
            Assert.Equal(row.Payload, persisted[row.Id].Payload);
        });
        Assert.NotEmpty(GuidDefaultInsertHeaders(capture.Commands, GuidSplitGeneratedSecondaryTable));
        Assert.All(GuidDefaultInsertHeaders(capture.Commands, GuidSplitGeneratedSecondaryTable), header =>
        {
            Assert.DoesNotContain("`RequiredValue`", header, StringComparison.Ordinal);
            Assert.DoesNotContain("`NullableValue`", header, StringComparison.Ordinal);
        });
    }

    /// <summary>Refreshes secondary computed GUIDs on update, including both nullable transitions.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultMappingCases))]
    public async Task Entity_split_guid_computed_values_are_refreshed_on_update(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsSplitComputedMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var rows = Enumerable.Range(1, 8)
            .Select(id => new GuidSplitGeneratedRow { Id = id, Input = id, Payload = $"row-{id}" })
            .Reverse()
            .ToArray();

        context.Set<GuidSplitGeneratedRow>().AddRange(rows);
        await context.SaveChangesAsync(CancellationToken.None);
        var previous = rows.ToDictionary(row => row.Id, row => row.RequiredValue);

        foreach (var row in rows)
        {
            // WHY: An odd increment changes the UUID and flips nullable output
            // in both directions, exposing stale insert-only generation metadata.
            row.Input += 101;
        }

        capture.Enabled = true;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        capture.Enabled = false;
        var persisted = await context.Set<GuidSplitGeneratedRow>().AsNoTracking()
            .ToDictionaryAsync(row => row.Id, CancellationToken.None);

        // Assert
        Assert.Equal(8, persisted.Count);
        AssertSplitGuidGeneratedMetadata(context, ValueGenerated.OnAddOrUpdate);
        Assert.All(rows, row =>
        {
            AssertSplitGuidComputedValues(row);
            Assert.NotEqual(previous[row.Id], row.RequiredValue);
            Assert.Equal(row.RequiredValue, persisted[row.Id].RequiredValue);
            Assert.Equal(row.NullableValue, persisted[row.Id].NullableValue);
            Assert.Equal(row.Payload, persisted[row.Id].Payload);
        });
        Assert.Equal(8, capture.Commands.Sum(sql => CountOccurrences(sql, $"UPDATE `{GuidSplitGeneratedSecondaryTable}`")));
        Assert.DoesNotContain(capture.Commands, sql =>
            sql.Contains($"UPDATE `{GuidSplitGeneratedPrincipalTable}`", StringComparison.Ordinal));
    }

    private static void AssertSplitGuidGeneratedMetadata(
        DbContext context,
        ValueGenerated valueGenerated
    )
    {
        var entity = context.Model.FindEntityType(typeof(GuidSplitGeneratedRow))!;
        var primary = StoreObjectIdentifier.Table(GuidSplitGeneratedPrincipalTable, null);
        var secondary = StoreObjectIdentifier.Table(GuidSplitGeneratedSecondaryTable, null);
        var required = entity.FindProperty(nameof(GuidSplitGeneratedRow.RequiredValue))!;
        var nullable = entity.FindProperty(nameof(GuidSplitGeneratedRow.NullableValue))!;

        Assert.Equal(valueGenerated, required.ValueGenerated);
        Assert.Equal(valueGenerated, nullable.ValueGenerated);
        Assert.Null(required.GetColumnName(primary));
        Assert.Null(nullable.GetColumnName(primary));
        Assert.Equal(nameof(GuidSplitGeneratedRow.RequiredValue), required.GetColumnName(secondary));
        Assert.Equal(nameof(GuidSplitGeneratedRow.NullableValue), nullable.GetColumnName(secondary));
        Assert.False(required.IsNullable);
        Assert.True(nullable.IsNullable);
    }

    private static void AssertSplitGuidComputedValues(
        GuidSplitGeneratedRow row
    )
    {
        Assert.Equal(ExpectedComputedGuid(row.Input), row.RequiredValue);
        Assert.Equal(row.Input % 2 == 0 ? null : ExpectedComputedGuid(row.Input), row.NullableValue);
    }
}
