namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlRecursiveDataReader
{
    private ReadOnlyCollection<DbColumn>? _columnSchema;
    private DataTable? _schemaTable;

    /// <summary>Gets the native column metadata restricted to visible payload ordinals.</summary>
    public ReadOnlyCollection<DbColumn> GetColumnSchema()
        => _columnSchema ??= VisibleColumns(_reader.GetColumnSchema());

    /// <inheritdoc />
    public override DataTable? GetSchemaTable() => _schemaTable ??= VisibleSchema(_reader.GetSchemaTable());

    /// <inheritdoc />
    public override async Task<DataTable?> GetSchemaTableAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _schemaTable ??= VisibleSchema(await _reader
            .GetSchemaTableAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    /// <inheritdoc />
    public override async Task<ReadOnlyCollection<DbColumn>> GetColumnSchemaAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _columnSchema ??= VisibleColumns(await _reader
            .GetColumnSchemaAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    private DataTable? VisibleSchema(
        DataTable? schema
    )
    {
        if (schema is null)
        {
            return null;
        }

        var visible = schema.Clone();

        foreach (DataRow row in schema.Rows)
        {
            if (row[SchemaTableColumn.ColumnOrdinal] is int ordinal
                && ordinal < _payloadCount)
            {
                visible.ImportRow(row);
            }
        }

        return visible;
    }

    private ReadOnlyCollection<DbColumn> VisibleColumns(
        ReadOnlyCollection<DbColumn> columns
    )
    {
        var visible = new DbColumn[_payloadCount];

        for (var ordinal = 0; ordinal < visible.Length; ordinal++)
        {
            visible[ordinal] = columns[ordinal];
        }

        return Array.AsReadOnly(visible);
    }
}
