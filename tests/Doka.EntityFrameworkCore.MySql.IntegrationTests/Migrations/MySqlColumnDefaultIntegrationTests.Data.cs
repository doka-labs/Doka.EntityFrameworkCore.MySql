namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    /// <summary>Gets independently selectable history-failure paths for every selected supported engine.</summary>
    public static IEnumerable<object[]> RuntimeFailureCases => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment.GetSupportedTargets().Contains(target))
        .SelectMany(target => new[]
        {
            new object[] { target.ToString(), true },
            new object[] { target.ToString(), false },
        });

    /// <summary>Gets native MySQL temporal-default syntax rejection cases.</summary>
    public static IEnumerable<object[]> InvalidTemporalDefaultCases => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => target is IntegrationDatabaseTarget.MySql84 or IntegrationDatabaseTarget.MySql97)
        .SelectMany(target => new[]
        {
            new object[] { target.ToString(), "date", "DATE '2026-08-17'" },
            new object[] { target.ToString(), "time(6)", "TIME '12:34:56.123456'" },
        });
}
