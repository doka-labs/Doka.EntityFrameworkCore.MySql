using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.Fixtures;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Xunit.Abstractions;

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

    /// <summary>
    /// Verifies that an empty primitive collection remains a valid JSON array in storage.
    /// </summary>
    [Fact]
    public async Task Empty_collection_is_persisted_as_json_array()
    {
        await using var context = Fixture
            .GetContextCreator()();

        var storedValue = await context
            .Database
            .SqlQueryRaw<string>(
                """
                SELECT HEX(`Ints`) AS `Value`
                FROM `PrimitiveCollectionsEntity`
                WHERE `Id` = 5
                """)
            .SingleAsync();

        Assert.Equal("5B5D", storedValue);
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
    [SpecEngineLimitationTheory(
        "MYSQL84-SPATIAL-RELATE",
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
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-BUFFER-STRATEGY",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task Buffer_quadrantSegments(
        bool async
    ) => base.Buffer_quadrantSegments(async);

    /// <summary>
    /// MariaDB 11.4 and 11.8 do not expose the spatial aggregate required by NTS
    /// collection-combine semantics.
    /// </summary>
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-COLLECT",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task Combine_aggregate(
        bool async
    ) => base.Combine_aggregate(async);

    /// <summary>
    /// MariaDB 11.4 and 11.8 do not expose the spatial aggregate required before
    /// computing a collection envelope.
    /// </summary>
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-COLLECT",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task EnvelopeCombine_aggregate(
        bool async
    ) => base.EnvelopeCombine_aggregate(async);

    /// <summary>
    /// MariaDB 11.4 and 11.8 do not expose the spatial aggregate required before
    /// computing a unary collection union.
    /// </summary>
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-COLLECT",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task Union_aggregate(
        bool async
    ) => base.Union_aggregate(async);

    /// <summary>
    /// MariaDB adds server-side OGC validity testing only after the supported 11.x
    /// targets.
    /// </summary>
    [SpecEngineLimitationTheory(
        "MARIADB-SPATIAL-VALIDITY",
        "mariadb114",
        "mariadb118")]
    [MemberData(nameof(IsAsyncData))]
    public override Task IsValid(
        bool async
    ) => base.IsValid(async);
}
