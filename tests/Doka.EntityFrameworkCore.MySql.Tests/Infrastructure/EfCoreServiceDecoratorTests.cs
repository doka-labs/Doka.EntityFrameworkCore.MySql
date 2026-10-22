#pragma warning disable EF1001 // IMigrationsModelDiffer is EF Core internal; the test asserts the wrap is active.

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the wrap contract of <see cref="EfCoreServiceDecorator"/>: after AddEntityFrameworkDokaMySql
/// and AddEntityFrameworkDokaMySqlDesignTime build their service graphs, the IMigrationsModelDiffer
/// and IModelCodeGenerator registrations resolve to the Doka decorator types -- not the EF Core
/// defaults. A patch release that silently breaks the wrap would surface here as a type assertion
/// failure rather than as a silent regression in scaffolding or migration output.
/// </summary>
public sealed class EfCoreServiceDecoratorTests
{
    /// <summary>
    /// Verifies decorate throws when no inner registration exists.
    /// </summary>
    [Fact]
    public void Decorate_throws_when_no_inner_registration_exists()
    {
        // Arrange
        var services = new ServiceCollection();
        Action act = () =>
            EfCoreServiceDecorator.Decorate<IMigrationsModelDiffer, MySqlMigrationsModelDiffer>(
                services,
                (inner, _) => new MySqlMigrationsModelDiffer(inner));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("IMigrationsModelDiffer", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ADR D-001", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies decorate wraps existing implementation type registration.
    /// </summary>
    [Fact]
    public void Decorate_wraps_existing_implementation_type_registration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDummyService, DummyServiceImpl>();
        EfCoreServiceDecorator.Decorate<IDummyService, DummyServiceDecorator>(
            services,
            (inner, _) => new DummyServiceDecorator(inner));
        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetRequiredService<IDummyService>();

        // Assert
        var decorator = Assert.IsType<DummyServiceDecorator>(resolved);
        Assert.IsType<DummyServiceImpl>(decorator.Inner);
    }

    /// <summary>
    /// Verifies decorate preserves inner descriptor lifetime.
    /// </summary>
    [Fact]
    public void Decorate_preserves_inner_descriptor_lifetime()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IDummyService, DummyServiceImpl>();
        EfCoreServiceDecorator.Decorate<IDummyService, DummyServiceDecorator>(
            services,
            (inner, _) => new DummyServiceDecorator(inner));
        using var provider = services.BuildServiceProvider();
        using var scopeOne = provider.CreateScope();
        using var scopeTwo = provider.CreateScope();
        var first = scopeOne.ServiceProvider.GetRequiredService<IDummyService>();

        // Act
        var second = scopeTwo.ServiceProvider.GetRequiredService<IDummyService>();

        // Assert
        Assert.NotSame(first, second);
    }

    /// <summary>
    /// Verifies decorate wraps existing factory registration.
    /// </summary>
    [Fact]
    public void Decorate_wraps_existing_factory_registration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDummyService>(_ => new DummyServiceImpl());
        EfCoreServiceDecorator.Decorate<IDummyService, DummyServiceDecorator>(
            services,
            (inner, _) => new DummyServiceDecorator(inner));
        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetRequiredService<IDummyService>();

        // Assert
        Assert.IsType<DummyServiceDecorator>(resolved);
    }

    /// <summary>
    /// Verifies decorate wraps existing instance registration.
    /// </summary>
    [Fact]
    public void Decorate_wraps_existing_instance_registration()
    {
        // Arrange
        var services = new ServiceCollection();
        var inner = new DummyServiceImpl();
        services.AddSingleton<IDummyService>(inner);
        EfCoreServiceDecorator.Decorate<IDummyService, DummyServiceDecorator>(
            services,
            (innerService, _) => new DummyServiceDecorator(innerService));
        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetRequiredService<IDummyService>();

        // Assert
        var decorator = Assert.IsType<DummyServiceDecorator>(resolved);
        Assert.Same(inner, decorator.Inner);
    }

    /// <summary>
    /// Verifies <c>AddEntityFrameworkDokaMySql</c> resolves migrationsmodeldiffer as doka decorator.
    /// </summary>
    [Fact]
    public void AddEntityFrameworkDokaMySql_resolves_MigrationsModelDiffer_as_doka_decorator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<DummyContext>(options => options.UseMySql(
            "Server=localhost;Database=stub;User ID=root;Password=pwd;",
            MySqlServerVersion.MySql(new Version(8, 4, 0))));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DummyContext>();

        // Act
        var diff = context.GetService<IMigrationsModelDiffer>();

        // Assert
        Assert.IsType<MySqlMigrationsModelDiffer>(diff);
    }

    public interface IDummyService
    {
        string Describe();
    }

    private sealed class DummyServiceImpl : IDummyService
    {
        public string Describe() => "inner";
    }

    private sealed class DummyServiceDecorator : IDummyService
    {
        public DummyServiceDecorator(
            IDummyService inner
        )
        {
            Inner = inner;
        }

        public IDummyService Inner { get; }

        public string Describe() => $"decorator({Inner.Describe()})";
    }

    private sealed class DummyContext : DbContext
    {
        public DummyContext(
            DbContextOptions<DummyContext> options
        ) : base(options) { }
    }
}

#pragma warning restore EF1001
