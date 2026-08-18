namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Orchestrates the per-aspect scaffolding loaders. The previous monolith bundled the
/// seven INFORMATION_SCHEMA queries and the MariaDB JSON_VALID detection in one 800+
/// LOC class; this orchestrator delegates each query to a dedicated loader under
/// <c>Internal/Scaffolding/Loaders/</c> and threads the per-call state through a
/// <see cref="ScaffoldingPipelineContext"/>. The cross-service hand-off for the
/// detected server version and NetTopologySuite-usage flag stays in
/// <see cref="MySqlScaffoldingContext"/> per ADR D-005.
/// </summary>
internal sealed class MySqlDatabaseModelFactory : IDatabaseModelFactory
{
    private readonly IMySqlDriverFacade _driverFacade;
    private readonly MySqlScaffoldingContext _scaffoldingContext;
    private readonly ILogger? _logger;

    public MySqlDatabaseModelFactory(
        IMySqlDriverFacade driverFacade,
        MySqlScaffoldingContext scaffoldingContext,
        ILoggerFactory? loggerFactory = null
    )
    {
        _driverFacade = driverFacade ?? throw new ArgumentNullException(nameof(driverFacade));
        _scaffoldingContext = scaffoldingContext ?? throw new ArgumentNullException(nameof(scaffoldingContext));
        _logger = loggerFactory?.CreateLogger(MySqlLoggerCategory.Scaffolding);
    }

    public DatabaseModel Create(
        string connectionString,
        DatabaseModelFactoryOptions options
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        using var connection = _driverFacade.CreateConnection(connectionString);

        return Create(connection, options);
    }

    public DatabaseModel Create(
        DbConnection connection,
        DatabaseModelFactoryOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            connection.Open();
        }

        string? initialDatabaseName = null;

        try
        {
            _scaffoldingContext.Begin();

            var rawServerVersion = ScaffoldingHelpers.ExecuteScalarString(connection, "SELECT VERSION();");
            var serverVersion = MySqlServerVersion.Parse(rawServerVersion);

            _scaffoldingContext.SetDetectedServerVersionText(rawServerVersion);

            var databaseName = ScaffoldingHelpers.ExecuteScalarString(connection, "SELECT DATABASE();");
            initialDatabaseName = databaseName;
            var requestedDatabaseNames = options
                .Schemas.Where(schema => !string.IsNullOrWhiteSpace(schema))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var databaseNames = requestedDatabaseNames.Length == 0
                ? [databaseName]
                : requestedDatabaseNames;
            var databaseCollation = ScaffoldingHelpers.ExecuteScalarString(
                connection,
                """
                SELECT DEFAULT_COLLATION_NAME
                FROM information_schema.SCHEMATA
                WHERE SCHEMA_NAME = DATABASE();
                """);

            var databaseModel = new DatabaseModel
            {
                DatabaseName = databaseName,
                Collation = databaseCollation,
            };

            var databaseCharSet = ScaffoldingHelpers.DeriveCharSetFromCollation(databaseCollation);

            if (!string.IsNullOrWhiteSpace(databaseCharSet))
            {
                databaseModel.SetAnnotation(MySqlAnnotationNames.CharSet, databaseCharSet);
            }

            var tableFilter = TableFilter.For(options.Tables);
            var databaseTables =
                new Dictionary<(string DatabaseName, string TableName), DatabaseTable>();
            var databaseColumns =
                new Dictionary<(string DatabaseName, string TableName, string ColumnName), DatabaseColumn>();
            var temporalHistoryTables = new HashSet<(string DatabaseName, string TableName)>();
            var pipelineContexts = new List<ScaffoldingPipelineContext>(databaseNames.Length);

            foreach (var selectedDatabaseName in databaseNames)
            {
                ChangeDatabase(connection, selectedDatabaseName);

                var mariaDbJsonColumns = serverVersion.Profile.GetSupport(ProviderCapability.JsonColumns)
                    == ProviderSupportStatus.Emulated
                    ? JsonCheckConstraintLoader.Load(connection, tableFilter)
                    : new HashSet<(string, string)>();
                var pipelineContext = new ScaffoldingPipelineContext(
                    connection,
                    databaseModel,
                    tableFilter,
                    serverVersion.Profile,
                    mariaDbJsonColumns,
                    selectedDatabaseName,
                    requestedDatabaseNames.Length > 0,
                    databaseTables,
                    databaseColumns,
                    temporalHistoryTables);

                TableLoader.Load(pipelineContext);
                ColumnLoader.Load(pipelineContext);
                TemporalTableLoader.Load(pipelineContext);
                SequenceLoader.Load(pipelineContext);
                PrimaryKeyLoader.Load(pipelineContext);
                UniqueConstraintLoader.Load(pipelineContext);
                IndexLoader.Load(pipelineContext);
                ApplicationTimeTableLoader.Load(pipelineContext);
                SpatialColumnLoader.Load(pipelineContext);
                CheckConstraintLoader.Load(pipelineContext);
                pipelineContexts.Add(pipelineContext);
            }

            // Foreign keys are loaded after every selected database so references
            // across MySQL database qualifiers resolve regardless of selection order.
            foreach (var pipelineContext in pipelineContexts)
            {
                ChangeDatabase(connection, pipelineContext.DatabaseName);
                ForeignKeyLoader.Load(pipelineContext, _logger);
            }

            return databaseModel;
        }
        finally
        {
            if (connection.State == ConnectionState.Open
                && !string.IsNullOrWhiteSpace(initialDatabaseName))
            {
                ChangeDatabase(connection, initialDatabaseName);
            }

            if (shouldCloseConnection)
            {
                connection.Close();
            }
        }
    }

    private static void ChangeDatabase(
        DbConnection connection,
        string databaseName
    )
    {
        if (!string.Equals(connection.Database, databaseName, StringComparison.Ordinal))
        {
            connection.ChangeDatabase(databaseName);
        }
    }
}
