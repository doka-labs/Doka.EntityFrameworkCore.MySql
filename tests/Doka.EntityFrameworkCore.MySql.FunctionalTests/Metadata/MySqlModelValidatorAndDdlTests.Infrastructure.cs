namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the infrastructure cases and shared support.</summary>
public sealed partial class MySqlModelValidatorAndDdlTests
{
    // -- Helpers --

    private static DdlTestContext CreateMySqlContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DdlTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new DdlTestContext(builder.Options);
    }

    private static DdlTestContext CreateMariaDbContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DdlTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));

        return new DdlTestContext(builder.Options);
    }

    private static SchemaDdlTestContext CreateSchemaContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<SchemaDdlTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new SchemaDdlTestContext(builder.Options);
    }

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
