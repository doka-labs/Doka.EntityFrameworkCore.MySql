using System.Diagnostics.CodeAnalysis;
using Doka.EntityFrameworkCore.MySql.Internal.Metadata;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Configures a temporal table mapping.
/// </summary>
public class MySqlTemporalTableBuilder
{
    private readonly EntityTypeBuilder _entityTypeBuilder;

    internal MySqlTemporalTableBuilder(
        EntityTypeBuilder entityTypeBuilder
    )
    {
        _entityTypeBuilder = entityTypeBuilder ?? throw new ArgumentNullException(nameof(entityTypeBuilder));
    }

    /// <summary>
    /// Configures the external history table used by MySQL temporal emulation.
    /// MariaDB retains native system-versioned history internally, so it does not
    /// use the configured physical history-table name.
    /// </summary>
    /// <param name="name">The history-table name.</param>
    /// <returns>The same builder so that additional configuration can be chained.</returns>
    public virtual MySqlTemporalTableBuilder UseHistoryTable(
        string name
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _entityTypeBuilder.Metadata.SetMySqlTemporalHistoryTableName(name);

        return this;
    }

    /// <summary>
    /// Configures the external history table and database/schema used by MySQL
    /// temporal emulation. MariaDB retains native system-versioned history
    /// internally, so it does not use this physical placement.
    /// </summary>
    /// <param name="name">The history-table name.</param>
    /// <param name="schema">The history-table database/schema.</param>
    /// <returns>The same builder so that additional configuration can be chained.</returns>
    public virtual MySqlTemporalTableBuilder UseHistoryTable(
        string name,
        string schema
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        _entityTypeBuilder.Metadata.SetMySqlTemporalHistoryTableName(name);
        _entityTypeBuilder.Metadata.SetMySqlTemporalHistoryTableSchema(schema);

        return this;
    }

    /// <summary>
    /// Configures the shadow or CLR property used for the temporal period start.
    /// </summary>
    /// <param name="propertyName">The period-start property name.</param>
    /// <returns>A property builder for configuring the period column.</returns>
    public virtual PropertyBuilder<DateTime> HasPeriodStart(
        string propertyName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        MySqlTemporalMetadata.ValidatePeriodPropertyType(_entityTypeBuilder.Metadata, propertyName);

        _entityTypeBuilder.Metadata.SetMySqlTemporalPeriodStartPropertyName(propertyName);

        return _entityTypeBuilder
            .Property<DateTime>(propertyName)
            .HasColumnName(propertyName)
            .ValueGeneratedOnAddOrUpdate();
    }

    /// <summary>
    /// Configures the shadow or CLR property used for the temporal period end.
    /// </summary>
    /// <param name="propertyName">The period-end property name.</param>
    /// <returns>A property builder for configuring the period column.</returns>
    public virtual PropertyBuilder<DateTime> HasPeriodEnd(
        string propertyName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        MySqlTemporalMetadata.ValidatePeriodPropertyType(_entityTypeBuilder.Metadata, propertyName);

        _entityTypeBuilder.Metadata.SetMySqlTemporalPeriodEndPropertyName(propertyName);

        return _entityTypeBuilder
            .Property<DateTime>(propertyName)
            .HasColumnName(propertyName)
            .ValueGeneratedOnAddOrUpdate();
    }
}

/// <summary>
/// Configures a temporal table mapping for a typed entity.
/// </summary>
/// <typeparam name="TEntity">The entity CLR type.</typeparam>
public sealed class
    MySqlTemporalTableBuilder<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TEntity> : MySqlTemporalTableBuilder
    where TEntity : class
{
    internal MySqlTemporalTableBuilder(
        EntityTypeBuilder<TEntity> entityTypeBuilder
    ) : base(entityTypeBuilder) { }
}

/// <summary>
/// Configures a temporal table mapping for an owned entity type.
/// </summary>
public class MySqlOwnedNavigationTemporalTableBuilder
{
    private readonly OwnedNavigationBuilder _ownedNavigationBuilder;

    internal MySqlOwnedNavigationTemporalTableBuilder(
        OwnedNavigationBuilder ownedNavigationBuilder
    )
    {
        _ownedNavigationBuilder = ownedNavigationBuilder
            ?? throw new ArgumentNullException(nameof(ownedNavigationBuilder));
    }

    /// <summary>
    /// Configures the external history table used by MySQL temporal emulation.
    /// MariaDB retains native system-versioned history internally, so it does not
    /// use the configured physical history-table name.
    /// </summary>
    /// <param name="name">The history-table name.</param>
    /// <returns>The same builder so that additional configuration can be chained.</returns>
    public virtual MySqlOwnedNavigationTemporalTableBuilder UseHistoryTable(
        string name
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _ownedNavigationBuilder.OwnedEntityType.SetMySqlTemporalHistoryTableName(name);

        return this;
    }

    /// <summary>
    /// Configures the external history table and database/schema used by MySQL
    /// temporal emulation. MariaDB retains native system-versioned history
    /// internally, so it does not use this physical placement.
    /// </summary>
    /// <param name="name">The history-table name.</param>
    /// <param name="schema">The history-table database/schema.</param>
    /// <returns>The same builder so that additional configuration can be chained.</returns>
    public virtual MySqlOwnedNavigationTemporalTableBuilder UseHistoryTable(
        string name,
        string schema
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        _ownedNavigationBuilder.OwnedEntityType.SetMySqlTemporalHistoryTableName(name);
        _ownedNavigationBuilder.OwnedEntityType.SetMySqlTemporalHistoryTableSchema(schema);

        return this;
    }

    /// <summary>
    /// Configures the shadow or CLR property used for the temporal period start.
    /// </summary>
    /// <param name="propertyName">The period-start property name.</param>
    /// <returns>A property builder for configuring the period column.</returns>
    public virtual PropertyBuilder<DateTime> HasPeriodStart(
        string propertyName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        MySqlTemporalMetadata.ValidatePeriodPropertyType(_ownedNavigationBuilder.OwnedEntityType, propertyName);

        _ownedNavigationBuilder.OwnedEntityType.SetMySqlTemporalPeriodStartPropertyName(propertyName);

        return _ownedNavigationBuilder
            .Property<DateTime>(propertyName)
            .HasColumnName(propertyName)
            .ValueGeneratedOnAddOrUpdate();
    }

    /// <summary>
    /// Configures the shadow or CLR property used for the temporal period end.
    /// </summary>
    /// <param name="propertyName">The period-end property name.</param>
    /// <returns>A property builder for configuring the period column.</returns>
    public virtual PropertyBuilder<DateTime> HasPeriodEnd(
        string propertyName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        MySqlTemporalMetadata.ValidatePeriodPropertyType(_ownedNavigationBuilder.OwnedEntityType, propertyName);

        _ownedNavigationBuilder.OwnedEntityType.SetMySqlTemporalPeriodEndPropertyName(propertyName);

        return _ownedNavigationBuilder
            .Property<DateTime>(propertyName)
            .HasColumnName(propertyName)
            .ValueGeneratedOnAddOrUpdate();
    }
}

/// <summary>
/// Configures a temporal table mapping for a typed owned entity type.
/// </summary>
/// <typeparam name="TOwnerEntity">The owner CLR type.</typeparam>
/// <typeparam name="TDependentEntity">The owned CLR type.</typeparam>
public sealed class MySqlOwnedNavigationTemporalTableBuilder<
    [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TOwnerEntity,
    [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TDependentEntity> :
    MySqlOwnedNavigationTemporalTableBuilder
    where TOwnerEntity : class
    where TDependentEntity : class
{
    internal MySqlOwnedNavigationTemporalTableBuilder(
        OwnedNavigationBuilder<TOwnerEntity, TDependentEntity> ownedNavigationBuilder
    ) : base(ownedNavigationBuilder) { }
}
