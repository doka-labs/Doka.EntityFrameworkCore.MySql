namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>Reports separate escaping and nullable-value variants for every selected engine.</summary>
    public static TheoryData<string, string> TextCases
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, string.Empty);
                data.Add(target, "Hallo 'Test' \"quoted\" \\ folder\\file");
                data.Add(target, "line1\r\nline2\n\t\0end");
                data.Add(target, "\u00e4\u4e2d\ud83d\ude42; DROP TABLE UpsertRows; --");
            }

            return data;
        }
    }

    /// <summary>Supplied runtime metadata preserves actual native conversions, defaults and computed values without rebuilding.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Supplied_runtime_model_executes_the_native_write_with_the_same_mapping_contract(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "converted-guid", CancellationToken.None);
        await using var original = store.CreateContext();
        var model = original.Model;
        var capture = new Writes();
        await using var context = store.CreatePreparedModelContext(model, capture);
        var input = NewRow(1, "supplied-runtime-model");

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        var persisted = await context
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Same(model, context.Model);
        Assert.Equal(0, context.ModelBuilds);
        Assert.Equal(1, affected);
        Assert.Equal(input.GuidValue, persisted.GuidValue);
        Assert.Equal(input.NullableGuid, persisted.NullableGuid);
        Assert.Equal(input.Payload, persisted.Payload);
        Assert.Equal("server-default", persisted.ServerText);
        Assert.Equal(input.Number + 1, persisted.Computed);
        Assert.Null(input.ServerText);
        Assert.Equal(0, input.Computed);
        Assert.Single(capture.Statements);
    }

    /// <summary>Text escapes remain values rather than SQL, including null characters and supplementary Unicode.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TextCases))]
    public async Task Text_payload_round_trips_without_quote_or_encoding_changes(
        string target,
        string text
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var input = NewRow(1, text);
        input.UniqueKey = null;
        input.NullableGuid = null;

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, affected);
        Assert.Equal(text, persisted.Name);
        Assert.Null(persisted.UniqueKey);
        Assert.Null(persisted.NullableGuid);
        var sql = Assert.Single(capture.Statements);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.Ordinal);
    }

    /// <summary>Multiple null unique-key values remain independent native candidates without provider deduplication.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Null_unique_keys_are_not_deduplicated_by_the_provider(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        var rows = new[] { NewRow(1), NewRow(2) };
        rows[0].UniqueKey = null;
        rows[1].UniqueKey = null;

        // Act
        var affected =
            await context.ExecuteUpsertRangeAsync(rows, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var ids = await reader
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal(s_originalIds, ids);
    }

    /// <summary>The physical prefix unique index, rather than CLR string equality, selects the existing row.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Prefix_unique_index_uses_native_conflict_equality(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "prefix", CancellationToken.None);
        await using var context = store.CreateContext();
        var existing = NewRow(1, "original");
        existing.UniqueKey = "abcdefgh-original";
        context.Add(existing);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(2, "replacement");
        input.UniqueKey = "abcdefgh-other";

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal(1, persisted.Id);
        Assert.Equal("replacement", persisted.Name);
        Assert.Equal("abcdefgh-original", persisted.UniqueKey);
    }

    /// <summary>Tenant identity in every reachable conflict key allows the same logical key in two tenants.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Tenant_qualified_primary_and_unique_keys_preserve_both_tenants(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "composite-safe", CancellationToken.None);
        await using var context = store.CreateContext();
        var first = NewRow(1, "tenant-seven");
        var second = NewRow(1, "tenant-eight");
        second.TenantId = 8;
        context.AddRange(first, second);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1, "updated-seven");

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var names = await reader
            .Rows
            .OrderBy(row => row.TenantId)
            .Select(row => row.Name)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal(s_tenantNames, names);
    }

    /// <summary>A target query filter is not a conflict predicate and cannot secure a tenantless physical key.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Query_filter_does_not_authorize_the_native_unique_conflict(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "filter", CancellationToken.None);
        await using var context = store.CreateContext();
        var existing = NewRow(1, "hidden-tenant-eight");
        existing.TenantId = 8;
        context.Add(existing);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(2, "native-conflict");
        input.UniqueKey = "key-1";

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal(8, persisted.TenantId);
        Assert.Equal("native-conflict", persisted.Name);
    }

    /// <summary>Tracked inputs retain current/original snapshots and their existing state after the direct write.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Tracked_input_preserves_its_current_original_and_state_snapshots(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        var input = NewRow(1, "original");
        context.Add(input);
        await context.SaveChangesAsync(CancellationToken.None);
        input.Name = "current";
        var entry = context.Entry(input);
        entry.State = EntityState.Modified;
        var originalName = entry.OriginalValues.GetValue<string>(nameof(UpsertRow.Name));

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var persistedName = await reader
            .Rows
            .Select(row => row.Name)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal("current", persistedName);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal("current", input.Name);
        Assert.Equal(originalName, entry.OriginalValues.GetValue<string>(nameof(UpsertRow.Name)));
    }

    /// <summary>Fourteen thousand fifty-column rows retain complete values across bounded native commands.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Wide_fourteen_thousand_row_range_preserves_complete_results_in_bounded_commands(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "wide", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var rows = Enumerable
            .Range(1, 14_000)
            .Select(id =>
            {
                var row = NewRow(id);
                row.C01 = id;
                row.C16 = -id;
                row.C32 = checked(id * 3);

                return row;
            })
            .ToArray();

        var properties = context.Model.FindEntityType(typeof(UpsertRow))!
            .GetProperties()
            .ToArray();

        // Act
        var affected =
            await context.ExecuteUpsertRangeAsync(rows, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(14_000, affected);
        Assert.Equal(rows.Length, persisted.Length);
        Assert.Equal(50, properties.Length);
        Assert.Equal(14, capture.Statements.Count);
        Assert.Equal(14, capture.AllStatements.Count);
        Assert.All(capture.ParameterCounts,
            count => Assert.InRange(count, 1, MySqlUpsertCommandBuilder.MaximumParameters));

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var expected = rows[rowIndex];
            var actual = persisted[rowIndex];
            Assert.Equal(expected.Number + 1, actual.Computed);
            Assert.Equal("server-default", actual.ServerText);

            foreach (var property in properties)
            {
                if (property.Name is nameof(UpsertRow.Computed)
                    or nameof(UpsertRow.ServerText)
                    or nameof(UpsertRow.Json))
                {
                    continue;
                }

                var getter = property.GetGetter();
                Assert.True(property
                        .GetValueComparer()
                        .Equals(getter.GetClrValueUsingContainingEntity(expected),
                            getter.GetClrValueUsingContainingEntity(actual)),
                    property.Name);
            }
        }

        Assert.Empty(context.ChangeTracker.Entries());
    }
}
