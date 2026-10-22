namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Centralizes context creation and schema ownership for the test class.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    // WHY: The factories stay in this file because the caller path owns the
    // isolated schema, including calls made from the class fixture.
    /// <summary>Creates the binary-default context in the class-owned schema.</summary>
    private static PrimitiveCollectionContext CreateContext(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<PrimitiveCollectionContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Binary16));

        return new PrimitiveCollectionContext(builder.Options);
    }

    /// <summary>Creates the Char36-default context in the same class-owned schema.</summary>
    private static CharGuidCollectionContext CreateCharContext(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<CharGuidCollectionContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Char36));

        return new CharGuidCollectionContext(builder.Options);
    }

    /// <summary>Creates the isolated SQL-mode context with optional connector escaping.</summary>
    private static SqlModeStringCollectionContext CreateSqlModeContext(
        bool configureConnector = false
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);

        if (configureConnector)
        {
            connectionString = new MySqlConnectionStringBuilder(connectionString)
            {
                NoBackslashEscapes = true,
            }.ConnectionString;
        }

        var builder = IntegrationTestDbContextOptions.Create<SqlModeStringCollectionContext>();
        builder.UseMySql(
            connectionString,
            IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb118));

        return new SqlModeStringCollectionContext(builder.Options);
    }

    private static async Task<int[]> SelectIdsAsync(
        IQueryable<PrimitiveCollectionEntity> query
    ) => await query
        .OrderBy(item => item.Id)
        .Select(item => item.Id)
        .ToArrayAsync(CancellationToken.None);

    private static Guid CreateGuid(
        int value
    ) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static async Task DropTableAsync(
        PrimitiveCollectionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{TableName}`;",
        CancellationToken.None);

    private static async Task DropCharTableAsync(
        CharGuidCollectionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{CharTableName}`;",
        CancellationToken.None);

    private static async Task DropSqlModeTableAsync(
        SqlModeStringCollectionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{SqlModeTableName}`;",
        CancellationToken.None);
}
