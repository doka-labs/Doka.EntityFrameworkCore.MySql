namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlQueryTranslationIntegrationTests
{
    /// <summary>Verifies one DateTime arithmetic translation on the original MySQL 8.4 target.</summary>
    /// <param name="method">The independently reported DateTime method.</param>
    /// <param name="year">The expected result year.</param>
    /// <param name="month">The expected result month.</param>
    /// <param name="day">The expected result day.</param>
    /// <param name="hour">The expected result hour.</param>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql84 })]
    [InlineData("AddDays", 2025, 1, 11, 12)]
    [InlineData("AddMonths", 2025, 3, 1, 12)]
    [InlineData("AddYears", 2026, 1, 1, 12)]
    [InlineData("AddHours", 2025, 1, 1, 17)]
    public async Task DateTime_arithmetic_returns_expected_value(
        string method,
        int year,
        int month,
        int day,
        int hour
    )
    {
        // Arrange
        const IntegrationDatabaseTarget target = IntegrationDatabaseTarget.MySql84;
        var connectionString = GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));

        await SetupAsync(context);

        try
        {
            context.Items.Add(new QueryItem
            {
                Name = "dt",
                Category = "X",
                CreatedAt = new DateTime(2025, 1, 1, 12, 0, 0),
            });

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var query = method switch
            {
                "AddDays" => context.Items.Select(item => item.CreatedAt.AddDays(10)),
                "AddMonths" => context.Items.Select(item => item.CreatedAt.AddMonths(2)),
                "AddYears" => context.Items.Select(item => item.CreatedAt.AddYears(1)),
                "AddHours" => context.Items.Select(item => item.CreatedAt.AddHours(5)),
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown DateTime arithmetic method."),
            };

            // Act
            var result = await query.FirstAsync();

            // Assert
            Assert.Equal(new DateTime(year, month, day, hour, 0, 0), result);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }
}
