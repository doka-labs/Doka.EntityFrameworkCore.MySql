using System.Diagnostics.CodeAnalysis;
using Doka.EntityFrameworkCore.MySql.Internal.Metadata;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Configures a MariaDB application-time period.
/// </summary>
public class MySqlApplicationTimeTableBuilder
{
    private readonly EntityTypeBuilder _entityTypeBuilder;

    internal MySqlApplicationTimeTableBuilder(
        EntityTypeBuilder entityTypeBuilder
    )
    {
        _entityTypeBuilder = entityTypeBuilder ?? throw new ArgumentNullException(nameof(entityTypeBuilder));
    }

    /// <summary>
    /// Configures the SQL period identifier.
    /// </summary>
    /// <param name="periodName">The identifier emitted after <c>PERIOD FOR</c>.</param>
    /// <returns>The same builder so that additional period configuration can be chained.</returns>
    public virtual MySqlApplicationTimeTableBuilder HasPeriodName(
        string periodName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(periodName);
        _entityTypeBuilder.Metadata.SetMySqlApplicationTimePeriodName(periodName);

        return this;
    }

    /// <summary>
    /// Configures the non-nullable period-start property and its column.
    /// </summary>
    /// <param name="propertyName">The period-start property name.</param>
    /// <returns>A property builder for configuring the period-start column.</returns>
    public virtual PropertyBuilder<DateTime> HasPeriodStart(
        string propertyName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        MySqlApplicationTimeMetadata.ValidatePeriodPropertyType(_entityTypeBuilder.Metadata, propertyName);
        _entityTypeBuilder.Metadata.SetMySqlApplicationTimePeriodStartPropertyName(propertyName);

        return _entityTypeBuilder
            .Property<DateTime>(propertyName)
            .ValueGeneratedNever();
    }

    /// <summary>
    /// Configures the non-nullable period-end property and its column.
    /// </summary>
    /// <param name="propertyName">The period-end property name.</param>
    /// <returns>A property builder for configuring the period-end column.</returns>
    public virtual PropertyBuilder<DateTime> HasPeriodEnd(
        string propertyName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        MySqlApplicationTimeMetadata.ValidatePeriodPropertyType(_entityTypeBuilder.Metadata, propertyName);
        _entityTypeBuilder.Metadata.SetMySqlApplicationTimePeriodEndPropertyName(propertyName);

        return _entityTypeBuilder
            .Property<DateTime>(propertyName)
            .ValueGeneratedNever();
    }

    /// <summary>
    /// Extends the primary key with the period using MariaDB's
    /// <c>WITHOUT OVERLAPS</c> constraint.
    /// </summary>
    /// <param name="enabled">
    /// <see langword="true" /> to require non-overlapping application-time ranges;
    /// otherwise, <see langword="false" />.
    /// </param>
    /// <returns>The same builder so that additional period configuration can be chained.</returns>
    public virtual MySqlApplicationTimeTableBuilder UseWithoutOverlaps(
        bool enabled = true
    )
    {
        _entityTypeBuilder.Metadata.SetMySqlApplicationTimeWithoutOverlaps(enabled);

        return this;
    }
}

/// <summary>
/// Configures an application-time period for a typed entity.
/// </summary>
/// <typeparam name="TEntity">The entity type being configured.</typeparam>
public sealed class MySqlApplicationTimeTableBuilder<
    [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TEntity> : MySqlApplicationTimeTableBuilder
    where TEntity : class
{
    private readonly EntityTypeBuilder<TEntity> _entityTypeBuilder;

    internal MySqlApplicationTimeTableBuilder(
        EntityTypeBuilder<TEntity> entityTypeBuilder
    ) : base(entityTypeBuilder)
    {
        _entityTypeBuilder = entityTypeBuilder;
    }

    /// <summary>
    /// Configures a non-nullable CLR property as the period start.
    /// </summary>
    /// <param name="propertyExpression">An expression selecting the period-start property.</param>
    /// <returns>A property builder for configuring the period column.</returns>
    public PropertyBuilder<DateTime> HasPeriodStart(
        Expression<Func<TEntity, DateTime>> propertyExpression
    )
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var propertyBuilder = _entityTypeBuilder.Property(propertyExpression);
        _entityTypeBuilder.Metadata.SetMySqlApplicationTimePeriodStartPropertyName(propertyBuilder.Metadata.Name);

        return propertyBuilder.ValueGeneratedNever();
    }

    /// <summary>
    /// Configures a non-nullable CLR property as the period end.
    /// </summary>
    /// <param name="propertyExpression">An expression selecting the period-end property.</param>
    /// <returns>A property builder for configuring the period column.</returns>
    public PropertyBuilder<DateTime> HasPeriodEnd(
        Expression<Func<TEntity, DateTime>> propertyExpression
    )
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var propertyBuilder = _entityTypeBuilder.Property(propertyExpression);
        _entityTypeBuilder.Metadata.SetMySqlApplicationTimePeriodEndPropertyName(propertyBuilder.Metadata.Name);

        return propertyBuilder.ValueGeneratedNever();
    }
}
