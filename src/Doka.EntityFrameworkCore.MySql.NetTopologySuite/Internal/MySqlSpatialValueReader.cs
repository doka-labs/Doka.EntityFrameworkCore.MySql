namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Centralizes spatial input validation and NetTopologySuite parsing so every
/// provider materialization path crosses the same resource-safety boundary.
/// </summary>
internal static class MySqlSpatialValueReader
{
    private static readonly WKTReader s_wktReader = new();

    /// <summary>
    /// Validates and parses well-known text without exposing recursive parsing to
    /// structurally unbounded input.
    /// </summary>
    public static Geometry ReadWkt(
        string wkt
    )
    {
        MySqlSpatialInputGuard.ValidateWkt(wkt);

        return s_wktReader.Read(wkt);
    }

    /// <summary>
    /// Validates and parses an owned WKB buffer without an additional provider
    /// copy.
    /// </summary>
    public static Geometry ReadWkb(
        byte[] wkb
    )
    {
        ArgumentNullException.ThrowIfNull(wkb);
        MySqlSpatialInputGuard.ValidateWkb(wkb);

        return new WKBReader().Read(wkb);
    }

    /// <summary>
    /// Validates and parses a WKB slice, copying only after the resource-safety
    /// boundary has accepted the structure.
    /// </summary>
    public static Geometry ReadWkb(
        ReadOnlySpan<byte> wkb
    )
    {
        MySqlSpatialInputGuard.ValidateWkb(wkb);

        return new WKBReader().Read(wkb.ToArray());
    }
}
