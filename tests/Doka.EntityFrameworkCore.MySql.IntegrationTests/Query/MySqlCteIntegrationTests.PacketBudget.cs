namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteIntegrationTests
{
    /// <summary>An empty anchor cannot conceal a finite cast whose worst-case encoded capacity exceeds the server budget.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Empty_recursive_text_rejects_an_unsafe_native_packet_capacity(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        var limits = await store.ReadNativeBudgetsAsync(safety.Token);
        var depth = Math.Min(limits.RecursionDepth - 1, 999);

        if (depth <= 0)
        {
            throw new InvalidOperationException("The native fixture requires a positive safe recursion budget.");
        }

        var suffixLength = checked((int)(limits.PacketBytes / 4 / depth + 1));
        var step = ConstantTextGrowth(new string('z', suffixLength));
        await using var context = store.CreateContext();
        var query = context
            .Rows
            .Where(row => row.Id == -1)
            .Select(row => EF.Functions.Collate(row.Label, "utf8mb4_unicode_ci"))
            .WithRecursiveCte(step, rows => rows, depth);

        // Act
        var failure = await Record.ExceptionAsync(() => query.ToArrayAsync(safety.Token));

        // Assert
        Assert.Contains("recursive text capacity exceeds the active server packet limit",
            Assert.IsType<InvalidOperationException>(failure)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Represents a genuine structural literal rather than an unprovable runtime string parameter.</summary>
    private static Expression<Func<IQueryable<string>, IQueryable<string>>> ConstantTextGrowth(
        string suffix
    )
    {
        // WHY: The public contract proves literal growth at translation time. An ordinary
        // captured string would become a SQL parameter and must remain an unknown bound.
        var value = Expression.Parameter(typeof(string), "value");
        var selector = Expression.Lambda<Func<string, string>>(
            Expression.Call(typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!,
                value,
                Expression.Constant(suffix)),
            value);

        var frontier = Expression.Parameter(typeof(IQueryable<string>), "frontier");

        return Expression.Lambda<Func<IQueryable<string>, IQueryable<string>>>(
            Expression.Call(QueryableMethods.Select.MakeGenericMethod(typeof(string), typeof(string)),
                frontier,
                Expression.Quote(selector)),
            frontier);
    }
}
