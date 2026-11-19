namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Qualifies Point fidelity and native SRID enforcement for INSERT SELECT on the six supported engines.</summary>
[IntegrationDatabaseTest]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlInsertSelectSpatialIntegrationTests
{
    private const string SourceTable = "Insert spatial source";
    private const string TargetTable = "Insert spatial target";

    /// <summary>Reports each selected supported native profile independently.</summary>
    public static TheoryData<string> SelectedTargets => new(IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => target.ToString()));

    /// <summary>Store-side Point copying preserves coordinates, SRID and WKB without loading source geometries.</summary>
    /// <param name="target">The supported native server profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Point_4326_insert_select_preserves_native_and_materialized_values(
        string target
    )
    {
        // Arrange
        using var safety = new CancellationTokenSource();
        safety.CancelAfter(TimeSpan.FromMinutes(2));
        await using var store = await SpatialStore.CreateAsync(target, safety.Token);
        await store.SeedAsync(includeInvalidSrid: false, safety.Token);
        var expected = await store.ReadPointsAsync(SourceTable, safety.Token);
        var observation = new SpatialCommands();
        await using var context = store.CreateContext(observation);

        // Act
        var count = await context
            .Set<SpatialSource>()
            .OrderBy(row => row.Id)
            .ExecuteInsertFromQueryAsync(context.Set<SpatialTarget>(),
                setters => setters
                    .SetProperty(row => row.Id, row => row.Id)
                    .SetProperty(row => row.Location, row => row.Location),
                safety.Token);

        var actual = await store.ReadPointsAsync(TargetTable, safety.Token);
        await using var readback = store.CreateContext();
        var materialized = await readback
            .Set<SpatialTarget>()
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(2, expected.Length);
        Assert.Equal(expected, actual);
        Assert.Collection(actual,
            row =>
            {
                Assert.Equal(1, row.Id);
                Assert.Equal(1.25, row.X);
                Assert.Equal(2.5, row.Y);
                Assert.Equal(4326, row.Srid);
            },
            row =>
            {
                Assert.Equal(2, row.Id);
                Assert.Equal(-3.75, row.X);
                Assert.Equal(4.125, row.Y);
                Assert.Equal(4326, row.Srid);
            });

        Assert.Collection(materialized,
            row =>
            {
                Assert.Equal(1, row.Id);
                Assert.Equal(1.25, row.Location.X);
                Assert.Equal(2.5, row.Location.Y);
                Assert.Equal(4326, row.Location.SRID);
            },
            row =>
            {
                Assert.Equal(2, row.Id);
                Assert.Equal(-3.75, row.Location.X);
                Assert.Equal(4.125, row.Location.Y);
                Assert.Equal(4326, row.Location.SRID);
            });

        Assert.Empty(context.ChangeTracker.Entries());
        AssertDirectSpatialStatement(observation, safety.Token);
    }

    /// <summary>An SRID-zero row cannot be relabeled or partially committed into an SRID-4326 target.</summary>
    /// <param name="target">The supported native server profile.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Point_srid_mismatch_preserves_native_error_and_existing_target_rows(
        string target
    )
    {
        // Arrange
        using var safety = new CancellationTokenSource();
        safety.CancelAfter(TimeSpan.FromMinutes(2));
        await using var store = await SpatialStore.CreateAsync(target, safety.Token);
        await store.SeedAsync(includeInvalidSrid: true, safety.Token);
        await ExecuteNativeAsync(store.Observer,
            $"INSERT INTO `{TargetTable}` (`Id`,`Location`) VALUES (999,{store.PointSql("9.5 10.75", 4326)});",
            safety.Token);

        var before = await store.ReadPointsAsync(TargetTable, safety.Token);
        var observation = new SpatialCommands();
        await using var context = store.CreateContext(observation);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .Set<SpatialSource>()
            .OrderBy(row => row.Id)
            .ExecuteInsertFromQueryAsync(context.Set<SpatialTarget>(),
                setters => setters
                    .SetProperty(row => row.Id, row => row.Id)
                    .SetProperty(row => row.Location, row => row.Location),
                safety.Token));

        var after = await store.ReadPointsAsync(TargetTable, safety.Token);
        var sourceSrid = await ReadScalarIntAsync(store.Observer,
            $"SELECT ST_SRID(`Location`) FROM `{SourceTable}` WHERE `Id`=2;",
            safety.Token);

        // Assert
        // WHY: MySQL enforces a native column SRID; MariaDB uses Doka's generated SRID CHECK.
        Assert.Equal(store.Version.IsMariaDb
                ? 4025
                : 3643,
            Assert.IsType<MySqlException>(failure)
                .Number);

        Assert.Equal(before, after);
        Assert.Equal(999,
            Assert.Single(after)
                .Id);

        Assert.Equal(4326,
            Assert.Single(after)
                .Srid);

        Assert.Equal(0, sourceSrid);
        Assert.Empty(context.ChangeTracker.Entries());
        AssertDirectSpatialStatement(observation, safety.Token);
    }

    /// <summary>Checks the actual writer command rather than a separately rendered or hypothetical SQL string.</summary>
    private static void AssertDirectSpatialStatement(
        SpatialCommands observation,
        CancellationToken cancellationToken
    )
    {
        var sql = Assert.Single(observation.Statements);
        Assert.StartsWith($"INSERT INTO `{TargetTable}` (`Id`, `Location`)", sql, StringComparison.Ordinal);
        Assert.Contains($"FROM `{SourceTable}`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ST_TRANSFORM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ST_SRID(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ST_GeomFrom", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, observation.Readers);
        Assert.Equal(cancellationToken, Assert.Single(observation.Tokens));
    }
}
