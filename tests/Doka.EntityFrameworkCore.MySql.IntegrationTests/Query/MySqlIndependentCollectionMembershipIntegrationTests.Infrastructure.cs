namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Centralizes schema ownership and preserves typed membership expressions.</summary>
public sealed partial class MySqlIndependentCollectionMembershipIntegrationTests
{
    /// <summary>Creates every context through one stable class-owned schema path.</summary>
    private static MembershipContext CreateContext(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<MembershipContext>();

        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target));

        return new MembershipContext(builder.Options, SupportsNativeUuid(target));
    }

    private static IQueryable<MembershipEntity> GuidMembershipQuery(
        IQueryable<MembershipEntity> source,
        MySqlGuidFormat format,
        Guid[] values,
        bool negated
    ) => (format, negated) switch
    {
        (MySqlGuidFormat.Binary16, false) => source.Where(row => EF
            .Parameter(values)
            .Contains(row.BinaryId)),
        (MySqlGuidFormat.Binary16, true) => source.Where(row => !EF
            .Parameter(values)
            .Contains(row.BinaryId)),
        (MySqlGuidFormat.Char36, false) => source.Where(row => EF
            .Parameter(values)
            .Contains(row.CharId)),
        (MySqlGuidFormat.Char36, true) => source.Where(row => !EF
            .Parameter(values)
            .Contains(row.CharId)),
        (MySqlGuidFormat.Uuid, false) => source.Where(row => EF
            .Parameter(values)
            .Contains(row.NativeId)),
        (MySqlGuidFormat.Uuid, true) => source.Where(row => !EF
            .Parameter(values)
            .Contains(row.NativeId)),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported test GUID format."),
    };

    private static IQueryable<MembershipEntity> NullableGuidMembershipQuery(
        IQueryable<MembershipEntity> source,
        MySqlGuidFormat format,
        Guid?[] values,
        bool negated
    ) => (format, negated) switch
    {
        (MySqlGuidFormat.Binary16, false) => source.Where(row => EF
            .Parameter(values)
            .Contains(row.OptionalBinaryId)),
        (MySqlGuidFormat.Binary16, true) => source.Where(row => !EF
            .Parameter(values)
            .Contains(row.OptionalBinaryId)),
        (MySqlGuidFormat.Char36, false) => source.Where(row => EF
            .Parameter(values)
            .Contains(row.OptionalCharId)),
        (MySqlGuidFormat.Char36, true) => source.Where(row => !EF
            .Parameter(values)
            .Contains(row.OptionalCharId)),
        (MySqlGuidFormat.Uuid, false) => source.Where(row => EF
            .Parameter(values)
            .Contains(row.OptionalNativeId)),
        (MySqlGuidFormat.Uuid, true) => source.Where(row => !EF
            .Parameter(values)
            .Contains(row.OptionalNativeId)),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported test GUID format."),
    };

    /// <summary>Reads each matching row once in deterministic key order.</summary>
    /// <param name="query">The membership query under test.</param>
    /// <param name="cancellationToken">The token to observe during database work.</param>
    private static Task<int[]> SelectIdsAsync(
        IQueryable<MembershipEntity> query,
        CancellationToken cancellationToken
    ) => query
        .OrderBy(row => row.Id)
        .Select(row => row.Id)
        .ToArrayAsync(cancellationToken);

    private static Guid CreateGuid(
        int value
    ) => new(value,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);

    /// <summary>Drops the one owned corpus table under the caller's cancellation policy.</summary>
    /// <param name="context">The context in the class-owned schema.</param>
    /// <param name="cancellationToken">The token to observe during database work.</param>
    private static Task<int> DropTableAsync(
        MembershipContext context,
        CancellationToken cancellationToken
    ) => context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;", cancellationToken);
}
