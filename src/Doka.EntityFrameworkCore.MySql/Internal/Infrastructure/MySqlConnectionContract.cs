namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlConnectionContract
{
    private const string AllowUserVariablesOption = "Allow User Variables";
    private const string GuidFormatOption = "Guid Format";

    public static string NormalizeProviderOwned(
        string connectionString,
        bool userVariablesRequired
    )
    {
        var builder = Parse(connectionString);

        ValidateMatchedRows(builder);
        ValidateOwnedGuidFormat(builder);

        if (userVariablesRequired)
        {
            if (builder.ContainsKey(AllowUserVariablesOption))
            {
                if (!builder.AllowUserVariables)
                {
                    throw CreateUserVariablesUnavailableFailure();
                }
            }
            else
            {
                builder.AllowUserVariables = true;
            }
        }

        if (string.IsNullOrWhiteSpace(builder.ApplicationName))
        {
            builder.ApplicationName = MySqlDiagnostics.DefaultDriverPoolName;
        }

        builder.GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16;

        return builder.ConnectionString;
    }

    public static void ValidateBorrowed(
        DbConnection connection,
        bool userVariablesRequired
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        string connectionString;

        try
        {
            connectionString = connection.ConnectionString;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or NotSupportedException)
        {
            throw CreateInvalidConnectionStringFailure();
        }

        ValidateBorrowed(connectionString, userVariablesRequired);
    }

    public static void ValidateBorrowed(
        MySqlDataSource dataSource,
        bool userVariablesRequired
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        ValidateBorrowed(dataSource.ConnectionString, userVariablesRequired);
    }

    private static void ValidateBorrowed(
        string connectionString,
        bool userVariablesRequired
    )
    {
        var builder = Parse(connectionString);

        ValidateMatchedRows(builder);

        if (builder.GuidFormat != MySqlConnector.MySqlGuidFormat.Binary16)
        {
            throw CreateGuidTransportIncompatibleFailure();
        }

        if (userVariablesRequired && !builder.AllowUserVariables)
        {
            throw CreateUserVariablesUnavailableFailure();
        }
    }

    private static MySqlConnectionStringBuilder Parse(
        string connectionString
    )
    {
        try
        {
            return new MySqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw CreateInvalidConnectionStringFailure();
        }
    }

    private static void ValidateMatchedRows(
        MySqlConnectionStringBuilder builder
    )
    {
        if (builder.UseAffectedRows)
        {
            throw CreateChangedRowSemanticsUnsupportedFailure();
        }
    }

    private static void ValidateOwnedGuidFormat(
        MySqlConnectionStringBuilder builder
    )
    {
        if (builder.OldGuids
            || (builder.ContainsKey(GuidFormatOption) && builder.GuidFormat != MySqlConnector.MySqlGuidFormat.Binary16))
        {
            throw CreateGuidTransportIncompatibleFailure();
        }
    }

    private static MySqlConnectionContractException CreateInvalidConnectionStringFailure() => new(
        MySqlConfigurationFailureReason.InvalidConnectionString,
        "The MySQL connection configuration is invalid.");

    private static MySqlConnectionContractException CreateChangedRowSemanticsUnsupportedFailure() => new(
        MySqlConfigurationFailureReason.ChangedRowSemanticsUnsupported,
        "Doka requires matched-row semantics. Set UseAffectedRows=false.");

    private static MySqlConnectionContractException CreateGuidTransportIncompatibleFailure() => new(
        MySqlConfigurationFailureReason.GuidTransportIncompatible,
        "Doka requires the MySqlConnector transport setting GuidFormat=Binary16.");

    private static MySqlConnectionContractException CreateUserVariablesUnavailableFailure() => new(
        MySqlConfigurationFailureReason.UserVariablesUnavailable,
        "This context requires server-side user variables. Enable AllowUserVariables=true.");
}

internal sealed class MySqlConnectionContractException : InvalidOperationException
{
    public MySqlConnectionContractException(
        MySqlConfigurationFailureReason reason,
        string message
    ) : base(message)
    {
        Reason = reason;
    }

    public MySqlConfigurationFailureReason Reason { get; }
}
