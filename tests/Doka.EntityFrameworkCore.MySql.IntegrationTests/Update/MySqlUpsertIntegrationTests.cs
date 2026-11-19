namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Qualifies native direct UPSERT mapping and transaction contracts on all pinned supported engines.</summary>
[IntegrationDatabaseTest]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlUpsertIntegrationTests
{
    private static readonly string[] s_concurrentNames = { "first", "second" };
    private static readonly string[] s_generatedGuidNames = { "one", "two", "three" };
    private static readonly string[] s_repeatedServerDefaults = { "server-default", "server-default" };
    private static readonly int[] s_originalIds = { 1, 2 };
    private static readonly string[] s_tenantNames = { "updated-seven", "tenant-eight" };
    private static readonly int[] s_priorWorkIds = { 5000 };
    private static readonly int[] s_originalAndPriorWorkIds = { 1, 2, 5000 };

    /// <summary>Reports the selected pinned supported target profiles.</summary>
    public static IEnumerable<TheoryDataRow<string>> SelectedTargets => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => new TheoryDataRow<string>(target.ToString()));

    /// <summary>Reports independent model representation variants.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> MappingCases => SelectedTargets.SelectMany(target => new[]
    {
        new TheoryDataRow<string, string>(target.Data, "ordinary"),
        new TheoryDataRow<string, string>(target.Data, "char-guid"),
        new TheoryDataRow<string, string>(target.Data, "converted-guid"),
        new TheoryDataRow<string, string>(target.Data, "quoted"),
    });

    /// <summary>Reports native UUID mapping only on engines that actually supply the type.</summary>
    public static IEnumerable<TheoryDataRow<string>> UuidTargets => SelectedTargets.Where(target =>
        IntegrationTestEnvironment
            .GetServerVersion(Enum.Parse<IntegrationDatabaseTarget>(target.Data))
            .Profile
            .Engine
            .Has(EngineCapability.NativeUuidType));

    /// <summary>One native insert preserves mapped values without attaching or mutating the input.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(MappingCases))]
    public async Task Insert_preserves_mapped_values_and_keeps_input_detached(
        string target,
        string mode
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, mode, CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var input = NewRow(1);

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
        Assert.Equal(input.Id, persisted.Id);
        Assert.Equal(input.Name, persisted.Name);
        Assert.Equal(input.Amount, persisted.Amount);
        Assert.Equal(input.Timestamp, persisted.Timestamp);
        Assert.Equal(input.Payload, persisted.Payload);
        Assert.Equal(input.GuidValue, persisted.GuidValue);
        Assert.Equal(input.NullableGuid, persisted.NullableGuid);
        Assert.Equal(input.Status, persisted.Status);
        Assert.Equal(input.Number + 1, persisted.Computed);
        Assert.Equal("server-default", persisted.ServerText);
        Assert.Null(input.ServerText);
        Assert.Equal(0, input.Computed);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Single(capture.Statements);
        Assert.Single(capture.AllStatements);
    }

    /// <summary>A native primary-key conflict updates only the explicitly selected properties.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Primary_key_conflict_preserves_unselected_columns(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        context.Add(NewRow(1, "original"));
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1, "updated");
        input.Unchanged = "must-not-replace";
        input.Number = 99;

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
        Assert.Equal("updated", persisted.Name);
        Assert.Equal("preserved", persisted.Unchanged);
        Assert.Equal(1, persisted.Number);
        Assert.Equal(2, persisted.Computed);
    }

    /// <summary>CLIENT_FOUND_ROWS preserves the qualified native no-op count rather than an invented insert/update classification.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Unchanged_update_preserves_native_found_rows_accounting(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        context.Add(NewRow(1));
        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var affected =
            await context.ExecuteUpsertAsync(NewRow(1), new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var count = await reader.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, affected);
        Assert.Equal(1, count);
    }

    /// <summary>An alternate unique-key conflict keeps the stored primary key and updates the explicit selection.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Alternate_unique_conflict_keeps_the_existing_primary_key(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        context.Add(NewRow(1, "original"));
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(2, "replacement");
        input.UniqueKey = "KEY-1 ";

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
        Assert.Equal("key-1", persisted.UniqueKey);
        Assert.Equal(2, input.Id);
    }

    /// <summary>Computed values are produced by the server after an explicit update of their input.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Computed_column_is_not_written_and_reflects_the_updated_input(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        context.Add(NewRow(1));
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1);
        input.Number = 40;
        input.Computed = -999;

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Number) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal(40, persisted.Number);
        Assert.Equal(41, persisted.Computed);
        Assert.Equal(-999, input.Computed);
    }

    /// <summary>An omitted default selected for update references the native candidate's evaluated default.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Selected_default_update_uses_the_evaluated_native_candidate(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        var existing = NewRow(1, "original");
        existing.ServerText = "old-default";
        context.Add(existing);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1, "not-selected");

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.ServerText) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal("server-default", persisted.ServerText);
        Assert.Equal("original", persisted.Name);
        Assert.Null(input.ServerText);
    }

    /// <summary>EF's field-only and private-setter accessors provide real input values instead of invented DBNull.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Field_only_and_private_setter_properties_use_their_mapped_accessors(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        context.Add(NewRow(1));
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1);
        input.SetFields("field 'value' \\ \u4e2d");

        // Act
        var affected = await context.ExecuteUpsertAsync(input,
            new[] { "_onlyField", nameof(UpsertRow.PrivateValue) },
            CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal(input.FieldValue, persisted.FieldValue);
        Assert.Equal(input.PrivateValue, persisted.PrivateValue);
    }

    /// <summary>Native UUID parameters retain exact GUID identity and nullable values.</summary>

    // WHY: MySQL-only selections have no native UUID data; eligible MariaDB
    // selections must still fail if their capability-filtered data disappears.
    [RequiresDatabaseTargetsTheory([IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123])]
    [MemberData(nameof(UuidTargets))]
    public async Task Native_uuid_mapping_preserves_guid_values(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "uuid", CancellationToken.None);
        await using var context = store.CreateContext();
        var input = NewRow(1);
        input.NullableGuid = null;

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.GuidValue) }, CancellationToken.None);

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, affected);
        Assert.Equal(input.GuidValue, persisted.GuidValue);
        Assert.Null(persisted.NullableGuid);
    }
}
