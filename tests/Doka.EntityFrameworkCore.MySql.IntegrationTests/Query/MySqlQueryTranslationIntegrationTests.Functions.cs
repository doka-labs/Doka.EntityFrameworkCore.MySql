namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlQueryTranslationIntegrationTests
{
    /// <summary>Verifies the named aggregate or regular-expression translation on the selected engine.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Group_concat_executes_on_mysql84() => await RunGroupConcatTest(IntegrationDatabaseTarget.MySql84);

    /// <summary>Verifies the named aggregate or regular-expression translation on the selected engine.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Group_concat_executes_on_mariadb118() =>
        await RunGroupConcatTest(IntegrationDatabaseTarget.MariaDb118);

    private static async Task RunGroupConcatTest(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));
        await SetupAsync(context);

        try
        {
            context.Items.Add(
                new QueryItem
                {
                    Name = "Alpha",
                    Value = 1,
                    Category = "G1",
                    CreatedAt = DateTime.Now
                });
            context.Items.Add(
                new QueryItem
                {
                    Name = "Beta",
                    Value = 2,
                    Category = "G1",
                    CreatedAt = DateTime.Now
                });
            context.Items.Add(
                new QueryItem
                {
                    Name = "Gamma",
                    Value = 3,
                    Category = "G2",
                    CreatedAt = DateTime.Now
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Act
            var groups = await context
                .Items.GroupBy(e => e.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Names = string.Join(",", g.Select(e => e.Name))
                })
                .OrderBy(g => g.Category)
                .ToListAsync();

            // Assert
            Assert.Equal(2, groups.Count);
            Assert.Contains("Alpha", groups[0].Names, StringComparison.Ordinal);
            Assert.Contains("Beta", groups[0].Names, StringComparison.Ordinal);
            Assert.Contains("Gamma", groups[1].Names, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }

    /// <summary>Verifies the named aggregate or regular-expression translation on the selected engine.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Regexp_executes_on_mysql84() => await RunRegexpTest(IntegrationDatabaseTarget.MySql84);

    /// <summary>Verifies the named aggregate or regular-expression translation on the selected engine.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task Regexp_executes_on_mariadb118() => await RunRegexpTest(IntegrationDatabaseTarget.MariaDb118);

    private static async Task RunRegexpTest(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = GetConnectionString(target);
        await using var context = new QueryContext(CreateOptions(connectionString, target));
        await SetupAsync(context);

        try
        {
            context.Items.Add(
                new QueryItem
                {
                    Name = "abc123",
                    Value = 0,
                    Category = "R",
                    CreatedAt = DateTime.Now
                });
            context.Items.Add(
                new QueryItem
                {
                    Name = "xyz",
                    Value = 0,
                    Category = "R",
                    CreatedAt = DateTime.Now
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Act
            var matches = await context
                .Items.Where(e => EF.Functions.Regexp(e.Name, "[0-9]+"))
                .CountAsync();

            // Assert
            Assert.Equal(1, matches);
        }
        finally
        {
            await CleanupAsync(context);
        }
    }
}
