using Microsoft.EntityFrameworkCore.Query.Translations.Temporal;
using Microsoft.EntityFrameworkCore.TestModels.BasicTypesModel;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Translations;

/// <summary>
/// Executes the official <see cref="DateOnly"/> translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesDateOnlyTranslationsMySqlTest
    : DateOnlyTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesDateOnlyTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }
}

/// <summary>
/// Executes the official <see cref="DateTimeOffset"/> translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesDateTimeOffsetTranslationsMySqlTest
    : DateTimeOffsetTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesDateTimeOffsetTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }

    /// <summary>
    /// Verifies that a captured <see cref="TimeSpan"/> remains parameterized and
    /// is validated before it reaches the provider's <see cref="DateTimeOffset.ToOffset(TimeSpan)"/>
    /// translation.
    /// </summary>
    [Fact]
    public Task ToOffset_with_parameter()
    {
        var offset = TimeSpan.FromHours(2);

        return AssertQuery(source => source
            .Set<BasicTypesEntity>()
            .Where(entity => entity.DateTimeOffset.ToOffset(offset)
                == new DateTimeOffset(1998, 5, 4, 17, 30, 10, offset)));
    }

    /// <summary>
    /// Verifies that the two-argument <see cref="DateTimeOffset"/> constructor
    /// accepts and validates a captured offset parameter.
    /// </summary>
    [Fact]
    public Task Constructor_with_parameter_offset()
    {
        var offset = TimeSpan.FromHours(2);

        return AssertQuery(source => source
            .Set<BasicTypesEntity>()
            .Where(entity => entity.DateTime.Year > 1)
            .Where(entity => new DateTimeOffset(entity.DateTime, offset)
                == new DateTimeOffset(1998, 5, 4, 15, 30, 10, offset)));
    }

    /// <summary>
    /// Verifies that runtime offset parameters preserve the
    /// <see cref="DateTimeOffset"/> whole-minute and range contracts.
    /// </summary>
    [Theory]
    [InlineData(-15, 0, 0, "between -14:00 and +14:00")]
    [InlineData(0, 0, 30, "whole minutes")]
    [InlineData(15, 0, 0, "between -14:00 and +14:00")]
    public async Task Invalid_parameter_offset_is_rejected_before_execution(
        int hours,
        int minutes,
        int seconds,
        string expectedMessage
    )
    {
        await using var context = Fixture.CreateContext();
        var offset = new TimeSpan(hours, minutes, seconds);
        var query = context
            .Set<BasicTypesEntity>()
            .Where(entity => entity.DateTimeOffset.ToOffset(offset) == entity.DateTimeOffset);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => query.ToListAsync(CancellationToken.None));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a row-dependent offset is rejected because MySQL cannot
    /// validate its whole-minute and fourteen-hour constraints before execution.
    /// </summary>
    [Fact]
    public void ToOffset_with_column_offset_is_not_translated()
    {
        using var context = Fixture.CreateContext();
        var query = context
            .Set<BasicTypesEntity>()
            .Where(entity => entity.DateTimeOffset.ToOffset(entity.TimeSpan) == entity.DateTimeOffset);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that the two-argument constructor rejects a row-dependent offset
    /// for the same pre-execution validation requirement as <see cref="DateTimeOffset.ToOffset(TimeSpan)"/>.
    /// </summary>
    [Fact]
    public void Constructor_with_column_offset_is_not_translated()
    {
        using var context = Fixture.CreateContext();
        var query = context
            .Set<BasicTypesEntity>()
            .Where(entity => new DateTimeOffset(entity.DateTime, entity.TimeSpan) == entity.DateTimeOffset);

        var exception = Assert.Throws<InvalidOperationException>(query.ToQueryString);

        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Executes the official <see cref="DateTime"/> translation contract against the provider.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesDateTimeTranslationsMySqlTest
    : DateTimeTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesDateTimeTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }

    /// <inheritdoc />
    public override Task Parse_with_constant()
        => ExecuteWithUsEnglishCulture(
            base.Parse_with_constant);

    /// <inheritdoc />
    public override Task Parse_with_parameter()
        => ExecuteWithUsEnglishCulture(
            base.Parse_with_parameter);

    private static async Task ExecuteWithUsEnglishCulture(
        Func<Task> test
    )
    {
        var previousCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            await test().ConfigureAwait(false);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}

/// <summary>
/// Executes the official <see cref="TimeOnly"/> translation contract against the provider.
/// </summary>
/// <remarks>
/// MySQL and MariaDB preserve at most six fractional-second digits, so stored values
/// have no independently representable nanosecond component. Sources retrieved
/// 2026-07-29:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html">
/// MySQL fractional seconds</see> and
/// <see href="https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/microseconds-in-mariadb">
/// MariaDB microseconds</see>.
/// </remarks>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesTimeOnlyTranslationsMySqlTest
    : TimeOnlyTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesTimeOnlyTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }

    /// <summary>
    /// Verifies that engine-stored <see cref="TimeOnly"/> values expose zero
    /// nanoseconds beyond their preserved microsecond component.
    /// </summary>
    public override Task Nanosecond()
        => AssertQuery(
            source => source
                .Set<BasicTypesEntity>()
                .Where(entity => entity.TimeOnly.Nanosecond == 0));
}

/// <summary>
/// Executes the official <see cref="TimeSpan"/> translation contract against the provider.
/// </summary>
/// <remarks>
/// MySQL and MariaDB preserve at most six fractional-second digits, so stored values
/// have no independently representable nanosecond component. Sources retrieved
/// 2026-07-29:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html">
/// MySQL fractional seconds</see> and
/// <see href="https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/microseconds-in-mariadb">
/// MariaDB microseconds</see>.
/// </remarks>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class BasicTypesTimeSpanTranslationsMySqlTest
    : TimeSpanTranslationsTestBase<BasicTypesQueryMySqlFixture>
{
    public BasicTypesTimeSpanTranslationsMySqlTest(
        BasicTypesQueryMySqlFixture fixture
    ) : base(fixture)
    {
    }

    /// <summary>
    /// Verifies that engine-stored <see cref="TimeSpan"/> values expose zero
    /// nanoseconds beyond their preserved microsecond component.
    /// </summary>
    public override Task Nanoseconds()
        => AssertQuery(
            source => source
                .Set<BasicTypesEntity>()
                .Where(entity => entity.TimeSpan.Nanoseconds == 0));

    /// <summary>
    /// Verifies that adding two stored-duration expressions executes through
    /// the provider's ADDTIME translation and preserves materialized values.
    /// </summary>
    [Fact]
    public async Task Addition_translates_to_addtime_and_preserves_results()
    {
        Fixture.TestSqlLoggerFactory.Clear();

        await AssertQuery(source => source
            .Set<BasicTypesEntity>()
            .Select(entity => entity.TimeSpan + TimeSpan.FromMinutes(1)));

        Assert.Contains("ADDTIME(", Fixture.TestSqlLoggerFactory.Sql, StringComparison.Ordinal);
    }
}
