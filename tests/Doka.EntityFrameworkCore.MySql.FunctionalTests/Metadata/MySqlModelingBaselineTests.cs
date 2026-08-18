namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies EF Core modeling features (inheritance, owned types, complex types,
/// relationships, discriminators) produce valid MySQL DDL and query SQL.
/// </summary>
public sealed partial class MySqlModelingBaselineTests
{
    // -- Helper ----------------------------------------------------------

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }
}
