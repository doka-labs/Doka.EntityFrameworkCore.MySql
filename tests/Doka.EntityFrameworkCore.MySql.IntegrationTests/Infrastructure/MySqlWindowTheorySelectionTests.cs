namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Checks the actual correlated-window theory declarations without opening a database.</summary>
public sealed class MySqlWindowTheorySelectionTests
{
    /// <summary>Matches each family's native evidence to the current isolated target selection.</summary>
    /// <param name="methodName">The actual correlated-window test method.</param>
    /// <param name="dataName">The actual target data property used by that method.</param>
    /// <param name="mariaDb">Whether the case covers the MariaDB engine boundary.</param>
    [Theory]
    [InlineData(
        nameof(MySqlWindowFunctionIntegrationTests
            .Correlated_split_window_keeps_its_qualified_mysql_scope_without_n_plus_one),
        nameof(MySqlWindowFunctionIntegrationTests.CorrelatedWindowTargets),
        false)]
    [InlineData(
        nameof(MySqlWindowFunctionIntegrationTests.Correlated_split_window_reports_the_mariadb_lateral_boundary),
        nameof(MySqlWindowFunctionIntegrationTests.UnsupportedCorrelatedWindowTargets),
        true)]
    public void Correlated_window_theory_eligibility_matches_its_selected_family(
        string methodName,
        string dataName,
        bool mariaDb
    )
    {
        // Arrange
        var testClass = typeof(MySqlWindowFunctionIntegrationTests);
        var expectedTargets = IntegrationTestEnvironment
            .GetSelectedTargets()
            .Where(target => IntegrationTestEnvironment
                .GetSupportedTargets()
                .Contains(target))
            .Where(target => IntegrationTestEnvironment.GetServerVersion(target)
                    .IsMariaDb
                == mariaDb)
            .Select(target => target.ToString())
            .ToArray();

        // Act
        var attributes = testClass.GetMethod(methodName)!
            .GetCustomAttributes(typeof(RequiresDatabaseTargetsTheoryAttribute), false);

        // WHY: Materialize the typed view so xUnit's comparer cannot use TheoryData's object-array rows.
        IEnumerable<string> data = (TheoryData<string>)testClass.GetProperty(dataName)!.GetValue(null)!;
        var actualTargets = data.ToArray();

        // Assert
        // WHY: Checking the actual attribute type catches a return to the broad
        // supported-engine attribute, which would fail on empty family MemberData.
        var attribute = Assert.IsType<RequiresDatabaseTargetsTheoryAttribute>(Assert.Single(attributes));
        Assert.Equal(expectedTargets.Length == 0, attribute.Skip is not null);
        Assert.Equal(expectedTargets, actualTargets);
    }
}
