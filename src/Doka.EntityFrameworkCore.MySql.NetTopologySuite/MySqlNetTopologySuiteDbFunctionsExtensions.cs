namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds the approved provider-specific spatial helper functions to <see cref="EF.Functions" />.
/// </summary>
public static class MySqlNetTopologySuiteDbFunctionsExtensions
{
    /// <summary>
    /// Translates to the server-side spherical distance helper for long/latitude point scenarios.
    /// </summary>
    public static double DistanceSphere(
        this DbFunctions _,
        Point left,
        Point right
    ) => throw CreateClientUsageException(nameof(DistanceSphere));

    /// <summary>
    /// Translates to the server-side minimum-bounding-rectangle contains predicate.
    /// </summary>
    public static bool MbrContains(
        this DbFunctions _,
        Geometry left,
        Geometry right
    ) => throw CreateClientUsageException(nameof(MbrContains));

    /// <summary>
    /// Translates to the server-side minimum-bounding-rectangle within predicate.
    /// </summary>
    public static bool MbrWithin(
        this DbFunctions _,
        Geometry left,
        Geometry right
    ) => throw CreateClientUsageException(nameof(MbrWithin));

    /// <summary>
    /// Translates to the server-side minimum-bounding-rectangle intersects predicate.
    /// </summary>
    public static bool MbrIntersects(
        this DbFunctions _,
        Geometry left,
        Geometry right
    ) => throw CreateClientUsageException(nameof(MbrIntersects));

    /// <summary>
    /// Translates to the server-side minimum-bounding-rectangle overlaps predicate.
    /// </summary>
    public static bool MbrOverlaps(
        this DbFunctions _,
        Geometry left,
        Geometry right
    ) => throw CreateClientUsageException(nameof(MbrOverlaps));

    /// <summary>
    /// Translates to the server-side minimum-bounding-rectangle disjoint predicate.
    /// </summary>
    public static bool MbrDisjoint(
        this DbFunctions _,
        Geometry left,
        Geometry right
    ) => throw CreateClientUsageException(nameof(MbrDisjoint));

    private static InvalidOperationException CreateClientUsageException(
        string methodName
    )
    {
        return new InvalidOperationException(
            $"The spatial helper '{methodName}' is only supported inside LINQ queries translated by Entity Framework Core.");
    }
}
