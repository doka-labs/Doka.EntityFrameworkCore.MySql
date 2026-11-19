namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteIntegrationTests
{
    /// <summary>Separates strict and permissive storage behavior rather than assuming truncation always throws.</summary>
    public static TheoryData<string, bool> TextModes
    {
        get
        {
            var cases = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                cases.Add(target, false);
                cases.Add(target, true);
            }

            return cases;
        }
    }

    /// <summary>Widened anchor and outer-parameter mappings preserve text beyond the original physical column capacity.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TextModes))]
    public async Task Recursive_text_growth_preserves_exact_values_and_outer_comparison(
        string target,
        bool strict
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        var anchor = new string('x', 80);
        await store.SetAnchorLabelAsync(anchor, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.OpenConnectionAsync(safety.Token);

        // WHY: This nonpooled fixture owns its physical session. Both modes must prove
        // exact text, since a native permissive truncation can succeed without warnings.
        await context.Database.ExecuteSqlRawAsync(strict
                ? "SET SESSION sql_mode='STRICT_ALL_TABLES';"
                : "SET SESSION sql_mode='';",
            safety.Token);

        var expected = anchor + "zzz";
        var query = context
            .Rows
            .Where(row => row.Id == 1)
            .Select(row => EF.Functions.Collate(row.Label, "utf8mb4_unicode_ci"))
            .WithRecursiveCte(frontier => frontier.Select(value => value + "z"),
                rows => rows.Where(value => value == expected),
                3);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([expected], values);
        Assert.Equal(83,
            Assert.Single(values)
                .Length);

        Assert.Contains("CHAR(83)", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Recursive comparison retains native Unicode collation instead of inheriting a cast's connection default.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TextModes))]
    public async Task Recursive_unicode_growth_preserves_scalar_characters_and_collation(
        string target,
        bool strict
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await CteStore.CreateAsync(target, safety.Token);
        var anchor = new string('x', 79) + "\ud83d\ude00";
        await store.SetAnchorLabelAsync(anchor, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.OpenConnectionAsync(safety.Token);
        await context.Database.ExecuteSqlRawAsync(strict
                ? "SET SESSION sql_mode='STRICT_ALL_TABLES';"
                : "SET SESSION sql_mode='';",
            safety.Token);

        var expected = anchor + "\ud83d\ude00\ud83d\ude00";
        var query = context
            .Rows
            .Where(row => row.Id == 1)
            .Select(row => EF.Functions.Collate(row.Label, "utf8mb4_unicode_ci"))
            .WithRecursiveCte(frontier => frontier.Select(value => value + "\ud83d\ude00"),
                rows => rows.Where(value => value == expected),
                2);

        // Act
        var values = await query.ToArrayAsync(safety.Token);

        // Assert
        Assert.Equal([expected], values);
        Assert.Contains("CHAR(82)", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("utf8mb4_unicode_ci", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }
}
