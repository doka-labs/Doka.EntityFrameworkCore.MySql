namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlQueryTranslationIntegrationTests
{
    /// <summary>Verifies one math translation on the original MySQL 8.4 target.</summary>
    /// <param name="method">The independently reported math operation.</param>
    /// <param name="expected">The expected scalar result.</param>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql84 })]
    [InlineData("Abs", 16.7)]
    [InlineData("Ceiling", -16.0)]
    [InlineData("Floor", -17.0)]
    [InlineData("Sqrt", 4.08656334834051)]
    [InlineData("Round", -17.0)]
    public async Task Math_function_returns_expected_value(
        string method,
        double expected
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
                Name = "m",
                Value = -16.7,
                Category = "X",
                CreatedAt = new DateTime(2025, 1, 1),
            });

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var query = method switch
            {
                "Abs" => context.Items.Select(item => Math.Abs(item.Value)),
                "Ceiling" => context.Items.Select(item => Math.Ceiling(item.Value)),
                "Floor" => context.Items.Select(item => Math.Floor(item.Value)),
                "Sqrt" => context.Items.Select(item => Math.Sqrt(Math.Abs(item.Value))),
                "Round" => context.Items.Select(item => Math.Round(item.Value, 0)),
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown math method."),
            };

            // Act
            var result = await query.FirstAsync();

            // Assert
            Assert.Equal(expected, result, 1);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }
}
