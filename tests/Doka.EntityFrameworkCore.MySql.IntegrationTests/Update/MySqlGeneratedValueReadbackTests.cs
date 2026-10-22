namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Exercises native generated-value correlation and bounded insert groups.</summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>Reads all generated outputs for a normal bounded MySQL group.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task KnownIntegerKeysReadBack64Rows() =>
        AssertGeneratedValuesAsync<int, StandardMode>(IntegrationDatabaseTarget.MySql84, 64, value => value);

    /// <summary>Treats zero as a stable key when the column is not auto-incrementing.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task NonGeneratedZeroKeyRemainsEligible() =>
        AssertGeneratedValuesAsync<int, StandardMode>(IntegrationDatabaseTarget.MySql84, 64, value => value - 1);

    /// <summary>Exercises MySQL's largest supported insert group.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task KnownIntegerKeysReadBack1000Rows() =>
        AssertGeneratedValuesAsync<int, StandardMode>(IntegrationDatabaseTarget.MySql84, 1000, value => value);

    /// <summary>Resets relative result ordinals at the next insert batch.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task KnownIntegerKeysReadBackAcross1001Rows() =>
        AssertGeneratedValuesAsync<int, StandardMode>(IntegrationDatabaseTarget.MySql84, 1001, value => value);

    /// <summary>Exercises the separate synchronous native generated-result consumer.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task SynchronousSaveReadsEveryGeneratedValue()
    {
        // Arrange
        var capture = new ReadbackCapture();
        await using var context = CreateContext<int, StandardMode>(IntegrationDatabaseTarget.MySql84, capture);
        await RecreateTableAsync(context);
        var rows = CreateIntegerRows(64);
        AddRows(context, rows);
        capture.Enabled = true;

        // Act
        // WHY: This native control must exercise the synchronous provider result consumer.
        // ReSharper disable once MethodHasAsyncOverload
        var written = context.SaveChanges();

        // Assert
        Assert.Equal(64, written);
        Assert.All(rows, AssertGenerated);
        Assert.Equal(1, capture.Commands.Sum(sql => CountOccurrences(sql, "INSERT INTO")));
        Assert.Equal(63, capture.Commands.Sum(sql => CountOccurrences(sql, "UNION ALL")));
    }

    /// <summary>Preserves MariaDB's existing INSERT RETURNING path.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDbGeneratedValuesKeepReturning() =>
        AssertGeneratedValuesAsync<int, StandardMode>(IntegrationDatabaseTarget.MariaDb118, 64, value => value);

    /// <summary>Correlates signed 64-bit keys without narrowing them to integers.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task SignedLongKeysKeepTheirFullWidth() =>
        AssertGeneratedValuesAsync<long, StandardMode>(
            IntegrationDatabaseTarget.MySql84, 64, value => long.MaxValue - value);

    /// <summary>Correlates unsigned keys above the signed 64-bit range.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task UnsignedLongKeysKeepTheirFullWidth() =>
        AssertGeneratedValuesAsync<ulong, StandardMode>(
            IntegrationDatabaseTarget.MySql84, 64, value => ulong.MaxValue - (ulong)value);

    /// <summary>Correlates supplied binary GUID keys through the installed type mapping.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task SuppliedBinaryGuidKeysRemainCorrelated() =>
        AssertGeneratedValuesAsync<Guid, StandardMode>(
            IntegrationDatabaseTarget.MySql84, 64, CreateGuid);

    /// <summary>Correlates supplied character GUID keys through their value converter.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task SuppliedCharacterGuidKeysRemainCorrelated() =>
        AssertGeneratedValuesAsync<Guid, CharacterGuidMode>(
            IntegrationDatabaseTarget.MySql84, 64, CreateGuid);

    /// <summary>Uses provider values when a key has a custom CLR value converter.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task ConvertedKeysRemainCorrelated() =>
        AssertGeneratedValuesAsync<StoredKey, StandardMode>(
            IntegrationDatabaseTarget.MySql84, 64, value => new StoredKey(value));

    /// <summary>Reads composite shadow keys with the column's native comparison rules.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task CompositeShadowKeysUseNativeCollation() =>
        AssertGeneratedValuesAsync<long, ShadowCompositeMode>(
            IntegrationDatabaseTarget.MySql84, 64, value => value);

    /// <summary>Observes one save and its persisted readback without mixing assertions into either step.</summary>
    /// <typeparam name="TKey">The supplied key representation.</typeparam>
    /// <typeparam name="TMode">The fixture's mapping and identity behavior.</typeparam>
    /// <param name="target">The selected live database engine.</param>
    /// <param name="count">The number of rows in the insert group.</param>
    /// <param name="keyFactory">The deterministic supplied-key factory.</param>
    private static async Task AssertGeneratedValuesAsync<TKey, TMode>(
        IntegrationDatabaseTarget target,
        int count,
        Func<int, TKey> keyFactory
    ) where TKey : notnull
    {
        // Arrange
        var capture = new ReadbackCapture();
        await using var context = CreateContext<TKey, TMode>(target, capture);
        await RecreateTableAsync(context);
        var rows = Enumerable.Range(1, count)
            .Select(value => new GeneratedRow<TKey> { Id = keyFactory(value), Input = value })
            .Reverse()
            .ToArray();

        AddRows(context, rows);
        capture.Enabled = true;

        // Act: persistence and its readback are one dependent round trip.
        var written = await context.SaveChangesAsync(CancellationToken.None);

        capture.Enabled = false;
        var saveSql = capture.Commands.ToArray();

        var persisted = await context.Rows.AsNoTracking().OrderBy(row => row.Input)
            .ToListAsync(CancellationToken.None);

        var scopes = typeof(TMode) == typeof(ShadowCompositeMode)
            ? await context.Rows.AsNoTracking()
                .Select(row => new { row.Input, Scope = EF.Property<string>(row, "Tenant") })
                .ToListAsync(CancellationToken.None)
            : null;

        // Assert
        Assert.Equal(count, written);
        Assert.Equal(count, persisted.Count);
        Assert.All(rows, AssertGenerated);
        Assert.All(persisted, AssertGenerated);
        if (typeof(TMode) != typeof(ShadowCompositeMode))
        {
            Assert.Equal(rows.OrderBy(row => row.Input).Select(row => row.Id), persisted.Select(row => row.Id));
        }

        if (scopes is not null)
        {
            Assert.All(scopes, stored =>
            {
                var supplied = (string)context.Entry(rows.Single(row => row.Input == stored.Input))
                    .Property("Tenant").CurrentValue!;
                Assert.Equal(supplied.TrimEnd(' '), stored.Scope);
                Assert.NotEqual(supplied, stored.Scope);
            });
        }

        var expectedGroups = (count + 999) / 1000;
        Assert.Equal(expectedGroups, saveSql.Sum(sql => CountOccurrences(sql, "INSERT INTO")));

        if (target == IntegrationDatabaseTarget.MySql84)
        {
            Assert.Equal(count - expectedGroups, saveSql.Sum(sql => CountOccurrences(sql, "UNION ALL")));
            Assert.DoesNotContain(saveSql, sql => sql.Contains("RETURNING", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(expectedGroups, saveSql.Sum(sql => CountOccurrences(sql, "RETURNING")));
            Assert.DoesNotContain(saveSql, sql => sql.Contains("UNION ALL", StringComparison.Ordinal));
        }
    }

    private static void AssertGenerated<TKey>(GeneratedRow<TKey> row) where TKey : notnull
    {
        // WHY: Constant outputs alone cannot reveal an ordinal or key permutation.
        Assert.Equal((row.Input * 17L) + 3L, row.Stamp);
        Assert.Equal(row.Input % 2 == 0, row.IsEven);
        Assert.Equal(row.Input % 3 == 0 ? null : $"v{row.Input}", row.NullableValue);
        Assert.Equal("server-default", row.DefaultCode);
        Assert.Equal((ulong)row.Input * 31UL, row.Version);
    }

    private static Guid CreateGuid(int value) => new(value, 0, 0, new byte[8]);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;

        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
