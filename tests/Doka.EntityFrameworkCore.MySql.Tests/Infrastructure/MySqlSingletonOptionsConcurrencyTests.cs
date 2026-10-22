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
    /// <summary>
    /// Verifies initialize under high concurrency yields consistent snapshot.
    /// </summary>
    [Fact]
    public void Initialize_under_high_concurrency_yields_consistent_snapshot()
    {
        // Arrange
        var options = BuildOptions(MySqlGuidFormat.Char36);
        var singletonOptions = new MySqlSingletonOptions();

        // Act
        Parallel.For(0, 4000, _ => singletonOptions.Initialize(options));

        // Assert
        Assert.NotNull(singletonOptions.Profile);
        Assert.Equal(MySqlGuidFormat.Char36, singletonOptions.DefaultGuidFormat);
        Assert.False(singletonOptions.UsesDataSource);
    }

    /// <summary>
    /// Verifies initialize is idempotent when called serially.
    /// </summary>
    [Fact]
    public void Initialize_is_idempotent_when_called_serially()
    {
        // Arrange
        var options = BuildOptions(MySqlGuidFormat.Binary16);
        var singletonOptions = new MySqlSingletonOptions();
        singletonOptions.Initialize(options);
        var firstProfile = singletonOptions.Profile;

        // Act
        singletonOptions.Initialize(options);
        var secondProfile = singletonOptions.Profile;

        // Assert
        Assert.Same(firstProfile, secondProfile);
    }

    /// <summary>
    /// Verifies initialize completes before returning so validate sees full snapshot.
    /// </summary>
    [Fact]
    public void Initialize_completes_before_returning_so_validate_sees_full_snapshot()
    {
        // Arrange
        var options = BuildOptions(MySqlGuidFormat.Binary16);
        var singletonOptions = new MySqlSingletonOptions();

        // Act
        // The torn-snapshot failure mode would surface as Validate throwing because
        // Profile appeared set on one thread while the remaining snapshot fields
        // were still stale on another. Running them back-to-back from many threads
        // exercises exactly that ordering.
        Parallel.For(0, 1000, _ =>
        {
            singletonOptions.Initialize(options);
            singletonOptions.Validate(options);
        });

        // Assert
        Assert.NotNull(singletonOptions.Profile);
    }

    /// <summary>
    /// Verifies validate rejects reconfiguration after initialize.
    /// </summary>
    [Fact]
    public void Validate_rejects_reconfiguration_after_initialize()
    {
        // Arrange
        var originalOptions = BuildOptions(MySqlGuidFormat.Binary16);
        var singletonOptions = new MySqlSingletonOptions();
        singletonOptions.Initialize(originalOptions);
        var mutatedOptions = BuildOptions(MySqlGuidFormat.Char36);
        Action act = () => singletonOptions.Validate(mutatedOptions);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
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
