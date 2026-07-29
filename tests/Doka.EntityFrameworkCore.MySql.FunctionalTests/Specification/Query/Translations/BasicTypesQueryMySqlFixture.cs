using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Translations;
using Microsoft.EntityFrameworkCore.TestModels.BasicTypesModel;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Translations;

/// <summary>
/// Connects the official EF Core basic-types model and seed data to the shared MySQL
/// specification-test store.
/// </summary>
public sealed class BasicTypesQueryMySqlFixture : BasicTypesQueryFixtureBase
{
    private ISetSource? _expectedData;

    protected override ITestStoreFactory TestStoreFactory => MySqlTestStoreFactory.Instance;

    /// <summary>
    /// Returns the official expected data with store-mapped temporal values at the
    /// maximum precision that MySQL and MariaDB can physically preserve.
    /// </summary>
    /// <remarks>
    /// The official seed deliberately includes seventh-digit 100-nanosecond values,
    /// while both engine families support at most six fractional digits. Sources
    /// retrieved 2026-07-29:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html">
    /// MySQL fractional seconds</see> and
    /// <see href="https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/microseconds-in-mariadb">
    /// MariaDB microseconds</see>.
    /// </remarks>
    public override ISetSource GetExpectedData()
    {
        if (_expectedData is not null)
        {
            return _expectedData;
        }

        var expectedData = new BasicTypesData();

        foreach (var entity in expectedData.BasicTypesEntities)
        {
            entity.DateTime = TruncateToMicroseconds(entity.DateTime);
            entity.TimeOnly = TruncateToMicroseconds(entity.TimeOnly);
            entity.TimeSpan = TruncateToMicroseconds(entity.TimeSpan);
        }

        foreach (var entity in expectedData.NullableBasicTypesEntities)
        {
            entity.DateTime = TruncateToMicroseconds(entity.DateTime);
            entity.TimeOnly = TruncateToMicroseconds(entity.TimeOnly);
            entity.TimeSpan = TruncateToMicroseconds(entity.TimeSpan);
        }

        _expectedData = expectedData;
        return expectedData;
    }

    private static DateTime TruncateToMicroseconds(
        DateTime value
    ) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    private static DateTime? TruncateToMicroseconds(
        DateTime? value
    ) => value is null
        ? null
        : TruncateToMicroseconds(value.Value);

    private static TimeOnly TruncateToMicroseconds(
        TimeOnly value
    ) => new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMicrosecond));

    private static TimeOnly? TruncateToMicroseconds(
        TimeOnly? value
    ) => value is null
        ? null
        : TruncateToMicroseconds(value.Value);

    private static TimeSpan TruncateToMicroseconds(
        TimeSpan value
    ) => TimeSpan.FromTicks(value.Ticks - (value.Ticks % TimeSpan.TicksPerMicrosecond));

    private static TimeSpan? TruncateToMicroseconds(
        TimeSpan? value
    ) => value is null
        ? null
        : TruncateToMicroseconds(value.Value);
}
