namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Per-call state bag passed across the scaffolding loader hierarchy. Holds the live
/// connection, the in-flight DatabaseModel, the table-filter, the engine capabilities,
/// the MariaDB JSON_VALID column set (empty on MySQL), and the lookup dictionaries the
/// later loaders populate (tables, columns). Created once per
/// <see cref="MySqlDatabaseModelFactory.Create(DbConnection, DatabaseModelFactoryOptions)"/>
/// call and discarded when the call returns; do not cache references to it.
/// </summary>
internal sealed class ScaffoldingPipelineContext
{
    public ScaffoldingPipelineContext(
        DbConnection connection,
        DatabaseModel databaseModel,
        TableFilter tableFilter,
        ServerCapabilities capabilities,
        HashSet<(string TableName, string ColumnName)> mariaDbJsonColumns
    )
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        DatabaseModel = databaseModel ?? throw new ArgumentNullException(nameof(databaseModel));
        TableFilter = tableFilter;
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        MariaDbJsonColumns = mariaDbJsonColumns ?? throw new ArgumentNullException(nameof(mariaDbJsonColumns));
    }

    public DbConnection Connection { get; }

    public DatabaseModel DatabaseModel { get; }

    public TableFilter TableFilter { get; }

    public ServerCapabilities Capabilities { get; }

    public HashSet<(string TableName, string ColumnName)> MariaDbJsonColumns { get; }

    public Dictionary<string, DatabaseTable> TableLookup { get; } = new(StringComparer.Ordinal);

    public Dictionary<(string TableName, string ColumnName), DatabaseColumn> Columns { get; } = [];
}
