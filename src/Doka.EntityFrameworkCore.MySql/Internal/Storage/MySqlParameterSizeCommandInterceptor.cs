namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Enforces the ADO.NET <see cref="DbParameter.Size"/> contract for input
/// values before MySqlConnector sends the command.
/// </summary>
/// <remarks>
/// MySqlConnector retains <see cref="DbParameter.Size"/> as metadata but sends
/// the complete input value. Truncating the command-local parameter clone keeps
/// raw and generated parameters consistent with EF Core's relational contract
/// without mutating the parameter supplied by the application.
/// </remarks>
internal sealed class MySqlParameterSizeCommandInterceptor : DbCommandInterceptor
{
    public override DbCommand CommandInitialized(
        CommandEndEventData eventData,
        DbCommand result
    )
    {
        for (var index = 0; index < result.Parameters.Count; index++)
        {
            TruncateInputValue(result.Parameters[index]);
        }

        return result;
    }

    internal static void TruncateInputValue(
        DbParameter parameter
    )
    {
        if (parameter.Size <= 0
            || parameter.Direction is not (ParameterDirection.Input or ParameterDirection.InputOutput))
        {
            return;
        }

        var value = parameter.Value;
        switch (value)
        {
            case string text when text.Length > parameter.Size:
                parameter.Value = text[..parameter.Size];
                break;
            case byte[] bytes when bytes.Length > parameter.Size:
                parameter.Value = bytes[..parameter.Size];
                break;
            case char[] characters when characters.Length > parameter.Size:
                parameter.Value = characters[..parameter.Size];
                break;
        }
    }
}
