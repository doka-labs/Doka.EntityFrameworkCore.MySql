using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Fixtures;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// Runs primitive-collection translation and materialization through MySQL JSON storage.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed partial class PrimitiveCollectionsQueryMySqlTest
    : PrimitiveCollectionsQueryRelationalTestBase<PrimitiveCollectionsQueryMySqlFixture>
{
    public PrimitiveCollectionsQueryMySqlTest(
        PrimitiveCollectionsQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(
        DbContextOptionsBuilder optionsBuilder,
        ParameterTranslationMode parameterizedCollectionMode
    )
    {
        new MySqlDbContextOptionsBuilder(optionsBuilder).UseParameterizedCollectionMode(parameterizedCollectionMode);

        return optionsBuilder;
    }

    [Fact]
    public override Task Parameter_with_inferred_value_converter() => base.Parameter_with_inferred_value_converter();

    [Fact]
    public override async Task Column_collection_of_strings_Contains()
    {
        await base.Column_collection_of_strings_Contains();

        Assert.Contains("JSON_CONTAINS(`p`.`Strings`", Fixture.TestSqlLoggerFactory.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE(`p`.`Strings`", Fixture.TestSqlLoggerFactory.Sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that an empty primitive collection remains a valid JSON array in storage.
    /// </summary>
    [Fact]
    public async Task Empty_collection_is_persisted_as_json_array()
    {
        await using var context = Fixture
            .GetContextCreator()();

        var storedInts = await context
            .Database
            .SqlQueryRaw<string>(
                """
                SELECT HEX(`Ints`) AS `Value`
                FROM `PrimitiveCollectionsEntity`
                WHERE `Id` = 5
                """)
            .SingleAsync(Xunit.TestContext.Current.CancellationToken);

        var storedStrings = await context
            .Database
            .SqlQueryRaw<string>(
                """
                SELECT HEX(`Strings`) AS `Value`
                FROM `PrimitiveCollectionsEntity`
                WHERE `Id` = 5
                """)
            .SingleAsync(Xunit.TestContext.Current.CancellationToken);

        var invalidStringCollectionIds = await context
            .Database
            .SqlQueryRaw<string>(
                """
                SELECT COALESCE(GROUP_CONCAT(`Id` ORDER BY `Id`), '') AS `Value`
                FROM `PrimitiveCollectionsEntity`
                WHERE JSON_VALID(`Strings`) = 0
                """)
            .SingleAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.Equal("5B5D", storedInts);
        Assert.Equal("5B5D", storedStrings);
        Assert.Equal(string.Empty, invalidStringCollectionIds);
    }
}

/// <summary>
/// Runs relational spatial queries through the provider's NetTopologySuite services.
/// </summary>
[Trait("Category", "Spec")]
[Collection(FunctionalDatabaseTestGroup.Name)]
public sealed class SpatialQueryMySqlTest
    : SpatialQueryRelationalTestBase<SpatialQueryMySqlFixture>
{
    public SpatialQueryMySqlTest(
        SpatialQueryMySqlFixture fixture,
        ITestOutputHelper testOutputHelper
    ) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    /// <summary>
    /// MySQL cannot execute the NTS normalization contract on the server.
    /// </summary>
    /// <remarks>
    /// The exhaustive MySQL 8.4 spatial function reference contains no geometry
    /// normalization function. Source retrieved 2026-07-29:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/spatial-function-reference.html">
    /// MySQL 8.4 spatial function reference</see>.
    /// </remarks>
    [Theory]
    [SpecEngineLimitationTheory(
        "MYSQL-MARIADB-SPATIAL-NORMALIZE",
        "mysql84",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task Normalized(
        bool async
    ) => base.Normalized(async);

    /// <summary>
    /// MySQL cannot evaluate a DE-9IM relation matrix on the server.
    /// </summary>
    /// <remarks>
    /// The exhaustive MySQL 8.4 spatial function reference contains no
    /// <c>ST_Relate</c> function. Source retrieved 2026-07-29:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/spatial-function-reference.html">
    /// MySQL 8.4 spatial function reference</see>.
    /// </remarks>
    [Theory]
    [SpecEngineLimitationTheory(
        "MYSQL-SPATIAL-RELATE",
        "mysql84")]
    [MemberData(nameof(IsAsyncData))]
    public override Task Relate(
        bool async
    ) => base.Relate(async);

    /// <summary>
    /// MySQL cannot reverse geometry component order on the server.
    /// </summary>
    /// <remarks>
    /// The exhaustive MySQL 8.4 spatial function reference contains no geometry
    /// reverse function. Source retrieved 2026-07-29:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/spatial-function-reference.html">
    /// MySQL 8.4 spatial function reference</see>.
    /// </remarks>
    [Theory]
    [SpecEngineLimitationTheory(
        "MYSQL-MARIADB-SPATIAL-REVERSE",
        "mysql84",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task Reverse(
        bool async
    ) => base.Reverse(async);

    /// <summary>
    /// MariaDB cannot represent NTS quadrant-segment control in its two-argument
    /// <c>ST_Buffer</c> contract.
    /// </summary>
    [Theory]
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-BUFFER-STRATEGY",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task Buffer_quadrantSegments(
        bool async
    ) => base.Buffer_quadrantSegments(async);

    /// <summary>
    /// MariaDB 10.11, 11.4, and 11.8 do not expose the spatial aggregate
    /// required by NTS collection-combine semantics. MariaDB 12.3 executes the
    /// inherited contract through the function added in MariaDB 12.0.
    /// </summary>
    [Theory]
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-COLLECT",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task Combine_aggregate(
        bool async
    ) => base.Combine_aggregate(async);

    /// <summary>
    /// MariaDB 10.11, 11.4, and 11.8 do not expose the spatial aggregate
    /// required before computing a collection envelope. MariaDB 12.3 executes
    /// the inherited contract through the function added in MariaDB 12.0.
    /// </summary>
    [Theory]
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-COLLECT",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task EnvelopeCombine_aggregate(
        bool async
    ) => base.EnvelopeCombine_aggregate(async);

    /// <summary>
    /// MariaDB 10.11, 11.4, and 11.8 do not expose the spatial aggregate
    /// required before computing a unary collection union. MariaDB 12.3
    /// executes the inherited contract through the function added in MariaDB
    /// 12.0.
    /// </summary>
    [Theory]
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-COLLECT",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task Union_aggregate(
        bool async
    ) => base.Union_aggregate(async);

    /// <summary>
    /// MariaDB 10.11, 11.4, and 11.8 predate server-side OGC validity testing.
    /// MariaDB 12.3 executes the inherited contract through the function added
    /// in MariaDB 12.0.
    /// </summary>
    [Theory]
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-VALIDITY",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task IsValid(
        bool async
    ) => base.IsValid(async);
}
