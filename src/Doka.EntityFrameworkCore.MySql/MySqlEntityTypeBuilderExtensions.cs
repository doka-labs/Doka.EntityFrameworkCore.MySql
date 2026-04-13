namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds provider-specific entity-level configuration extensions for MySQL-family metadata.
/// </summary>
public static class MySqlEntityTypeBuilderExtensions
{
    /// <summary>
    /// Configures the table character set for an entity type.
    /// </summary>
    /// <param name="entityTypeBuilder">The entity-type builder.</param>
    /// <param name="charSet">The table character set.</param>
    /// <returns>The same <see cref="EntityTypeBuilder"/> instance.</returns>
    public static EntityTypeBuilder HasCharSet(
        this EntityTypeBuilder entityTypeBuilder,
        string charSet
    )
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(charSet);

        entityTypeBuilder.Metadata.SetMySqlCharSet(charSet);

        return entityTypeBuilder;
    }

    /// <summary>
    /// Configures the table character set for a typed entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity CLR type.</typeparam>
    /// <param name="entityTypeBuilder">The entity-type builder.</param>
    /// <param name="charSet">The table character set.</param>
    /// <returns>The same <see cref="EntityTypeBuilder{TEntity}"/> instance.</returns>
    public static EntityTypeBuilder<TEntity> HasCharSet<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string charSet
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);

        ((EntityTypeBuilder)entityTypeBuilder).HasCharSet(charSet);

        return entityTypeBuilder;
    }

    /// <summary>
    /// Configures the storage engine for an entity type.
    /// </summary>
    /// <param name="entityTypeBuilder">The entity-type builder.</param>
    /// <param name="engine">The storage engine.</param>
    /// <returns>The same <see cref="EntityTypeBuilder"/> instance.</returns>
    public static EntityTypeBuilder UseStorageEngine(
        this EntityTypeBuilder entityTypeBuilder,
        string engine
    )
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(engine);

        entityTypeBuilder.Metadata.SetMySqlStorageEngine(engine);

        return entityTypeBuilder;
    }

    /// <summary>
    /// Configures the storage engine for a typed entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity CLR type.</typeparam>
    /// <param name="entityTypeBuilder">The entity-type builder.</param>
    /// <param name="engine">The storage engine.</param>
    /// <returns>The same <see cref="EntityTypeBuilder{TEntity}"/> instance.</returns>
    public static EntityTypeBuilder<TEntity> UseStorageEngine<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string engine
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);

        ((EntityTypeBuilder)entityTypeBuilder).UseStorageEngine(engine);

        return entityTypeBuilder;
    }
}
