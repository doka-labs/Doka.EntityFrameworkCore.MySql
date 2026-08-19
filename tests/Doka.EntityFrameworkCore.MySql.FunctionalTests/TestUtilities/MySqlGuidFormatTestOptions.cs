namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Builds the intentionally isolated service-provider configurations used by
/// the Guid live contracts.
/// </summary>
internal static class MySqlGuidFormatTestOptions
{
    public static DbContextOptions<TContext> BuildOptions<TContext>(
        string baseConnectionString,
        string databaseName,
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext
    {
        var connectionString = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;

        return MySqlFunctionalTestOptions
            .CreateTransientBuilder<TContext>()
            .UseMySql(connectionString, serverVersion)
            .Options;
    }

    public static DbContextOptions<TContext> BuildDefaultChar36Options<TContext>(
        string connectionString,
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext => MySqlFunctionalTestOptions
        .CreateTransientBuilder<TContext>()
        .UseMySql(connectionString, serverVersion, options => options.DefaultGuidFormat(MySqlGuidFormat.Char36))
        .Options;

    public static DbContextOptions<TContext> BuildDefaultChar36Options<TContext>(
        DbConnection connection,
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext => MySqlFunctionalTestOptions
        .CreateTransientBuilder<TContext>()
        .UseMySql(connection, serverVersion, options => options.DefaultGuidFormat(MySqlGuidFormat.Char36))
        .Options;

    public static DbContextOptions<TContext> BuildDefaultChar36Options<TContext>(
        MySqlDataSource dataSource,
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext => MySqlFunctionalTestOptions
        .CreateTransientBuilder<TContext>()
        .UseMySql(dataSource, serverVersion, options => options.DefaultGuidFormat(MySqlGuidFormat.Char36))
        .Options;
}
