namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>Reports generated defaults, explicit nulls and explicit values as separate native cases.</summary>
    public static TheoryData<string, string?, string?> SentinelCases
    {
        get
        {
            var data = new TheoryData<string, string?, string?>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, "missing", "server-default");
                data.Add(target, null, null);
                data.Add(target, "explicit", "explicit");
            }

            return data;
        }
    }

    /// <summary>UUID() is evaluated per native candidate without invoking a client generator or mutating input IDs.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Guid_sql_default_generates_distinct_server_keys_without_input_readback(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "guid-default", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        GuidDefaultRow[] inputs =
        [
            new()
            {
                NaturalKey = "first",
                Name = "one",
            },
            new()
            {
                NaturalKey = "second",
                Name = "two",
            },
            new()
            {
                NaturalKey = "third",
                Name = "three",
            },
        ];

        // Act
        var affected =
            await context.ExecuteUpsertRangeAsync(inputs,
                new[] { nameof(GuidDefaultRow.Name) },
                CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Set<GuidDefaultRow>()
            .AsNoTracking()
            .OrderBy(row => row.NaturalKey)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, affected);
        Assert.Equal(3, persisted.Length);
        Assert.All(persisted, row => Assert.NotEqual(Guid.Empty, row.Id));
        Assert.Equal(3,
            persisted
                .Select(row => row.Id)
                .Distinct()
                .Count());

        Assert.All(inputs, row => Assert.Equal(Guid.Empty, row.Id));
        Assert.Equal(s_generatedGuidNames, persisted.Select(row => row.Name));
        Assert.Single(capture.Statements);
        Assert.Single(capture.AllStatements);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Native DEFAULT and explicit SQL NULL remain different under an EF custom sentinel.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SentinelCases))]
    public async Task Custom_sentinel_preserves_native_default_versus_explicit_null(
        string target,
        string? value,
        string? expected
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "sentinel", CancellationToken.None);
        await using var context = store.CreateContext();
        var existing = NewRow(1);
        existing.ServerText = "old";
        context.Add(existing);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1);
        input.ServerText = value;

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.ServerText) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var actual = await reader
            .Rows
            .Select(row => row.ServerText)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal(expected, actual);
        Assert.Equal(value, input.ServerText);
    }

    /// <summary>A range may mix explicit values and generated DEFAULT cells without changing its entity type or input order.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Mixed_default_cells_preserve_the_native_candidate_update_sequence(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        var first = NewRow(1);
        first.ServerText = "explicit";
        var second = NewRow(2);
        var repeated = NewRow(1);
        UpsertRow[] inputs = [first, second, repeated];

        // Act
        var affected =
            await context.ExecuteUpsertRangeAsync(inputs,
                new[] { nameof(UpsertRow.ServerText) },
                CancellationToken.None);

        await using var reader = store.CreateContext();
        var actual = await reader
            .Rows
            .OrderBy(row => row.Id)
            .Select(row => row.ServerText)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(4, affected);
        Assert.Equal(s_repeatedServerDefaults, actual);
        Assert.Equal("explicit", first.ServerText);
        Assert.Null(second.ServerText);
        Assert.Null(repeated.ServerText);
    }

    /// <summary>The mapped JSON serializer preserves values even when the engine normalizes its stored text spelling.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Json_conflict_update_preserves_quoted_text_nulls_and_array_values(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        var existing = NewRow(1);
        existing.Json = "{}";
        context.Add(existing);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1);

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Json) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var json = await reader
            .Rows
            .Select(row => row.Json)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Hallo 'Test'",
            document
                .RootElement
                .GetProperty("text")
                .GetString());

        var array = document.RootElement.GetProperty("items");
        Assert.Equal(3, array.GetArrayLength());
        Assert.Equal(1,
            array[0]
                .GetInt32());

        Assert.Equal(JsonValueKind.Null, array[1].ValueKind);
        Assert.Equal(3,
            array[2]
                .GetInt32());
    }
}
