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
    [Fact]
    public void Decorate_throws_when_no_inner_registration_exists()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EfCoreServiceDecorator.Decorate<IMigrationsModelDiffer, MySqlMigrationsModelDiffer>(
                services,
                (inner, _) => new MySqlMigrationsModelDiffer(inner)));

        Assert.Contains("IMigrationsModelDiffer", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ADR D-001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decorate_wraps_existing_implementation_type_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDummyService, DummyServiceImpl>();

        EfCoreServiceDecorator.Decorate<IDummyService, DummyServiceDecorator>(
            services,
            (inner, _) => new DummyServiceDecorator(inner));

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IDummyService>();

        var decorator = Assert.IsType<DummyServiceDecorator>(resolved);
        Assert.IsType<DummyServiceImpl>(decorator.Inner);
    }

    [Fact]
    public void Decorate_preserves_inner_descriptor_lifetime()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDummyService, DummyServiceImpl>();

        EfCoreServiceDecorator.Decorate<IDummyService, DummyServiceDecorator>(
            services,
            (inner, _) => new DummyServiceDecorator(inner));

        using var provider = services.BuildServiceProvider();
        using var scopeOne = provider.CreateScope();
        using var scopeTwo = provider.CreateScope();

        var first = scopeOne.ServiceProvider.GetRequiredService<IDummyService>();
        var second = scopeTwo.ServiceProvider.GetRequiredService<IDummyService>();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Decorate_wraps_existing_factory_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDummyService>(_ => new DummyServiceImpl());

        EfCoreServiceDecorator.Decorate<IDummyService, DummyServiceDecorator>(
            services,
            (inner, _) => new DummyServiceDecorator(inner));

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IDummyService>();

        Assert.IsType<DummyServiceDecorator>(resolved);
    }

    [Fact]
    public void Decorate_wraps_existing_instance_registration()
    {
        var services = new ServiceCollection();
        var inner = new DummyServiceImpl();
        services.AddSingleton<IDummyService>(inner);

        EfCoreServiceDecorator.Decorate<IDummyService, DummyServiceDecorator>(
            services,
            (innerService, _) => new DummyServiceDecorator(innerService));

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IDummyService>();

        var decorator = Assert.IsType<DummyServiceDecorator>(resolved);
        Assert.Same(inner, decorator.Inner);
    }

    [Fact]
    public void AddEntityFrameworkDokaMySql_resolves_MigrationsModelDiffer_as_doka_decorator()
    {
        var services = new ServiceCollection();
        services.AddDbContext<DummyContext>(options => options.UseMySql(
            "Server=localhost;Database=stub;User ID=root;Password=pwd;",
            MySqlServerVersion.MySql(new Version(8, 4, 0))));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DummyContext>();

        var diff = context.GetService<IMigrationsModelDiffer>();

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
