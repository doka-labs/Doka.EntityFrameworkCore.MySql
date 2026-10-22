namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Shares the infrastructure responsibility without changing the test fixture.</summary>
public sealed partial class MySqlQueryTranslationCoverageTests
{
    // -- Helpers --

    private static CoverageContext CreateContext(
        bool isMariaDb = false
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<CoverageContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            isMariaDb
                ? MySqlServerVersion.MariaDb(new Version(11, 4, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new CoverageContext(builder.Options);
    }
}
