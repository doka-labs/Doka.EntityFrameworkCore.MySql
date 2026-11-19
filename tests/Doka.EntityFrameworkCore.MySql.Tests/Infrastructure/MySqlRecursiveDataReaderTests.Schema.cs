namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRecursiveDataReaderTests
{
    /// <summary>Both schema APIs expose only payload metadata without reading rows.</summary>
    /// <param name="asynchronous">Whether to request asynchronous metadata.</param>
    /// <param name="columnSchema">Whether to request DbColumn metadata instead of a schema table.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Schema_metadata_hides_control_columns(
        bool asynchronous,
        bool columnSchema
    )
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var names = columnSchema
            ? (asynchronous
                ? await reader.GetColumnSchemaAsync(cancellationToken)
                : reader.GetColumnSchema())
            .Select(column => column.ColumnName)
            .ToArray()
            : SchemaNames(asynchronous
                ? await reader.GetSchemaTableAsync(cancellationToken)
                : reader.GetSchemaTable());

        // Assert
        Assert.Equal(["payload"], names);
        Assert.Equal(1, reader.FieldCount);
        Assert.Equal(1, reader.VisibleFieldCount);
        Assert.Equal(0, raw.ReadCalls);
    }

    /// <summary>Missing guard rows cannot silently become successful empty recursion.</summary>
    [Fact]
    public void Missing_guard_row_is_rejected()
    {
        // Arrange
        using var raw = Rows(typeof(int));
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));

        // Act
        var exception = Record.Exception(reader.Prime);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, raw.PayloadReads);
    }

    /// <summary>A replacing interceptor must retain the exact native control aliases and arity.</summary>
    /// <param name="wrongCount">Whether the shape has an extra payload column instead of a wrong alias.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replaced_reader_shape_fails_closed(
        bool wrongCount
    )
    {
        // Arrange
        var table = GuardTable(typeof(int));
        if (wrongCount)
        {
            table.Columns.Add("extra", typeof(int));
        }
        else
        {
            var presence = table.Columns[Presence];
            Assert.NotNull(presence);
            presence.ColumnName = "unexpected";
        }

        using var raw = new ReaderProbe(table);

        // Act
        var exception = Record.Exception(() => new MySqlRecursiveDataReader(raw, Policy([])));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, raw.ReadCalls);
    }

    /// <summary>Closed readers cannot replay a cached first row.</summary>
    /// <param name="asynchronous">Whether to request an asynchronous read.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closed_reader_does_not_replay_a_prefetched_row(
        bool asynchronous
    )
    {
        // Arrange
        using var raw = Rows(typeof(int), 7);
        using var reader = new MySqlRecursiveDataReader(raw, Policy([]));
        reader.Prime();
        reader.Close();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => reader.ReadAsync(cancellationToken))
            : Record.Exception(() => reader.Read());

        // Assert
        Assert.IsType<ObjectDisposedException>(exception);
    }

    private static string[] SchemaNames(
        DataTable? schema
    )
    {
        Assert.NotNull(schema);

        return schema
            .Rows
            .Cast<DataRow>()
            .Select(row => (string)row[SchemaTableColumn.ColumnName])
            .ToArray();
    }
}
