namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Covers higher-level option registration behavior.
/// </summary>
public sealed class MySqlOptionsRegistrationTests
{
    /// <summary>
    /// Verifies that runtime registration adds the provider bootstrap services through the runtime seam only.
    /// </summary>
    [Fact]
    public void AddEntityFrameworkDokaMySql_registers_runtime_provider_services()
    {
        var services = new ServiceCollection();

        services.AddEntityFrameworkDokaMySql();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDatabaseProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IMySqlDriverFacade));
    }

    /// <summary>
    /// Verifies that runtime registration produces a resolvable provider service graph without design-time services.
    /// </summary>
    [Fact]
    public void AddEntityFrameworkDokaMySql_builds_a_resolvable_runtime_provider_graph()
    {
        var services = new ServiceCollection();

        services.AddEntityFrameworkDokaMySql();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        Assert.IsType<DatabaseProvider<MySqlOptionsExtension>>(serviceProvider.GetRequiredService<IDatabaseProvider>());
        Assert.IsType<MySqlConnectorDriverFacade>(serviceProvider.GetRequiredService<IMySqlDriverFacade>());
        Assert.Single(
            serviceProvider
                .GetServices<ISingletonOptions>()
                .OfType<MySqlSingletonOptions>());
    }

    /// <summary>
    /// Verifies that the explicit design-time path composes the runtime registrations.
    /// </summary>
    [Fact]
    public void Design_time_registration_uses_the_explicit_design_time_seam()
    {
        var services = new ServiceCollection();

        new MySqlDesignTimeServices().ConfigureDesignTimeServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDatabaseProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IMySqlDriverFacade));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDatabaseModelFactory));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProviderConfigurationCodeGenerator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAnnotationCodeGenerator));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType.FullName
                == "Microsoft.EntityFrameworkCore.Design.Internal.ICSharpRuntimeAnnotationCodeGenerator");
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IScaffoldingModelFactory));
    }

    /// <summary>
    /// Verifies that repeated registration updates the same extension slot.
    /// </summary>
    [Fact]
    public void Repeated_UseMySql_calls_keep_a_single_extension_instance()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);
        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);

        var extensions = builder
            .Options.Extensions.OfType<MySqlOptionsExtension>()
            .ToArray();

        Assert.Single(extensions);
    }

    /// <summary>
    /// Verifies that the generic overload preserves the original builder instance.
    /// </summary>
    [Fact]
    public void Generic_builder_overload_returns_the_same_builder_instance()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>();
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

        var returned = builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion);

        Assert.Same(builder, returned);
    }

    /// <summary>
    /// Verifies that the generic data-source overload preserves the original builder instance.
    /// </summary>
    [Fact]
    public void Generic_data_source_builder_overload_returns_the_same_builder_instance()
    {
        using var dataSource = new MySqlDataSourceBuilder(
            "Server=localhost;Database=doka;User ID=root;Password=password;").Build();
        var builder = new DbContextOptionsBuilder<TestDbContext>();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        var returned = builder.UseMySql(dataSource, serverVersion);

        Assert.Same(builder, returned);
    }

    /// <summary>
    /// Verifies that switching registration modes keeps one extension and the latest connection path.
    /// </summary>
    [Fact]
    public void Repeated_UseMySql_calls_replace_the_connection_path_consistently()
    {
        using var dataSource = new MySqlDataSourceBuilder(
            "Server=localhost;Database=doka;User ID=root;Password=password;").Build();
        var builder = new DbContextOptionsBuilder<TestDbContext>();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql("Server=localhost;Database=doka;User ID=root;Password=password;", serverVersion);
        builder.UseMySql(dataSource, serverVersion);

        var extension = Assert.IsType<MySqlOptionsExtension>(builder.Options.FindExtension<MySqlOptionsExtension>());
        var extensions = builder
            .Options.Extensions.OfType<MySqlOptionsExtension>()
            .ToArray();

        Assert.Single(extensions);
        Assert.Same(dataSource, extension.DataSource);
        Assert.Null(extension.ConnectionString);
        Assert.Null(extension.Connection);
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(
            DbContextOptions<TestDbContext> options
        ) : base(options) { }
    }
}
