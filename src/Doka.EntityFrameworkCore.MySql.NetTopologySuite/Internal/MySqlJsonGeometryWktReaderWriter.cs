namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Reads and writes NetTopologySuite values in JSON using well-known text.
/// </summary>
internal sealed class MySqlJsonGeometryWktReaderWriter : JsonValueReaderWriter<Geometry>
{
    private static readonly PropertyInfo s_instanceProperty =
        typeof(MySqlJsonGeometryWktReaderWriter).GetProperty(nameof(Instance))!;

    private static readonly WKTReader s_wktReader = new();

    public static MySqlJsonGeometryWktReaderWriter Instance { get; } = new();

    private MySqlJsonGeometryWktReaderWriter() { }

    public override Geometry FromJsonTyped(
        ref Utf8JsonReaderManager manager,
        object? existingObject = null
    ) => s_wktReader.Read(manager.CurrentReader.GetString());

    public override void ToJsonTyped(
        Utf8JsonWriter writer,
        Geometry value
    ) => writer.WriteStringValue(value.ToText());

    public override Expression ConstructorExpression => Expression.Property(null, s_instanceProperty);
}
