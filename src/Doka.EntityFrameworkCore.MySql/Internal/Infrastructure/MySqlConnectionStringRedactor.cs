namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlConnectionStringRedactor
{
    public static string Redact(
        string? connectionString
    )
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "<none>";
        }

        try
        {
            var builder = new MySqlConnectionStringBuilder(connectionString);

            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "***";
            }

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return "<redacted>";
        }
    }
}
