namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlQueryTranslationIntegrationTests
{
    /// <summary>Returns independently selectable string-predicate cases on the original engines.</summary>
    public static IEnumerable<object[]> StringPredicateCases => SelectedStringTargets.SelectMany(target =>
        new[]
        {
            new object[] { target.ToString(), "Contains", 2 },
            new object[] { target.ToString(), "StartsWith", 1 },
            new object[] { target.ToString(), "EndsWith", 1 },
        });

    /// <summary>Returns independently selectable string-projection cases on the original engines.</summary>
    public static IEnumerable<object[]> StringProjectionCases => SelectedStringTargets.SelectMany(target =>
        new[]
        {
            new object[] { target.ToString(), "ToUpper", "  HELLO WORLD  " },
            new object[] { target.ToString(), "ToLower", "goodbye" },
            new object[] { target.ToString(), "Trim", "Hello World" },
            new object[] { target.ToString(), "Substring", "bye" },
            new object[] { target.ToString(), "Replace", "See you bye" },
        });

    /// <summary>Returns independently selectable numeric string projections on the original engines.</summary>
    public static IEnumerable<object[]> StringNumericCases => SelectedStringTargets.SelectMany(target =>
        new[]
        {
            new object[] { target.ToString(), "IndexOf", 8 },
            new object[] { target.ToString(), "Length", 7 },
        });

    private static IEnumerable<IntegrationDatabaseTarget> SelectedStringTargets => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => target is IntegrationDatabaseTarget.MySql84
            or IntegrationDatabaseTarget.MySql97
            or IntegrationDatabaseTarget.MariaDb1011
            or IntegrationDatabaseTarget.MariaDb118
            or IntegrationDatabaseTarget.MariaDb123);

    /// <summary>Verifies one string predicate against real persisted values.</summary>
    /// <param name="targetName">The original eligible engine selected for this case.</param>
    /// <param name="method">The independently reported string method.</param>
    /// <param name="expectedCount">The number of rows matched by that predicate.</param>
    [RequiresDatabaseTargetsTheory(
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(StringPredicateCases))]
    public async Task String_predicate_matches_expected_rows(
        string targetName,
        string method,
        int expectedCount
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var connectionString = GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));

        await SetupAsync(context);

        try
        {
            await SeedStringItemsAsync(context);

            var query = method switch
            {
                "Contains" => context.Items.Where(item => item.Name.Contains("Hello")),
                "StartsWith" => context.Items.Where(item => item.Name.StartsWith("Good")),
                "EndsWith" => context.Items.Where(item => item.Name.EndsWith("Again")),
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown string predicate."),
            };

            // Act
            var result = await query.CountAsync();

            // Assert
            Assert.Equal(expectedCount, result);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    /// <summary>Verifies one string projection against a real persisted value.</summary>
    /// <param name="targetName">The original eligible engine selected for this case.</param>
    /// <param name="method">The independently reported string method.</param>
    /// <param name="expected">The complete expected server result.</param>
    [RequiresDatabaseTargetsTheory(
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(StringProjectionCases))]
    public async Task String_projection_returns_expected_value(
        string targetName,
        string method,
        string expected
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var connectionString = GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));

        await SetupAsync(context);

        try
        {
            await SeedStringItemsAsync(context);

            // WHY: These calls are expression-tree nodes translated to SQL;
            // the CLR culture is never consulted by UPPER()/LOWER().
#pragma warning disable CA1304, CA1311
            var query = method switch
            {
                "ToUpper" => context.Items.Where(item => item.Id == 1).Select(item => item.Name.ToUpper()),
                "ToLower" => context.Items.Where(item => item.Id == 2).Select(item => item.Name.ToLower()),
                "Trim" => context.Items.Where(item => item.Id == 1).Select(item => item.Name.Trim()),
                "Substring" => context.Items.Where(item => item.Id == 2).Select(item => item.Name.Substring(4)),
                "Replace" => context.Items.Where(item => item.Id == 2).Select(item => item.Name.Replace("Good", "See you ")),
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown string projection."),
            };
#pragma warning restore CA1304, CA1311

            // Act
            var result = await query.FirstAsync();

            // Assert
            Assert.Equal(expected, result);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    /// <summary>Verifies one numeric string projection against a real persisted value.</summary>
    /// <param name="targetName">The original eligible engine selected for this case.</param>
    /// <param name="method">The independently reported string member.</param>
    /// <param name="expected">The expected zero-based position or character count.</param>
    [RequiresDatabaseTargetsTheory(
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(StringNumericCases))]
    public async Task String_numeric_projection_returns_expected_value(
        string targetName,
        string method,
        int expected
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var connectionString = GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));

        await SetupAsync(context);

        try
        {
            await SeedStringItemsAsync(context);

            var query = method switch
            {
                "IndexOf" => context.Items.Where(item => item.Id == 1)
                    .Select(item => item.Name.IndexOf("World", StringComparison.Ordinal)),
                "Length" => context.Items.Where(item => item.Id == 2).Select(item => item.Name.Length),
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown numeric string projection."),
            };

            // Act
            var result = await query.FirstAsync();

            // Assert
            Assert.Equal(expected, result);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    private static async Task SeedStringItemsAsync(
        QueryContext context
    )
    {
        context.Items.AddRange(
            new QueryItem
            {
                Name = "  Hello World  ",
                Value = 42.5,
                Category = "A",
                CreatedAt = new DateTime(2025, 6, 15, 10, 30, 0),
            },
            new QueryItem
            {
                Name = "Goodbye",
                Value = 7.0,
                Category = "A",
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0),
            },
            new QueryItem
            {
                Name = "Hello Again",
                Value = -3.0,
                Category = "B",
                CreatedAt = new DateTime(2024, 12, 25, 18, 0, 0),
            });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}
