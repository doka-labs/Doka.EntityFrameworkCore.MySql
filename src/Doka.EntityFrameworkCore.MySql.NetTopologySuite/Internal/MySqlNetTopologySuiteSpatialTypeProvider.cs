namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteSpatialTypeProvider : IMySqlNetTopologySuiteMarker, IMySqlSpatialTypeProvider
{
    private static readonly Dictionary<string, Type> s_storeTypeToClrType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["geometry"] = typeof(Geometry),
        ["point"] = typeof(Point),
        ["linestring"] = typeof(LineString),
        ["polygon"] = typeof(Polygon),
        ["geometrycollection"] = typeof(GeometryCollection),
        ["multipoint"] = typeof(MultiPoint),
        ["multilinestring"] = typeof(MultiLineString),
        ["multipolygon"] = typeof(MultiPolygon),
    };

    public Type GeometryType => typeof(Geometry);

    public bool TryResolveClrType(
        string? storeTypeName,
        out Type? clrType
    )
    {
        clrType = null;

        var normalizedStoreType = MySqlSpatialTypeSupport.NormalizeStoreTypeName(storeTypeName);

        return normalizedStoreType is not null && s_storeTypeToClrType.TryGetValue(normalizedStoreType, out clrType);
    }
}
