namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    private static readonly decimal[] s_decimalBoundaryValues = [99999999999999.9999m, -99999999999999.9999m, 0.0001m];
    private static readonly bool[] s_binaryPayloadVariants = [false, true];

    private static readonly byte[] s_everyByteValue = Enumerable
        .Range(0, 256)
        .Select(value => (byte)value)
        .ToArray();

    private static readonly string[] s_amountUpdate = [nameof(UpsertRow.Amount)];
    private static readonly string[] s_timestampUpdate = [nameof(UpsertRow.Timestamp)];
    private static readonly string[] s_payloadUpdate = [nameof(UpsertRow.Payload)];
    private static readonly string[] s_amountAndNameUpdate = [nameof(UpsertRow.Amount), nameof(UpsertRow.Name)];
    private static readonly string[] s_jsonAndNameUpdate = [nameof(UpsertRow.Json), nameof(UpsertRow.Name)];

    /// <summary>Reports representable decimal boundaries independently for every selected engine.</summary>
    public static TheoryData<string, decimal> DecimalConflictCases
    {
        get
        {
            var data = new TheoryData<string, decimal>();

            foreach (var target in SelectedTargets)
            {
                foreach (var value in s_decimalBoundaryValues)
                {
                    data.Add(target, value);
                }
            }

            return data;
        }
    }

    /// <summary>Reports binary edge bytes and the distinct empty, non-null representation.</summary>
    public static TheoryData<string, bool> BinaryConflictCases
    {
        get
        {
            var data = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                foreach (var empty in s_binaryPayloadVariants)
                {
                    data.Add(target, empty);
                }
            }

            return data;
        }
    }

    /// <summary>A duplicate update preserves all eighteen decimal digits and the mapped four-place scale.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(DecimalConflictCases))]
    public async Task Decimal_conflict_update_preserves_precision_scale_and_unselected_values(
        string target,
        decimal value
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        var existing = NewRow(1, "original");
        context.Add(existing);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1, "must-not-replace");
        input.Amount = value;

        // Act
        var affected = await context.ExecuteUpsertAsync(input, s_amountUpdate, CancellationToken.None);
        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal(value, persisted.Amount);
        Assert.Equal(value, input.Amount);
        Assert.Equal("original", persisted.Name);
        Assert.Equal(existing.Timestamp, persisted.Timestamp);
        Assert.Equal(existing.Payload, persisted.Payload);
    }

    /// <summary>A decimal outside the mapped precision fails natively without partially updating the conflicting row.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Decimal_conflict_overflow_preserves_the_existing_row(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var existing = NewRow(1, "original");
        context.Add(existing);
        await context.SaveChangesAsync(CancellationToken.None);
        await context.Database.OpenConnectionAsync(CancellationToken.None);

        // WHY: Range rejection belongs to this owned session's explicit SQL mode,
        // rather than an assumed engine default that could permit clipping.
        await context.Database.ExecuteSqlRawAsync("SET SESSION sql_mode='STRICT_ALL_TABLES';", CancellationToken.None);
        var input = NewRow(1, "must-not-persist");
        input.Amount = 100000000000000m;

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertAsync(input, s_amountAndNameUpdate, CancellationToken.None));

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1264,
            Assert.IsType<MySqlException>(error)
                .Number);

        Assert.Single(capture.Statements);
        Assert.Equal(existing.Amount, persisted.Amount);
        Assert.Equal("original", persisted.Name);
        Assert.Equal(existing.Timestamp, persisted.Timestamp);
        Assert.Equal(existing.Payload, persisted.Payload);
    }

    /// <summary>A temporal duplicate update uses the same fractional-second mapping as an ordinary parameterized write.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Temporal_conflict_update_matches_the_existing_parameter_mapping(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        var existing = NewRow(1, "original");
        var control = NewRow(2, "ordinary-mapping-control");
        var incomingTimestamp = new DateTime(2027,
            11,
            18,
            3,
            4,
            5,
            123,
            DateTimeKind.Unspecified).AddTicks(7890);

        control.Timestamp = incomingTimestamp;
        context.AddRange(existing, control);
        await context.SaveChangesAsync(CancellationToken.None);

        // WHY: The engines need not use identical fractional rounding. An actual
        // ordinary mapped write supplies the native oracle for this same server.
        var expectedTimestamp = await context
            .Rows
            .Where(row => row.Id == 2)
            .Select(row => row.Timestamp)
            .SingleAsync(CancellationToken.None);

        var input = NewRow(1, "must-not-replace");
        input.Timestamp = incomingTimestamp;

        // Act
        var affected = await context.ExecuteUpsertAsync(input, s_timestampUpdate, CancellationToken.None);
        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(row => row.Id == 1, CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.Equal(expectedTimestamp, persisted.Timestamp);
        Assert.Equal(0, persisted.Timestamp.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.NotEqual(incomingTimestamp, persisted.Timestamp);
        Assert.Equal(incomingTimestamp, input.Timestamp);
        Assert.Equal("original", persisted.Name);
        Assert.Equal(existing.Amount, persisted.Amount);
        Assert.Equal(existing.Payload, persisted.Payload);
    }

    /// <summary>Binary duplicate updates preserve every byte value and distinguish an empty payload from SQL NULL.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(BinaryConflictCases))]
    public async Task Binary_conflict_update_preserves_edge_bytes_and_empty_payload(
        string target,
        bool empty
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await using var context = store.CreateContext();
        var existing = NewRow(1, "original");
        context.Add(existing);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1, "must-not-replace");
        var expectedPayload = empty
            ? Array.Empty<byte>()
            : s_everyByteValue;

        input.Payload = expectedPayload;

        // WHY: An in-place input mutation must not also change the expected readback bytes.
        var originalPayload = expectedPayload.ToArray();

        // Act
        var affected = await context.ExecuteUpsertAsync(input, s_payloadUpdate, CancellationToken.None);
        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, affected);
        Assert.NotNull(persisted.Payload);
        Assert.Equal(originalPayload, persisted.Payload);
        Assert.Equal(originalPayload, input.Payload);
        Assert.Same(expectedPayload, input.Payload);
        Assert.Equal("original", persisted.Name);
        Assert.Equal(existing.Amount, persisted.Amount);
        Assert.Equal(existing.Timestamp, persisted.Timestamp);
    }

    /// <summary>Malformed scalar JSON is rejected by the native type or validation constraint without a partial duplicate update.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Malformed_json_conflict_update_preserves_the_existing_row(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var existing = NewRow(1, "original");
        existing.Json = "{}";
        context.Add(existing);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewRow(1, "must-not-persist");
        input.Json = "{\"text\":]}";

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertAsync(input, s_jsonAndNameUpdate, CancellationToken.None));

        await using var reader = store.CreateContext();
        var persisted = await reader
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(store.Version.IsMariaDb
                ? 4025
                : 3140,
            Assert.IsType<MySqlException>(error)
                .Number);

        Assert.Single(capture.Statements);
        using var document = JsonDocument.Parse(persisted.Json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateObject());
        Assert.Equal("original", persisted.Name);
        Assert.Equal(existing.Amount, persisted.Amount);
        Assert.Equal(existing.Timestamp, persisted.Timestamp);
        Assert.Equal(existing.Payload, persisted.Payload);
    }
}
