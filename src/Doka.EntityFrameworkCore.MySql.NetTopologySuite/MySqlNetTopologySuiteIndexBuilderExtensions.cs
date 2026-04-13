namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds the approved spatial index configuration surface for the optional NetTopologySuite package.
/// </summary>
public static class MySqlNetTopologySuiteIndexBuilderExtensions
{
    /// <summary>
    /// Configures the index as an explicit single-column spatial index.
    /// </summary>
    public static IndexBuilder IsSpatial(
        this IndexBuilder indexBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(indexBuilder);

        indexBuilder.Metadata.SetMySqlSpatialIndex(true);

        return indexBuilder;
    }

    /// <summary>
    /// Configures the index as an explicit single-column spatial index.
    /// </summary>
    public static IndexBuilder<TEntity> IsSpatial<TEntity>(
        this IndexBuilder<TEntity> indexBuilder
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(indexBuilder);

        ((IndexBuilder)indexBuilder).IsSpatial();

        return indexBuilder;
    }
}
