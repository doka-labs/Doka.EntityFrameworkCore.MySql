namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds provider-specific index configuration extensions for MySQL-family metadata.
/// </summary>
public static class MySqlIndexBuilderExtensions
{
    /// <summary>
    /// Extends a unique index with the entity's application-time period by using
    /// MariaDB's <c>WITHOUT OVERLAPS</c> constraint.
    /// </summary>
    /// <param name="indexBuilder">The index builder.</param>
    /// <returns>The same <see cref="IndexBuilder"/> instance.</returns>
    public static IndexBuilder UseWithoutOverlaps(
        this IndexBuilder indexBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(indexBuilder);

        indexBuilder.Metadata.SetMySqlApplicationTimeWithoutOverlaps(true);

        return indexBuilder;
    }

    /// <summary>
    /// Extends a unique index with the entity's application-time period by using
    /// MariaDB's <c>WITHOUT OVERLAPS</c> constraint.
    /// </summary>
    /// <typeparam name="TEntity">The entity CLR type.</typeparam>
    /// <param name="indexBuilder">The index builder.</param>
    /// <returns>The same <see cref="IndexBuilder{TEntity}"/> instance.</returns>
    public static IndexBuilder<TEntity> UseWithoutOverlaps<TEntity>(
        this IndexBuilder<TEntity> indexBuilder
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(indexBuilder);

        ((IndexBuilder)indexBuilder).UseWithoutOverlaps();

        return indexBuilder;
    }

    /// <summary>
    /// Configures the index as a full-text index.
    /// </summary>
    /// <param name="indexBuilder">The index builder.</param>
    /// <returns>The same <see cref="IndexBuilder"/> instance.</returns>
    public static IndexBuilder IsFullText(
        this IndexBuilder indexBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(indexBuilder);

        indexBuilder.Metadata.SetMySqlFullTextIndex(true);

        return indexBuilder;
    }

    /// <summary>
    /// Configures the index as a full-text index.
    /// </summary>
    /// <typeparam name="TEntity">The entity CLR type.</typeparam>
    /// <param name="indexBuilder">The index builder.</param>
    /// <returns>The same <see cref="IndexBuilder{TEntity}"/> instance.</returns>
    public static IndexBuilder<TEntity> IsFullText<TEntity>(
        this IndexBuilder<TEntity> indexBuilder
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(indexBuilder);

        ((IndexBuilder)indexBuilder).IsFullText();

        return indexBuilder;
    }

    /// <summary>
    /// Configures the indexed prefix length for each index property. A value of
    /// zero selects the complete value for the corresponding property.
    /// </summary>
    /// <param name="indexBuilder">The index builder.</param>
    /// <param name="prefixLengths">One non-negative prefix length per index property.</param>
    /// <returns>The same <see cref="IndexBuilder"/> instance.</returns>
    public static IndexBuilder HasPrefixLength(
        this IndexBuilder indexBuilder,
        params int[] prefixLengths
    )
    {
        ArgumentNullException.ThrowIfNull(indexBuilder);
        ArgumentNullException.ThrowIfNull(prefixLengths);

        if (prefixLengths.Length != indexBuilder.Metadata.Properties.Count)
        {
            throw new ArgumentException(
                "Exactly one prefix length must be supplied for each index property.",
                nameof(prefixLengths));
        }

        if (prefixLengths.Any(prefixLength => prefixLength < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLengths), "Index prefix lengths cannot be negative.");
        }

        indexBuilder.Metadata.SetMySqlIndexPrefixLengths(prefixLengths);

        return indexBuilder;
    }

    /// <summary>
    /// Configures the indexed prefix length for each index property. A value of
    /// zero selects the complete value for the corresponding property.
    /// </summary>
    /// <typeparam name="TEntity">The entity CLR type.</typeparam>
    /// <param name="indexBuilder">The index builder.</param>
    /// <param name="prefixLengths">One non-negative prefix length per index property.</param>
    /// <returns>The same <see cref="IndexBuilder{TEntity}"/> instance.</returns>
    public static IndexBuilder<TEntity> HasPrefixLength<TEntity>(
        this IndexBuilder<TEntity> indexBuilder,
        params int[] prefixLengths
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(indexBuilder);

        ((IndexBuilder)indexBuilder).HasPrefixLength(prefixLengths);

        return indexBuilder;
    }
}
