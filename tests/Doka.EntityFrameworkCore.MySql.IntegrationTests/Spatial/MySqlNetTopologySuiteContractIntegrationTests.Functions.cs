namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlNetTopologySuiteContractIntegrationTests
{
    /// <summary>Verifies the scalar validity function independently of geometry-returning functions.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MySql84,
            IntegrationDatabaseTarget.MySql97,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(FunctionTargetCases))]
    public async Task Supported_validity_function_executes(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateFunctionContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        var query = context.Rows.Select(row => row.LeftGeometry!.IsValid);

        // Act
        var actual = await query.SingleAsync().ConfigureAwait(false);

        // Assert
        Assert.True(actual);
    }

    /// <summary>Verifies one buffer or aggregate operation with the engine's supported overload.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MySql84,
            IntegrationDatabaseTarget.MySql97,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(GeometryFunctionCases))]
    public async Task Supported_geometry_function_executes(
        string target,
        string function
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        await using var context = await _fixture.CreateFunctionContextAsync(engine);
        var query = function switch
        {
            "Buffer" when engine == IntegrationDatabaseTarget.MariaDb123 =>
                context.Rows.Select(row => row.LeftGeometry!.Buffer(1)),
            "Buffer" => context.Rows.Select(row => row.LeftGeometry!.Buffer(1, 4)),
            "Combine" => context.Rows.GroupBy(_ => 1)
                .Select(group => NetTopologySuite.Geometries.Utilities.GeometryCombiner.Combine(
                    group.Select(row => row.LeftGeometry))),
            "Union" => context.Rows.GroupBy(_ => 1)
                .Select(group => NetTopologySuite.Operation.Union.UnaryUnionOp.Union(
                    group.Select(row => row.LeftGeometry))),
            "Envelope" => context.Rows.GroupBy(_ => 1)
                .Select(group => NetTopologySuite.Geometries.Utilities.EnvelopeCombiner.CombineAsGeometry(
                    group.Select(row => row.LeftGeometry))),
            _ => throw new ArgumentOutOfRangeException(nameof(function)),
        };

        // Act
        var actual = await query.SingleAsync().ConfigureAwait(false);

        // Assert
        Assert.False(actual.IsEmpty);
    }

    /// <summary>Verifies one nullable projection over the complete dimension/order matrix.</summary>
    [RequiresDatabaseTargetsTheory(
        new[]
        {
            IntegrationDatabaseTarget.MariaDb1011,
            IntegrationDatabaseTarget.MariaDb114,
            IntegrationDatabaseTarget.MariaDb118,
            IntegrationDatabaseTarget.MariaDb123
        })]
    [MemberData(nameof(MariaDbCases))]
    public async Task MariaDb_crosses_matches_nts_for_every_dimension_order(
        string target
    )
    {
        // Arrange
        await using var context = await _fixture.CreateCrossesContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));

        var cases = CreateCrossesCases();
        var expected = cases.Select(pair => (bool?)pair.Left.Crosses(pair.Right))
            .Concat([null, null, null]).ToArray();
        var query = context.Rows.OrderBy(row => row.Id)
            .Select(row => (bool?)row.LeftGeometry!.Crosses(row.RightGeometry!));

        // WHY: One server projection dispatches over all dimension orders and
        // both nullable operands; the complete result vector is the contract.
        // Act
        var actual = await query.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.Contains(true, expected);
        Assert.Contains(false, expected);
        Assert.Equal(3, expected.Count(result => result is null));
        Assert.Equal(expected, actual);
    }
}
