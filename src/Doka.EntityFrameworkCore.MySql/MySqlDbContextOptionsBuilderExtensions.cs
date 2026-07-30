namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds the Doka MySQL provider bootstrap configuration surface to <see cref="DbContextOptionsBuilder"/>.
/// </summary>
public static class MySqlDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures the context to use the Doka MySQL provider bootstrap with a connection string.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder.</param>
    /// <param name="connectionString">The target connection string.</param>
    /// <param name="serverVersion">The resolved server version.</param>
    /// <param name="mySqlOptionsAction">An optional provider-specific options callback.</param>
    /// <returns>The same <see cref="DbContextOptionsBuilder"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "EF Core community standard: provider UseXxx extensions carry the optional `mySqlOptionsAction = null` parameter as part of the documented public API. See ADR D-008.")]
    public static DbContextOptionsBuilder UseMySql(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        MySqlServerVersion serverVersion,
        Action<MySqlDbContextOptionsBuilder>? mySqlOptionsAction = null
    )
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(serverVersion);

        ConfigureWarnings(optionsBuilder);

        var extension = GetOrCreateExtension(optionsBuilder)
            .WithConnectionString(connectionString)
            .WithServerVersion(serverVersion);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        mySqlOptionsAction?.Invoke(new MySqlDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to use the Doka MySQL provider bootstrap with an existing connection.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder.</param>
    /// <param name="connection">The target connection.</param>
    /// <param name="serverVersion">The resolved server version.</param>
    /// <param name="mySqlOptionsAction">An optional provider-specific options callback.</param>
    /// <returns>The same <see cref="DbContextOptionsBuilder"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "EF Core community standard: provider UseXxx extensions carry the optional `mySqlOptionsAction = null` parameter as part of the documented public API. See ADR D-008.")]
    public static DbContextOptionsBuilder UseMySql(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        MySqlServerVersion serverVersion,
        Action<MySqlDbContextOptionsBuilder>? mySqlOptionsAction = null
    )
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(serverVersion);

        ConfigureWarnings(optionsBuilder);

        var extension = GetOrCreateExtension(optionsBuilder)
            .WithConnection(connection)
            .WithServerVersion(serverVersion);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        mySqlOptionsAction?.Invoke(new MySqlDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to use the Doka MySQL provider bootstrap with an existing data source.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder.</param>
    /// <param name="dataSource">The target data source.</param>
    /// <param name="serverVersion">The resolved server version.</param>
    /// <param name="mySqlOptionsAction">An optional provider-specific options callback.</param>
    /// <returns>The same <see cref="DbContextOptionsBuilder"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "EF Core community standard: provider UseXxx extensions carry the optional `mySqlOptionsAction = null` parameter as part of the documented public API. See ADR D-008.")]
    public static DbContextOptionsBuilder UseMySql(
        this DbContextOptionsBuilder optionsBuilder,
        MySqlDataSource dataSource,
        MySqlServerVersion serverVersion,
        Action<MySqlDbContextOptionsBuilder>? mySqlOptionsAction = null
    )
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(serverVersion);

        ConfigureWarnings(optionsBuilder);

        var extension = GetOrCreateExtension(optionsBuilder)
            .WithDataSource(dataSource)
            .WithServerVersion(serverVersion);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        mySqlOptionsAction?.Invoke(new MySqlDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to use the Doka MySQL provider bootstrap with a connection string.
    /// </summary>
    /// <typeparam name="TContext">The target context type.</typeparam>
    /// <param name="optionsBuilder">The EF Core options builder.</param>
    /// <param name="connectionString">The target connection string.</param>
    /// <param name="serverVersion">The resolved server version.</param>
    /// <param name="mySqlOptionsAction">An optional provider-specific options callback.</param>
    /// <returns>The same <see cref="DbContextOptionsBuilder{TContext}"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "EF Core community standard: provider UseXxx extensions carry the optional `mySqlOptionsAction = null` parameter as part of the documented public API. See ADR D-008.")]
    public static DbContextOptionsBuilder<TContext> UseMySql<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        MySqlServerVersion serverVersion,
        Action<MySqlDbContextOptionsBuilder>? mySqlOptionsAction = null
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((DbContextOptionsBuilder)optionsBuilder).UseMySql(connectionString, serverVersion, mySqlOptionsAction);

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to use the Doka MySQL provider bootstrap with an existing connection.
    /// </summary>
    /// <typeparam name="TContext">The target context type.</typeparam>
    /// <param name="optionsBuilder">The EF Core options builder.</param>
    /// <param name="connection">The target connection.</param>
    /// <param name="serverVersion">The resolved server version.</param>
    /// <param name="mySqlOptionsAction">An optional provider-specific options callback.</param>
    /// <returns>The same <see cref="DbContextOptionsBuilder{TContext}"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "EF Core community standard: provider UseXxx extensions carry the optional `mySqlOptionsAction = null` parameter as part of the documented public API. See ADR D-008.")]
    public static DbContextOptionsBuilder<TContext> UseMySql<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        MySqlServerVersion serverVersion,
        Action<MySqlDbContextOptionsBuilder>? mySqlOptionsAction = null
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((DbContextOptionsBuilder)optionsBuilder).UseMySql(connection, serverVersion, mySqlOptionsAction);

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to use the Doka MySQL provider bootstrap with an existing data source.
    /// </summary>
    /// <typeparam name="TContext">The target context type.</typeparam>
    /// <param name="optionsBuilder">The EF Core options builder.</param>
    /// <param name="dataSource">The target data source.</param>
    /// <param name="serverVersion">The resolved server version.</param>
    /// <param name="mySqlOptionsAction">An optional provider-specific options callback.</param>
    /// <returns>The same <see cref="DbContextOptionsBuilder{TContext}"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "EF Core community standard: provider UseXxx extensions carry the optional `mySqlOptionsAction = null` parameter as part of the documented public API. See ADR D-008.")]
    public static DbContextOptionsBuilder<TContext> UseMySql<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        MySqlDataSource dataSource,
        MySqlServerVersion serverVersion,
        Action<MySqlDbContextOptionsBuilder>? mySqlOptionsAction = null
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((DbContextOptionsBuilder)optionsBuilder).UseMySql(dataSource, serverVersion, mySqlOptionsAction);

        return optionsBuilder;
    }

    private static MySqlOptionsExtension GetOrCreateExtension(
        DbContextOptionsBuilder optionsBuilder
    ) => optionsBuilder.Options.FindExtension<MySqlOptionsExtension>() ?? new MySqlOptionsExtension();

    private static void ConfigureWarnings(
        DbContextOptionsBuilder optionsBuilder
    )
    {
        var coreOptionsExtension =
            optionsBuilder.Options.FindExtension<CoreOptionsExtension>() ?? new CoreOptionsExtension();

        coreOptionsExtension = RelationalOptionsExtension.WithDefaultWarningConfiguration(coreOptionsExtension);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
    }
}
