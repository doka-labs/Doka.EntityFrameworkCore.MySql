namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Per-call state bag passed across the scaffolding loader hierarchy. Holds the live
/// connection, the in-flight DatabaseModel, the table-filter, the engine capabilities,
/// the MariaDB JSON_VALID column set (empty on MySQL), and the lookup dictionaries the
/// later loaders populate (tables, columns). Database-qualified lookups are shared by
/// every selected database so cross-database foreign keys can be assembled after all
/// table and column metadata has been loaded. The shared history-table set prevents a
/// recognized temporal implementation detail from resurfacing when multiple databases
/// are scaffolded in a different order. Created once per selected database in a
/// <see cref="MySqlDatabaseModelFactory.Create(DbConnection, DatabaseModelFactoryOptions)"/>
/// call and discarded when the call returns; do not cache references to it.
/// </summary>
internal sealed class ScaffoldingPipelineContext
{
    public ScaffoldingPipelineContext(
        DbConnection connection,
        DatabaseModel databaseModel,
        TableFilter tableFilter,
        ProviderProfile profile,
        HashSet<(string TableName, string ColumnName)> mariaDbJsonColumns,
        string databaseName,
        bool qualifyNamesWithSchema,
        Dictionary<(string DatabaseName, string TableName), DatabaseTable> databaseTables,
        Dictionary<(string DatabaseName, string TableName, string ColumnName), DatabaseColumn> databaseColumns,
        HashSet<(string DatabaseName, string TableName)> temporalHistoryTables
    )
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        DatabaseModel = databaseModel ?? throw new ArgumentNullException(nameof(databaseModel));
        TableFilter = tableFilter;
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        MariaDbJsonColumns = mariaDbJsonColumns ?? throw new ArgumentNullException(nameof(mariaDbJsonColumns));
        DatabaseName = string.IsNullOrWhiteSpace(databaseName)
            ? throw new ArgumentException("A scaffolding database name is required.", nameof(databaseName))
            : databaseName;

        QualifyNamesWithSchema = qualifyNamesWithSchema;
        DatabaseTables = databaseTables ?? throw new ArgumentNullException(nameof(databaseTables));
        DatabaseColumns = databaseColumns ?? throw new ArgumentNullException(nameof(databaseColumns));
        TemporalHistoryTables = temporalHistoryTables
            ?? throw new ArgumentNullException(nameof(temporalHistoryTables));
    }

    public DbConnection Connection { get; }

    public DatabaseModel DatabaseModel { get; }

    public TableFilter TableFilter { get; }

    public ProviderProfile Profile { get; }

    public HashSet<(string TableName, string ColumnName)> MariaDbJsonColumns { get; }

    public string DatabaseName { get; }

    public bool QualifyNamesWithSchema { get; }

    public Dictionary<string, DatabaseTable> TableLookup { get; } = new(StringComparer.Ordinal);

    public Dictionary<(string TableName, string ColumnName), DatabaseColumn> Columns { get; } = [];

    public Dictionary<(string DatabaseName, string TableName), DatabaseTable> DatabaseTables { get; }

    public Dictionary<(string DatabaseName, string TableName, string ColumnName), DatabaseColumn> DatabaseColumns
    {
        get;
    }

    public HashSet<(string DatabaseName, string TableName)> TemporalHistoryTables { get; }
}
