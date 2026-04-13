namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

internal static class IntegrationConnectionStringSettings
{
    public const string MySql80Variable = "DOKA_MYSQL80_CONNECTION_STRING";
    public const string MySql84Variable = "DOKA_MYSQL84_CONNECTION_STRING";
    public const string MariaDb114Variable = "DOKA_MARIADB114_CONNECTION_STRING";
    public const string MariaDb118Variable = "DOKA_MARIADB118_CONNECTION_STRING";

    public static string? MySql80ConnectionString => Environment.GetEnvironmentVariable(MySql80Variable);
    public static string? MySql84ConnectionString => Environment.GetEnvironmentVariable(MySql84Variable);
    public static string? MariaDb114ConnectionString => Environment.GetEnvironmentVariable(MariaDb114Variable);
    public static string? MariaDb118ConnectionString => Environment.GetEnvironmentVariable(MariaDb118Variable);
}
