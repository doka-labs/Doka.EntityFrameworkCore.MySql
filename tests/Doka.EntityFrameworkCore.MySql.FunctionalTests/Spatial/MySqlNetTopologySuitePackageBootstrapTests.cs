namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Covers the optional NetTopologySuite package bootstrap behavior.
/// </summary>
public sealed class MySqlNetTopologySuitePackageBootstrapTests
{
    /// <summary>
    /// Verifies that the optional spatial services are not part of the main provider runtime graph by default.
    /// </summary>
    [Fact]
    public void Main_provider_runtime_graph_does_not_register_spatial_services_by_default()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddEntityFrameworkDokaMySql();

        // Act
        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        // Assert
        Assert.Null(serviceProvider.GetService<IMySqlNetTopologySuiteMarker>());
    }

    /// <summary>
    /// Verifies that explicit spatial activation adds the optional package services.
    /// </summary>
    [Fact]
    public void UseNetTopologySuite_adds_the_optional_spatial_services()
    {
        // Arrange
        using var context = CreateContext(useNetTopologySuite: true);
        var serviceProvider = ((IInfrastructure<IServiceProvider>)context).Instance;

        // Act
        var marker = serviceProvider.GetService<IMySqlNetTopologySuiteMarker>();

        // Assert
        Assert.NotNull(marker);
        Assert.Equal("Geometry", marker.GeometryType.Name);
    }

    /// <summary>
    /// Verifies that the main provider remains resolvable without the optional spatial seam.
    /// </summary>
    [Fact]
    public void Main_provider_context_resolves_without_the_optional_spatial_seam()
    {
        // Arrange
        using var context = CreateContext(useNetTopologySuite: false);

        // Act
        var serviceProvider = ((IInfrastructure<IServiceProvider>)context).Instance;

        // Assert
        Assert.Null(serviceProvider.GetService<IMySqlNetTopologySuiteMarker>());
        Assert.NotNull(context.GetService<IRelationalConnection>());
    }

    private static TestDbContext CreateContext(
        bool useNetTopologySuite
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TestDbContext>();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion,
            options =>
            {
                if (useNetTopologySuite)
                {
                    options.UseNetTopologySuite();
                }
            });

        return new TestDbContext(builder.Options);
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(
            DbContextOptions<TestDbContext> options
        ) : base(options) { }
    }
}
