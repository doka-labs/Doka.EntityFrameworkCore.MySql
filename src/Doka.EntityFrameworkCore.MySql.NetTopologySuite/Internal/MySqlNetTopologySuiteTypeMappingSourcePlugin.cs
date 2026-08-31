namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    private static readonly ConcurrentDictionary<
        (Type ClrType, string StoreType, bool UseLongLatAxisOrder), RelationalTypeMapping> s_mappingCache = new();

    private readonly MySqlSingletonOptions _mySqlSingletonOptions;

    private static readonly Dictionary<Type, string> s_defaultStoreTypes = new()
    {
        [typeof(Geometry)] = "geometry",
        [typeof(Point)] = "point",
        [typeof(LineString)] = "linestring",
        [typeof(Polygon)] = "polygon",
        [typeof(GeometryCollection)] = "geometrycollection",
        [typeof(MultiPoint)] = "multipoint",
        [typeof(MultiLineString)] = "multilinestring",
        [typeof(MultiPolygon)] = "multipolygon",
    };

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

    public MySqlNetTopologySuiteTypeMappingSourcePlugin(
        IEnumerable<ISingletonOptions> singletonOptions
    )
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _mySqlSingletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    public RelationalTypeMapping? FindMapping(
        in RelationalTypeMappingInfo mappingInfo
    )
    {
        var clrType = mappingInfo.ClrType?.UnwrapNullableType();
        var normalizedStoreType = NormalizeStoreTypeName(mappingInfo.StoreTypeName);
        var spatialClrType = ResolveSpatialClrType(clrType, normalizedStoreType);

        if (spatialClrType is null)
        {
            return null;
        }

        var storeType = ResolveStoreType(spatialClrType, normalizedStoreType);
        var useLongLatAxisOrder =
            _mySqlSingletonOptions.Profile?.Engine.Has(EngineCapability.SpatialWktAxisOrderOptions) == true;

        return s_mappingCache.GetOrAdd(
            (spatialClrType, storeType, useLongLatAxisOrder),
            static key => CreateMapping(key.ClrType, key.StoreType, key.UseLongLatAxisOrder));
    }

    private static Type? ResolveSpatialClrType(
        Type? clrType,
        string? normalizedStoreType
    )
    {
        if (clrType is not null
            && typeof(Geometry).IsAssignableFrom(clrType))
        {
            return clrType;
        }

        if (normalizedStoreType is not null
            && s_storeTypeToClrType.TryGetValue(normalizedStoreType, out var mappedClrType))
        {
            return mappedClrType;
        }

        return null;
    }

    private static string ResolveStoreType(
        Type clrType,
        string? normalizedStoreType
    )
    {
        if (normalizedStoreType is not null
            && s_storeTypeToClrType.ContainsKey(normalizedStoreType))
        {
            return normalizedStoreType;
        }

        return s_defaultStoreTypes.GetValueOrDefault(clrType, "geometry");
    }

    private static RelationalTypeMapping CreateMapping(
        Type clrType,
        string storeType,
        bool useLongLatAxisOrder
    )
    {
        if (clrType == typeof(Geometry))
        {
            return CreateTypedMapping<Geometry>(storeType, useLongLatAxisOrder);
        }

        if (clrType == typeof(Point))
        {
            return CreateTypedMapping<Point>(storeType, useLongLatAxisOrder);
        }

        if (clrType == typeof(LineString))
        {
            return CreateTypedMapping<LineString>(storeType, useLongLatAxisOrder);
        }

        if (clrType == typeof(Polygon))
        {
            return CreateTypedMapping<Polygon>(storeType, useLongLatAxisOrder);
        }

        if (clrType == typeof(GeometryCollection))
        {
            return CreateTypedMapping<GeometryCollection>(storeType, useLongLatAxisOrder);
        }

        if (clrType == typeof(MultiPoint))
        {
            return CreateTypedMapping<MultiPoint>(storeType, useLongLatAxisOrder);
        }

        if (clrType == typeof(MultiLineString))
        {
            return CreateTypedMapping<MultiLineString>(storeType, useLongLatAxisOrder);
        }

        if (clrType == typeof(MultiPolygon))
        {
            return CreateTypedMapping<MultiPolygon>(storeType, useLongLatAxisOrder);
        }

        throw new InvalidOperationException(
            $"The CLR type '{clrType.Name}' is not part of the supported spatial contract.");
    }

    private static MySqlNetTopologySuiteGeometryTypeMapping<TGeometry> CreateTypedMapping<TGeometry>(
        string storeType,
        bool useLongLatAxisOrder
    )
        where TGeometry : Geometry
    {
        return new MySqlNetTopologySuiteGeometryTypeMapping<TGeometry>(
            new ValueConverter<TGeometry, MySqlGeometry>(
                geometry => MySqlGeometry.FromWkb(geometry.SRID, new WKBWriter().Write(geometry)),
                providerValue => ConvertFromProvider<TGeometry>(providerValue)),
            storeType,
            MySqlJsonGeometryWktReaderWriter.Instance,
            useLongLatAxisOrder);
    }

    private static TGeometry ConvertFromProvider<TGeometry>(
        MySqlGeometry providerValue
    )
        where TGeometry : Geometry
    {
        var geometry = MySqlSpatialValueReader.ReadWkb(providerValue.WKB.ToArray());
        geometry.SRID = providerValue.SRID;

        return geometry as TGeometry
            ?? throw new InvalidOperationException(
                $"The provider returned a geometry of type '{geometry.GetType().Name}' which cannot be materialized as '{typeof(TGeometry).Name}'.");
    }

    private static string? NormalizeStoreTypeName(
        string? storeTypeName
    )
    {
        if (string.IsNullOrWhiteSpace(storeTypeName))
        {
            return null;
        }

        var normalized = storeTypeName.Trim();
        var parenthesisIndex = normalized.IndexOf('(');

        if (parenthesisIndex >= 0)
        {
            normalized = normalized[..parenthesisIndex];
        }

        var whitespaceIndex = normalized.IndexOf(' ');

        if (whitespaceIndex >= 0)
        {
            normalized = normalized[..whitespaceIndex];
        }

        return normalized
            .Trim()
            .ToLowerInvariant();
    }
}
