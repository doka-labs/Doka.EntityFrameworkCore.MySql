namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>A same-type integer SUM retains the input converter exactly as EF's ordinary aggregate does.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_integer_window_sum_preserves_the_ordinary_aggregate_mapping(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await SeedNumericTargetValuesAsync(context, safety.Token);
        var source = context
            .Set<WindowTarget>()
            .Where(row => row.Id <= 2);

        // WHY: The independent ordinary aggregate is the established EF mapping contract,
        // not an expected value derived from the new window's generated SQL.
        var ordinary = await source.SumAsync(row => row.ConvertedInt, safety.Token);
        var query = source
            .OrderBy(row => row.Id)
            .Select(row => EF
                .Functions
                .Window()
                .Sum(row.ConvertedInt));

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(3, ordinary);
        Assert.Equal([3, 3], values);
    }

    /// <summary>A decimal SUM retains conversion and precision instead of exposing the raw provider units.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_decimal_window_sum_preserves_the_ordinary_aggregate_mapping(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await SeedNumericTargetValuesAsync(context, safety.Token);
        var source = context
            .Set<WindowTarget>()
            .Where(row => row.Id <= 2);

        var ordinary = await source.SumAsync(row => row.ConvertedDecimal, safety.Token);
        var query = source
            .OrderBy(row => row.Id)
            .Select(row => EF
                .Functions
                .Window()
                .Sum(row.ConvertedDecimal));

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(3m, ordinary);
        Assert.Equal([3m, 3m], values);
    }

    /// <summary>A same-type decimal AVG retains its input converter rather than returning tenfold provider units.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_decimal_window_average_preserves_the_ordinary_aggregate_mapping(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await SeedNumericTargetValuesAsync(context, safety.Token);
        var source = context
            .Set<WindowTarget>()
            .Where(row => row.Id <= 2);

        var ordinary = await source.AverageAsync(row => row.ConvertedDecimal, safety.Token);
        var query = source
            .OrderBy(row => row.Id)
            .Select(row => EF
                .Functions
                .Window()
                .Average(row.ConvertedDecimal));

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(1.5m, ordinary);
        Assert.Equal([1.5m, 1.5m], values);
    }

    /// <summary>An integer AVG deliberately changes CLR type, so it follows EF's floating input conversion instead of an incompatible integer converter.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Converted_integer_window_average_preserves_the_distinct_floating_result_mapping(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await WindowStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await SeedNumericTargetValuesAsync(context, safety.Token);
        var source = context
            .Set<WindowTarget>()
            .Where(row => row.Id <= 2);

        var ordinary = await source.AverageAsync(row => row.ConvertedInt, safety.Token);
        var query = source
            .OrderBy(row => row.Id)
            .Select(row => EF
                .Functions
                .Window()
                .Average(row.ConvertedInt));

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal(15d, ordinary);
        Assert.Equal([15d, 15d], values);
    }

    /// <summary>Seeds known provider units without exercising EF's converter as part of the write setup.</summary>
    private static Task<int> SeedNumericTargetValuesAsync(
        WindowContext context,
        CancellationToken cancellationToken
    ) => context.Database.ExecuteSqlRawAsync(
        "UPDATE `Window targets` SET `ConvertedInt`=`Id`*10,`ConvertedDecimal`=`Id`*10;",
        cancellationToken);
}
