using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query;

/// <summary>
/// Activates an upstream flaky-test skip after the complete provider matrix proved
/// deterministic SaveChanges behavior.
/// </summary>
public sealed partial class AdHocMiscellaneousQueryMySqlTest
{
    [Fact]
    public override Task SaveChangesAsync_accepts_changes_with_ConfigureAwait_true() =>
        base.SaveChangesAsync_accepts_changes_with_ConfigureAwait_true();

    /// <inheritdoc />
    [Fact]
    [SpecEngineLimitationFact(
        "MDB-CORRELATED-DERIVED-TABLE",
        "mariadb114",
        "mariadb118")]
    public override async Task Correlated_SelectMany_DefaultIfEmpty_whole_object()
    {
        var contextFactory = await InitializeNonSharedTest<Context30915>(seed: Seed30915);
        await using var context = contextFactory.CreateDbContext();

        var query = from status in context.Statuses
                    from countInfo in context.Requests
                        .Where(request => request.PickupStatusId == status.PickupStatusId)
                        .GroupBy(
                            request => request.PickupStatusId,
                            (key, requests) => new
                            {
                                pickupStatusId = key,
                                Count = requests.Count(),
                            })
                        .DefaultIfEmpty()
                    orderby status.PickupStatusId
                    select new { status.PickupStatusId, countInfo };

        var result = await query.ToListAsync(CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].PickupStatusId);
        Assert.NotNull(result[0].countInfo);
        Assert.Equal(1, result[0].countInfo.pickupStatusId);
        Assert.Equal(2, result[0].countInfo.Count);
        Assert.Equal(2, result[1].PickupStatusId);
        Assert.Null(result[1].countInfo);
        Assert.Equal(3, result[2].PickupStatusId);
        Assert.NotNull(result[2].countInfo);
        Assert.Equal(3, result[2].countInfo.pickupStatusId);
        Assert.Equal(1, result[2].countInfo.Count);
    }
}

/// <summary>
/// Runs the complex-type pushdown contract on MySQL and records MariaDB's existing
/// correlated-derived-table engine boundary.
/// </summary>
public sealed partial class ComplexTypeQueryMySqlTest
{
    [Theory]
    [SpecEngineLimitationTheory("MDB-CORRELATED-DERIVED-TABLE", "mariadb114", "mariadb118")]
    [InheritedTheoryData]
    public override Task Same_complex_type_projected_twice_with_pushdown_as_part_of_another_projection(
        bool async
    ) => base.Same_complex_type_projected_twice_with_pushdown_as_part_of_another_projection(async);
}

/// <summary>
/// Records the rowset-function boundary for the remaining upstream-skipped UDF
/// contract instead of inheriting an untracked framework skip.
/// </summary>
public sealed partial class UdfDbFunctionMySqlTest
{
    [Fact]
    [SpecEngineLimitationFact("MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS", "mysql84", "mariadb114", "mariadb118")]
    public override void QF_Select_Direct_In_Anonymous_distinct() => base.QF_Select_Direct_In_Anonymous_distinct();
}
