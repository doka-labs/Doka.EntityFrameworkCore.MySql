namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator : MigrationsSqlGenerator
{
    private const string PreviousDdlCommentSqlModeVariable = "@__doka_previous_sql_mode";
    private readonly MySqlMigrationOperationHandlerRegistry _operationHandlerRegistry;
    private DdlCommentSqlModeCommands? _ddlCommentSqlModeCommands;
    private MySqlMigrationFeatureSet? _migrationFeatures;
    private MySqlServerVersion? _serverVersion;
    private int _operationOrdinal = -1;

    private ProviderProfile Profile => ServerVersion.Profile;

    private MySqlServerVersion ServerVersion => _serverVersion ??= Dependencies
        .CurrentContext.Context.GetService<IDbContextOptions>()
        .FindExtension<MySqlOptionsExtension>()
        ?.ServerVersion
        ?? throw new InvalidOperationException(
            "The server version must be configured before migration SQL generation.");

    public MySqlMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        IEnumerable<IMySqlMigrationOperationHandler>? operationHandlers = null
    ) : base(dependencies)
    {
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
                engineFamily: null,
                errorType: exception.FailureCode.ToString());

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
    ) => GetProviderCommandBuilder(builder).BeginProviderScope(GetDdlCommentSqlModeCommands().Setup);

    private void AppendDdlCommentSqlModeScopeEnd(
        MigrationCommandListBuilder builder
    ) => GetProviderCommandBuilder(builder).CompleteProviderScope(GetDdlCommentSqlModeCommands().Cleanup);

    private DdlCommentSqlModeCommands GetDdlCommentSqlModeCommands()
    {
        var commands = _ddlCommentSqlModeCommands;
        if (commands is not null)
        {
            return commands;
        }

        var terminator = Dependencies.SqlGenerationHelper.StatementTerminator;
        var newLine = Environment.NewLine;
        var created = new DdlCommentSqlModeCommands(
            [
                // The server executes MySQL-family executable comments, while
                // MySqlConnector's parameter parser leaves their contents alone.
                // This keeps generated script text independent of AllowUserVariables.
                "/*! SET "
                + PreviousDdlCommentSqlModeVariable
                + " = @@SESSION.sql_mode */"
                + terminator
                + newLine,
                "/*! SET SESSION sql_mode = IF("
                + "FIND_IN_SET('NO_BACKSLASH_ESCAPES', @@SESSION.sql_mode), "
                + "@@SESSION.sql_mode, "
                + "CONCAT_WS(',', NULLIF(@@SESSION.sql_mode, ''), 'NO_BACKSLASH_ESCAPES')) */"
                + terminator
                + newLine,
            ],
            [
                "/*! SET SESSION sql_mode = "
                + PreviousDdlCommentSqlModeVariable
                + " */"
                + terminator
                + newLine,
            ]);

        return Interlocked.CompareExchange(ref _ddlCommentSqlModeCommands, created, null) ?? created;
    }

    private static MySqlMigrationCommandListBuilder GetProviderCommandBuilder(
        MigrationCommandListBuilder builder
    ) => builder as MySqlMigrationCommandListBuilder
        ?? throw new InvalidOperationException(
            "The Doka migrations generator requires its provider-owned command builder.");

    private static bool RequiresDdlCommentSqlModeScope(
        string? comment
    ) => comment?.Contains('\\') == true;

    private static bool RequiresDdlCommentSqlModeScope(
        CreateTableOperation operation
    ) => RequiresDdlCommentSqlModeScope(operation.Comment)
        || operation.Columns.Any(column => RequiresDdlCommentSqlModeScope(column.Comment));

    private sealed class DdlCommentSqlModeCommands(
        IReadOnlyList<string> setup,
        IReadOnlyList<string> cleanup
    )
    {
        public IReadOnlyList<string> Setup { get; } = setup;

        public IReadOnlyList<string> Cleanup { get; } = cleanup;
    }
}
