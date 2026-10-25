namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMariaDbUuidIntegrationTests
{
    /// <summary>Identifies each native, binary, or character ordering/range boundary.</summary>
    public static IEnumerable<object[]> VersionedOrderCases =>
        from target in s_mariaDbTargets.Where(IntegrationTestEnvironment.IsTargetSelected)
        from testCase in s_versionedOrderCases
        select new object[] { target.ToString(), testCase.Name };

    /// <summary>Verifies ordered v7 UUID materialization independently of comparisons.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Versioned_uuid_materializes_v7_values(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateVersionedContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        var query = context.Set<VersionedUuidEntity>().AsNoTracking()
            .Where(entity => entity.Id >= 3).OrderBy(entity => entity.Id).Select(entity => entity.NativeId);

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Equal([s_v7First, s_v7Last], actual);
    }

    /// <summary>Verifies parameter equality against native v7 UUID storage.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Versioned_uuid_equality_finds_v7_value(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateVersionedContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        var query = context.Set<VersionedUuidEntity>().AsNoTracking();

        // Act
        var actual = await query.SingleAsync(
            entity => entity.NativeId == s_v7Last,
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Equal(4, actual.Id);
    }

    /// <summary>Verifies explicit JSON membership against native v7 UUID storage.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Versioned_uuid_membership_finds_v7_value(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateVersionedContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        var membership = s_v7Membership;
        var query = context.Set<VersionedUuidEntity>().AsNoTracking();

        // Act
        var actual = await query.SingleAsync(
            entity => EF.Parameter(membership).Contains(entity.NativeId),
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Equal(4, actual.Id);
    }

    /// <summary>Verifies a native UUID comparison does not match the missing empty GUID.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(NativeTargetCases))]
    public async Task Versioned_uuid_missing_value_does_not_match(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateVersionedContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        // Act
        var actual = await context.Set<VersionedUuidEntity>().AnyAsync(
            entity => entity.NativeId == Guid.Empty,
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Verifies server ordering for one UUID version and storage representation.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(VersionedOrderCases))]
    public async Task Versioned_uuid_ordering_matches_storage_contract(
        string target,
        string scenario
    )
    {
        // Arrange
        await using var context = await _fixture.CreateVersionedContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        var testCase = s_versionedOrderCases.Single(item => item.Name == scenario);
        var columnName = testCase.Column;
        var rows = testCase.FirstId == 1
            ? context.Set<VersionedUuidEntity>().AsNoTracking().Where(entity => entity.Id <= 2)
            : context.Set<VersionedUuidEntity>().AsNoTracking().Where(entity => entity.Id >= 3);

        var query = rows.OrderBy(entity => EF.Property<Guid>(entity, columnName)).Select(entity => entity.Id);

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Equal(testCase.ExpectedIds, actual);
    }

    /// <summary>Verifies a range window against the server's actual UUID storage ordering.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(VersionedOrderCases))]
    public async Task Versioned_uuid_range_window_matches_storage_contract(
        string target,
        string scenario
    )
    {
        // Arrange
        await using var context = await _fixture.CreateVersionedContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        await context.Database.OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        var connection = (MySqlConnection)context.Database.GetDbConnection();
        var testCase = s_versionedOrderCases.Single(item => item.Name == scenario);

        // Act
        var actual = await ReadFirstIdAfterAsync(
            connection,
            testCase.Column,
            testCase.FirstId,
            testCase.LastId,
            testCase.Pivot).ConfigureAwait(false);

        // Assert
        Assert.Equal(testCase.ExpectedNextId, actual);
    }

    private sealed record VersionedOrderCase(
        string Name,
        string Column,
        int FirstId,
        int LastId,
        int[] ExpectedIds,
        Guid Pivot,
        int? ExpectedNextId);

    /// <summary>Captures raw range semantics without routing through Guid.CompareTo translation.</summary>
    private static async Task<int?> ReadFirstIdAfterAsync(
        MySqlConnection connection,
        string columnName,
        int firstId,
        int lastId,
        Guid pivot
    )
    {
        await using var command = connection.CreateCommand();
        // The server owns UUID ordering. Raw SQL isolates its range semantics
        // from Guid.CompareTo translation, which is not part of this feature.
        command.CommandText = $"SELECT `Id` FROM `{VersionedTable}` "
            + $"WHERE `Id` BETWEEN @firstId AND @lastId AND `{columnName}` > @pivot "
            + $"ORDER BY `{columnName}` LIMIT 1;";
        command.Parameters.AddWithValue("@firstId", firstId);
        command.Parameters.AddWithValue("@lastId", lastId);
        // The connector must bind Binary16 as Guid, but native UUID as text.
        // A text pivot against Binary16 would test coercion instead of key order.
        if (columnName == "BinaryId")
        {
            command.Parameters.AddWithValue("@pivot", pivot);
        }
        else
        {
            command.Parameters.AddWithValue("@pivot", pivot.ToString("D", CultureInfo.InvariantCulture));
        }

        var value = await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

        return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }
}
