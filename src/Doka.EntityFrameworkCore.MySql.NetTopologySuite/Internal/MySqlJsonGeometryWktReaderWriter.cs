namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Reads and writes NetTopologySuite values in JSON using well-known text.
/// </summary>
internal sealed class MySqlJsonGeometryWktReaderWriter : JsonValueReaderWriter<Geometry>
{
    private static readonly PropertyInfo s_instanceProperty =
        typeof(MySqlJsonGeometryWktReaderWriter).GetProperty(nameof(Instance))!;

    public static MySqlJsonGeometryWktReaderWriter Instance { get; } = new();

    private MySqlJsonGeometryWktReaderWriter() { }

    public override Geometry FromJsonTyped(
        ref Utf8JsonReaderManager manager,
        object? existingObject = null
    )
    {
        var wkt = manager.CurrentReader.GetString();

        if (wkt is null)
        {
            throw new InvalidOperationException("A JSON spatial value must contain well-known text.");
        }

        return MySqlSpatialValueReader.ReadWkt(wkt);
    }

    public override void ToJsonTyped(
        Utf8JsonWriter writer,
        Geometry value
    ) => writer.WriteStringValue(value.ToText());

    public override Expression ConstructorExpression => Expression.Property(null, s_instanceProperty);
}
