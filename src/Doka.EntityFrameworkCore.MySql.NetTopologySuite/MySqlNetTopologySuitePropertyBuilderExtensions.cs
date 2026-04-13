namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds the approved spatial property metadata extensions for the optional NetTopologySuite package.
/// </summary>
public static class MySqlNetTopologySuitePropertyBuilderExtensions
{
    /// <summary>
    /// Configures the fixed SRID contract for a spatial property.
    /// </summary>
    public static PropertyBuilder HasSrid(
        this PropertyBuilder propertyBuilder,
        int srid
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        ArgumentOutOfRangeException.ThrowIfNegative(srid);

        if (!typeof(Geometry).IsAssignableFrom(propertyBuilder.Metadata.ClrType))
        {
            throw new InvalidOperationException(
                $"The property '{propertyBuilder.Metadata.DeclaringType.DisplayName()}.{propertyBuilder.Metadata.Name}' is not a NetTopologySuite geometry property.");
        }

        propertyBuilder.Metadata.SetMySqlSpatialReferenceSystemId(srid);

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the fixed SRID contract for a typed spatial property.
    /// </summary>
    public static PropertyBuilder<TGeometry> HasSrid<TGeometry>(
        this PropertyBuilder<TGeometry> propertyBuilder,
        int srid
    )
        where TGeometry : Geometry
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        HasSrid((PropertyBuilder)propertyBuilder, srid);

        return propertyBuilder;
    }
}
