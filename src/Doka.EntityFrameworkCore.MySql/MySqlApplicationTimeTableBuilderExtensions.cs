using Doka.EntityFrameworkCore.MySql.Internal.Metadata;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides application-time and bitemporal table configuration.
/// </summary>
public static class MySqlApplicationTimeTableBuilderExtensions
{
    /// <summary>
    /// Configures an application-time period using conventional names.
    /// </summary>
    /// <param name="tableBuilder">The table builder being configured.</param>
    /// <returns>A builder for configuring the application-time period.</returns>
    public static MySqlApplicationTimeTableBuilder HasApplicationTimePeriod(
        this TableBuilder tableBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);

        var builder = Enable(tableBuilder.Metadata, tableBuilder.GetInfrastructure());
        ConfigureDefaults(builder);

        return builder;
    }

    /// <summary>
    /// Configures an application-time period.
    /// </summary>
    /// <param name="tableBuilder">The table builder being configured.</param>
    /// <param name="buildAction">An action that configures the application-time period.</param>
    /// <returns>The same table builder so that additional configuration can be chained.</returns>
    public static TableBuilder HasApplicationTimePeriod(
        this TableBuilder tableBuilder,
        Action<MySqlApplicationTimeTableBuilder> buildAction
    )
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);
        ArgumentNullException.ThrowIfNull(buildAction);

        var builder = Enable(tableBuilder.Metadata, tableBuilder.GetInfrastructure());
        ConfigureDefaults(builder);
        buildAction(builder);

        return tableBuilder;
    }

    /// <summary>
    /// Configures a bitemporal table by combining system time and application time.
    /// </summary>
    /// <param name="tableBuilder">The table builder being configured.</param>
    /// <returns>The same table builder so that additional configuration can be chained.</returns>
    public static TableBuilder IsBitemporal(
        this TableBuilder tableBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);

        _ = tableBuilder.IsTemporal();
        _ = tableBuilder.HasApplicationTimePeriod();

        return tableBuilder;
    }

    /// <summary>
    /// Configures both dimensions of a bitemporal table.
    /// </summary>
    /// <param name="tableBuilder">The table builder being configured.</param>
    /// <param name="temporalAction">An action that configures system-time versioning.</param>
    /// <param name="applicationTimeAction">An action that configures the application-time period.</param>
    /// <returns>The same table builder so that additional configuration can be chained.</returns>
    public static TableBuilder IsBitemporal(
        this TableBuilder tableBuilder,
        Action<MySqlTemporalTableBuilder> temporalAction,
        Action<MySqlApplicationTimeTableBuilder> applicationTimeAction
    )
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);
        ArgumentNullException.ThrowIfNull(temporalAction);
        ArgumentNullException.ThrowIfNull(applicationTimeAction);

        tableBuilder.IsTemporal(temporalAction);
        tableBuilder.HasApplicationTimePeriod(applicationTimeAction);

        return tableBuilder;
    }

    /// <summary>
    /// Configures a typed application-time period using conventional names.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="tableBuilder">The table builder being configured.</param>
    /// <returns>A typed builder for configuring the application-time period.</returns>
    public static MySqlApplicationTimeTableBuilder<TEntity> HasApplicationTimePeriod<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TEntity>(
        this TableBuilder<TEntity> tableBuilder
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);

        var builder = new MySqlApplicationTimeTableBuilder<TEntity>(
            tableBuilder.GetInfrastructure<EntityTypeBuilder<TEntity>>());
        tableBuilder.Metadata.SetMySqlApplicationTime(true);
        ConfigureDefaults(builder);

        return builder;
    }

    /// <summary>
    /// Configures a typed application-time period.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="tableBuilder">The table builder being configured.</param>
    /// <param name="buildAction">An action that configures the application-time period.</param>
    /// <returns>The same table builder so that additional configuration can be chained.</returns>
    public static TableBuilder<TEntity> HasApplicationTimePeriod<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TEntity>(
        this TableBuilder<TEntity> tableBuilder,
        Action<MySqlApplicationTimeTableBuilder<TEntity>> buildAction
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);
        ArgumentNullException.ThrowIfNull(buildAction);

        var builder = tableBuilder.HasApplicationTimePeriod();
        buildAction(builder);

        return tableBuilder;
    }

    /// <summary>
    /// Configures a typed bitemporal table.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="tableBuilder">The table builder being configured.</param>
    /// <returns>The same table builder so that additional configuration can be chained.</returns>
    public static TableBuilder<TEntity> IsBitemporal<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TEntity>(
        this TableBuilder<TEntity> tableBuilder
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);

        _ = tableBuilder.IsTemporal();
        _ = tableBuilder.HasApplicationTimePeriod();

        return tableBuilder;
    }

    /// <summary>
    /// Configures both dimensions of a typed bitemporal table.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="tableBuilder">The table builder being configured.</param>
    /// <param name="temporalAction">An action that configures system-time versioning.</param>
    /// <param name="applicationTimeAction">An action that configures the application-time period.</param>
    /// <returns>The same table builder so that additional configuration can be chained.</returns>
    public static TableBuilder<TEntity> IsBitemporal<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TEntity>(
        this TableBuilder<TEntity> tableBuilder,
        Action<MySqlTemporalTableBuilder<TEntity>> temporalAction,
        Action<MySqlApplicationTimeTableBuilder<TEntity>> applicationTimeAction
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);
        ArgumentNullException.ThrowIfNull(temporalAction);
        ArgumentNullException.ThrowIfNull(applicationTimeAction);

        tableBuilder.IsTemporal(temporalAction);
        tableBuilder.HasApplicationTimePeriod(applicationTimeAction);

        return tableBuilder;
    }

    private static MySqlApplicationTimeTableBuilder Enable(
        IMutableEntityType entityType,
        EntityTypeBuilder entityTypeBuilder
    )
    {
        entityType.SetMySqlApplicationTime(true);

        return new MySqlApplicationTimeTableBuilder(entityTypeBuilder);
    }

    private static void ConfigureDefaults(
        MySqlApplicationTimeTableBuilder builder
    )
    {
        builder.HasPeriodName(MySqlApplicationTimeMetadata.DefaultPeriodName);
        _ = builder.HasPeriodStart(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName);
        _ = builder.HasPeriodEnd(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName);
    }
}
