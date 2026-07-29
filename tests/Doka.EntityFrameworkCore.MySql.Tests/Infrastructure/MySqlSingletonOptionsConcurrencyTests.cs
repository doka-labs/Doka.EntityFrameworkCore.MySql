namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the thread-safety contract of <see cref="MySqlSingletonOptions.Initialize"/>:
/// under <c>AddDbContextPool</c> the framework can resolve a singleton service from
/// many threads before the first Initialize completes. The double-checked-lock guard
/// must ensure that no consumer ever observes a torn property snapshot, and that
/// concurrent Initialize callers all return without throwing.
/// </summary>
public sealed class MySqlSingletonOptionsConcurrencyTests
{
    [Fact]
    public void Initialize_under_high_concurrency_yields_consistent_snapshot()
    {
        var options = BuildOptions(MySqlGuidFormat.Char36);
        var singletonOptions = new MySqlSingletonOptions();

        Parallel.For(0, 4000, _ => singletonOptions.Initialize(options));

        Assert.NotNull(singletonOptions.ServerVersion);
        Assert.NotNull(singletonOptions.Profile);
        Assert.Equal(MySqlGuidFormat.Char36, singletonOptions.DefaultGuidFormat);
        Assert.False(singletonOptions.UsesDataSource);
    }

    [Fact]
    public void Initialize_is_idempotent_when_called_serially()
    {
        var options = BuildOptions(MySqlGuidFormat.Binary16);
        var singletonOptions = new MySqlSingletonOptions();

        singletonOptions.Initialize(options);
        var firstServerVersion = singletonOptions.ServerVersion;

        singletonOptions.Initialize(options);
        var secondServerVersion = singletonOptions.ServerVersion;

        Assert.Same(firstServerVersion, secondServerVersion);
    }

    [Fact]
    public void Initialize_completes_before_returning_so_validate_sees_full_snapshot()
    {
        var options = BuildOptions(MySqlGuidFormat.Binary16);
        var singletonOptions = new MySqlSingletonOptions();

        // The torn-snapshot failure mode would surface as Validate throwing because
        // ServerVersion appeared set on one thread while Capabilities was still
        // null on another. Running them back-to-back from many threads exercises
        // exactly that ordering.
        Parallel.For(0, 1000, _ =>
        {
            singletonOptions.Initialize(options);
            singletonOptions.Validate(options);
        });

        Assert.NotNull(singletonOptions.Profile);
    }

    [Fact]
    public void Validate_rejects_reconfiguration_after_initialize()
    {
        var originalOptions = BuildOptions(MySqlGuidFormat.Binary16);
        var singletonOptions = new MySqlSingletonOptions();
        singletonOptions.Initialize(originalOptions);

        var mutatedOptions = BuildOptions(MySqlGuidFormat.Char36);

        Assert.Throws<InvalidOperationException>(() => singletonOptions.Validate(mutatedOptions));
    }

    private static DbContextOptions BuildOptions(
        MySqlGuidFormat guidFormat
    )
    {
        var builder = new DbContextOptionsBuilder();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            mySql => mySql.DefaultGuidFormat(guidFormat));
        return builder.Options;
    }
}
