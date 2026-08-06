namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds provider-specific key configuration extensions for MariaDB application-time periods.
/// </summary>
public static class MySqlKeyBuilderExtensions
{
    /// <summary>
    /// Extends a primary or alternate key with the entity's application-time period by using
    /// MariaDB's <c>WITHOUT OVERLAPS</c> constraint.
    /// </summary>
    /// <param name="keyBuilder">The key builder.</param>
    /// <returns>The same <see cref="KeyBuilder"/> instance.</returns>
    public static KeyBuilder UseWithoutOverlaps(
        this KeyBuilder keyBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(keyBuilder);

        keyBuilder.Metadata.SetAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps, true);

        return keyBuilder;
    }

    /// <summary>
    /// Extends a primary or alternate key with the entity's application-time period by using
    /// MariaDB's <c>WITHOUT OVERLAPS</c> constraint.
    /// </summary>
    /// <typeparam name="TEntity">The entity CLR type.</typeparam>
    /// <param name="keyBuilder">The key builder.</param>
    /// <returns>The same <see cref="KeyBuilder{TEntity}"/> instance.</returns>
    public static KeyBuilder<TEntity> UseWithoutOverlaps<TEntity>(
        this KeyBuilder<TEntity> keyBuilder
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(keyBuilder);

        ((KeyBuilder)keyBuilder).UseWithoutOverlaps();

        return keyBuilder;
    }
}
