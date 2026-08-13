namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator : MigrationsSqlGenerator
{
    private const string PreviousDdlCommentSqlModeVariable = "@__doka_previous_sql_mode";
    private readonly MySqlSingletonOptions _mySqlSingletonOptions;
    private readonly MySqlMigrationFeatureSet _migrationFeatures;
    private readonly MySqlMigrationOperationHandlerRegistry _operationHandlerRegistry;
    private int _operationOrdinal = -1;

    private ProviderProfile Profile => _mySqlSingletonOptions.Profile
        ?? throw new InvalidOperationException(
            "The provider profile must be initialized before migration SQL generation.");

    public MySqlMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        IEnumerable<ISingletonOptions> singletonOptions,
        IEnumerable<IMySqlMigrationOperationHandler>? operationHandlers = null
    ) : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _mySqlSingletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
        _migrationFeatures = new MySqlMigrationFeatureSet(Profile);

        try
        {
            _operationHandlerRegistry = new MySqlMigrationOperationHandlerRegistry(
                operationHandlers ?? []);
        }
        catch (MySqlMigrationOperationHandlerException exception)
        {
            MySqlLoggerMessages.InvalidMigrationOperationHandlerRegistration(
                Dependencies.MigrationsLogger.Logger,
                exception);

            var operationType = exception.OperationType;
            var tags = CreateHandlerMetricTags(
                exception.HandlerId ?? "unknown",
                operationType,
                "default",
                "invalid_registration",
                Profile.Engine.Family,
                exception.FailureCode.ToString());

            MySqlMeter.MigrationOperationHandlerContractViolationsTotal.Add(1, tags);

            throw;
        }
    }

    private string DelimitMigrationIdentifier(
        string identifier
    ) => Dependencies.SqlGenerationHelper.DelimitIdentifier(identifier);

    private string DelimitMigrationIdentifier(
        string identifier,
        string? schema
    ) => Dependencies.SqlGenerationHelper.DelimitIdentifier(identifier, schema);

    private void AppendDdlCommentSqlModeScopeStart(
        MigrationCommandListBuilder builder
    )
    {
        var terminator = Dependencies.SqlGenerationHelper.StatementTerminator;

        builder
            // The server executes MySQL-family executable comments, while
            // MySqlConnector's parameter parser leaves their contents alone.
            // This keeps the generated SQL independent of Allow User Variables.
            .Append("/*! SET ")
            .Append(PreviousDdlCommentSqlModeVariable)
            .Append(" = @@SESSION.sql_mode")
            .Append(" */")
            .AppendLine(terminator)
            .Append("/*! SET SESSION sql_mode = IF(")
            .Append("FIND_IN_SET('NO_BACKSLASH_ESCAPES', @@SESSION.sql_mode), ")
            .Append("@@SESSION.sql_mode, ")
            .Append("CONCAT_WS(',', NULLIF(@@SESSION.sql_mode, ''), 'NO_BACKSLASH_ESCAPES'))")
            .Append(" */")
            .AppendLine(terminator);
    }

    private void AppendDdlCommentSqlModeScopeEnd(
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("/*! SET SESSION sql_mode = ")
            .Append(PreviousDdlCommentSqlModeVariable)
            .Append(" */")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
    }

    private static bool RequiresDdlCommentSqlModeScope(
        string? comment
    ) => comment?.Contains('\\') == true;

    private static bool RequiresDdlCommentSqlModeScope(
        CreateTableOperation operation
    ) => RequiresDdlCommentSqlModeScope(operation.Comment)
        || operation.Columns.Any(column => RequiresDdlCommentSqlModeScope(column.Comment));
}
