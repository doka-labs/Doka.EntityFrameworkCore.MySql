using Doka.EntityFrameworkCore.MySql.Internal.Metadata;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides temporal-table configuration for relational table builders.
/// </summary>
public static class MySqlTableBuilderExtensions
{
    /// <summary>
    /// Configures the table as temporal.
    /// </summary>
    /// <param name="tableBuilder">The table builder.</param>
    /// <returns>A builder for configuring the temporal table.</returns>
    public static MySqlTemporalTableBuilder IsTemporal(
        this TableBuilder tableBuilder
    ) => tableBuilder.IsTemporal(true);

    /// <summary>
    /// Configures whether the table is temporal.
    /// </summary>
    /// <param name="tableBuilder">The table builder.</param>
    /// <param name="temporal">Whether temporal mapping is enabled.</param>
    /// <returns>A builder for configuring the temporal table.</returns>
    public static MySqlTemporalTableBuilder IsTemporal(
        this TableBuilder tableBuilder,
        bool temporal
    )
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);

        tableBuilder.Metadata.SetMySqlTemporal(temporal);

        return new MySqlTemporalTableBuilder(tableBuilder.GetInfrastructure());
    }

    /// <summary>
    /// Configures the table as temporal and exposes temporal-specific options.
    /// </summary>
    /// <param name="tableBuilder">The table builder.</param>
    /// <param name="buildAction">The temporal configuration callback.</param>
    /// <returns>The same table builder.</returns>
    public static TableBuilder IsTemporal(
        this TableBuilder tableBuilder,
        Action<MySqlTemporalTableBuilder> buildAction
    )
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);
        ArgumentNullException.ThrowIfNull(buildAction);

        tableBuilder.Metadata.SetMySqlTemporal(true);
        buildAction(new MySqlTemporalTableBuilder(tableBuilder.GetInfrastructure()));

        return tableBuilder;
    }

    /// <summary>
    /// Configures a typed table as temporal.
    /// </summary>
    /// <typeparam name="TEntity">The entity CLR type.</typeparam>
    /// <param name="tableBuilder">The table builder.</param>
    /// <returns>A builder for configuring the temporal table.</returns>
    public static MySqlTemporalTableBuilder<TEntity> IsTemporal<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TEntity>(
        this TableBuilder<TEntity> tableBuilder
    )
        where TEntity : class => tableBuilder.IsTemporal(true);

    /// <summary>
    /// Configures whether a typed table is temporal.
    /// </summary>
    /// <typeparam name="TEntity">The entity CLR type.</typeparam>
    /// <param name="tableBuilder">The table builder.</param>
    /// <param name="temporal">Whether temporal mapping is enabled.</param>
    /// <returns>A builder for configuring the temporal table.</returns>
    public static MySqlTemporalTableBuilder<TEntity> IsTemporal<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TEntity>(
        this TableBuilder<TEntity> tableBuilder,
        bool temporal
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);

        tableBuilder.Metadata.SetMySqlTemporal(temporal);

        return new MySqlTemporalTableBuilder<TEntity>(tableBuilder.GetInfrastructure<EntityTypeBuilder<TEntity>>());
    }

    /// <summary>
    /// Configures a typed table as temporal and exposes temporal-specific options.
    /// </summary>
    /// <typeparam name="TEntity">The entity CLR type.</typeparam>
    /// <param name="tableBuilder">The table builder.</param>
    /// <param name="buildAction">The temporal configuration callback.</param>
    /// <returns>The same table builder.</returns>
    public static TableBuilder<TEntity> IsTemporal<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TEntity>(
        this TableBuilder<TEntity> tableBuilder,
        Action<MySqlTemporalTableBuilder<TEntity>> buildAction
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);
        ArgumentNullException.ThrowIfNull(buildAction);

        tableBuilder.Metadata.SetMySqlTemporal(true);
        buildAction(
            new MySqlTemporalTableBuilder<TEntity>(tableBuilder.GetInfrastructure<EntityTypeBuilder<TEntity>>()));

        return tableBuilder;
    }

    /// <summary>
    /// Configures an owned table as temporal.
    /// </summary>
    /// <param name="tableBuilder">The owned table builder.</param>
    /// <returns>A builder for configuring the temporal table.</returns>
    public static MySqlOwnedNavigationTemporalTableBuilder IsTemporal(
        this OwnedNavigationTableBuilder tableBuilder
    ) => tableBuilder.IsTemporal(true);

    /// <summary>
    /// Configures whether an owned table is temporal.
    /// </summary>
    /// <param name="tableBuilder">The owned table builder.</param>
    /// <param name="temporal">Whether temporal mapping is enabled.</param>
    /// <returns>A builder for configuring the temporal table.</returns>
    public static MySqlOwnedNavigationTemporalTableBuilder IsTemporal(
        this OwnedNavigationTableBuilder tableBuilder,
        bool temporal
    )
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);

        tableBuilder.Metadata.SetMySqlTemporal(temporal);

        return new MySqlOwnedNavigationTemporalTableBuilder(tableBuilder.GetInfrastructure());
    }

    /// <summary>
    /// Configures an owned table as temporal and exposes temporal-specific options.
    /// </summary>
    /// <param name="tableBuilder">The owned table builder.</param>
    /// <param name="buildAction">The temporal configuration callback.</param>
    /// <returns>The same table builder.</returns>
    public static OwnedNavigationTableBuilder IsTemporal(
        this OwnedNavigationTableBuilder tableBuilder,
        Action<MySqlOwnedNavigationTemporalTableBuilder> buildAction
    )
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);
        ArgumentNullException.ThrowIfNull(buildAction);

        tableBuilder.Metadata.SetMySqlTemporal(true);
        buildAction(new MySqlOwnedNavigationTemporalTableBuilder(tableBuilder.GetInfrastructure()));

        return tableBuilder;
    }

    /// <summary>
    /// Configures a typed owned table as temporal.
    /// </summary>
    /// <typeparam name="TOwnerEntity">The owner CLR type.</typeparam>
    /// <typeparam name="TDependentEntity">The owned CLR type.</typeparam>
    /// <param name="tableBuilder">The owned table builder.</param>
    /// <returns>A builder for configuring the temporal table.</returns>
    public static MySqlOwnedNavigationTemporalTableBuilder<TOwnerEntity, TDependentEntity> IsTemporal<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TOwnerEntity,
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TDependentEntity>(
        this OwnedNavigationTableBuilder<TOwnerEntity, TDependentEntity> tableBuilder
    )
        where TOwnerEntity : class
        where TDependentEntity : class => tableBuilder.IsTemporal(true);

    /// <summary>
    /// Configures whether a typed owned table is temporal.
    /// </summary>
    /// <typeparam name="TOwnerEntity">The owner CLR type.</typeparam>
    /// <typeparam name="TDependentEntity">The owned CLR type.</typeparam>
    /// <param name="tableBuilder">The owned table builder.</param>
    /// <param name="temporal">Whether temporal mapping is enabled.</param>
    /// <returns>A builder for configuring the temporal table.</returns>
    public static MySqlOwnedNavigationTemporalTableBuilder<TOwnerEntity, TDependentEntity> IsTemporal<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TOwnerEntity,
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TDependentEntity>(
        this OwnedNavigationTableBuilder<TOwnerEntity, TDependentEntity> tableBuilder,
        bool temporal
    )
        where TOwnerEntity : class
        where TDependentEntity : class
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);

        tableBuilder.Metadata.SetMySqlTemporal(temporal);

        return new MySqlOwnedNavigationTemporalTableBuilder<TOwnerEntity, TDependentEntity>(
            tableBuilder.GetInfrastructure<OwnedNavigationBuilder<TOwnerEntity, TDependentEntity>>());
    }

    /// <summary>
    /// Configures a typed owned table as temporal and exposes temporal-specific options.
    /// </summary>
    /// <typeparam name="TOwnerEntity">The owner CLR type.</typeparam>
    /// <typeparam name="TDependentEntity">The owned CLR type.</typeparam>
    /// <param name="tableBuilder">The owned table builder.</param>
    /// <param name="buildAction">The temporal configuration callback.</param>
    /// <returns>The same table builder.</returns>
    public static OwnedNavigationTableBuilder<TOwnerEntity, TDependentEntity> IsTemporal<
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TOwnerEntity,
        [DynamicallyAccessedMembers(MySqlTrimmingConstants.EntityType)] TDependentEntity>(
        this OwnedNavigationTableBuilder<TOwnerEntity, TDependentEntity> tableBuilder,
        Action<MySqlOwnedNavigationTemporalTableBuilder<TOwnerEntity, TDependentEntity>> buildAction
    )
        where TOwnerEntity : class
        where TDependentEntity : class
    {
        ArgumentNullException.ThrowIfNull(tableBuilder);
        ArgumentNullException.ThrowIfNull(buildAction);

        tableBuilder.Metadata.SetMySqlTemporal(true);
        buildAction(
            new MySqlOwnedNavigationTemporalTableBuilder<TOwnerEntity, TDependentEntity>(
                tableBuilder.GetInfrastructure<OwnedNavigationBuilder<TOwnerEntity, TDependentEntity>>()));

        return tableBuilder;
    }
}
