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
        foreach (DbParameter parameter in result.Parameters)
        {
            TruncateInputValue(parameter);
        }

        return result;
    }

    private static void TruncateInputValue(
        DbParameter parameter
    )
    {
        if (parameter.Size <= 0
            || parameter.Direction is not (ParameterDirection.Input or ParameterDirection.InputOutput))
        {
            return;
        }

        parameter.Value = parameter.Value switch
        {
            string value when value.Length > parameter.Size => value[..parameter.Size],
            byte[] value when value.Length > parameter.Size => value[..parameter.Size],
            char[] value when value.Length > parameter.Size => value[..parameter.Size],
            _ => parameter.Value,
        };
    }
}
