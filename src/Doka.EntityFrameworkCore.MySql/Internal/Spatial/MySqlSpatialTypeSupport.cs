namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlSpatialTypeSupport
{
    private static readonly HashSet<string> s_spatialStoreTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "geometry",
        "point",
        "linestring",
        "polygon",
        "geometrycollection",
        "multipoint",
        "multilinestring",
        "multipolygon",
    };

    public static bool IsSpatialClrType(
        Type type
    )
    {
        ArgumentNullException.ThrowIfNull(type);

        for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
        {
            if (string.Equals(candidate.FullName, "NetTopologySuite.Geometries.Geometry", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsSpatialStoreType(
        string? storeTypeName
    )
    {
        var normalizedStoreType = NormalizeStoreTypeName(storeTypeName);

        return normalizedStoreType is not null && s_spatialStoreTypes.Contains(normalizedStoreType);
    }

    public static string? NormalizeStoreTypeName(
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
