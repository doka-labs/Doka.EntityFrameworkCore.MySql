namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Stress coverage for the migration advisory-lock lifecycle slot-management. The
/// lock class lives inside <see cref="MySqlHistoryRepository"/> as an internal
/// nested type; the surface under test is the visible behavior: Dispose is
/// idempotent across concurrent callers, ReacquireIfNeeded releases the old
/// connection before the next acquire, and disposal is a terminal lifecycle state.
/// The test points the DbContext at an unreachable port so every attempted acquire
/// fails fast while still exercising the serialized cleanup transitions.
/// </summary>
public sealed class MySqlAdvisoryLockLifecycleStressTests
{
    /// <summary>
    /// Verifies concurrent dispose and reacquire completes without deadlock.
    /// </summary>
    [Fact]
    public async Task Concurrent_dispose_and_reacquire_completes_without_deadlock()
    {
        // Arrange
        await using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);

        // Act
        var disposes = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                lockInstance.Dispose();
            }
        });

        var reacquires = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                try
                {
                    lockInstance.ReacquireIfNeeded(connectionReopened: true, transactionRestarted: null);
                }
                catch (Exception exception) when (exception is MySqlException or ObjectDisposedException)
                {
                    // AcquireLock targets an unreachable port and fails on Open; the
                    // expected exception proves the cleanup-on-failure path runs and
                    // does not leave the connection slot in a half-broken state.
                }
            }
        });

        await Task.WhenAll(disposes, reacquires);

        // Final Dispose proves the slot is clean after the storm; second call confirms
        // idempotency on a now-empty slot.
        var cleanupException = Record.Exception(() =>
        {
            lockInstance.Dispose();
            lockInstance.Dispose();
        });

        // Assert
        Assert.Null(cleanupException);
    }

    /// <summary>
    /// Verifies reacquire after dispose cannot resurrect lock.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reacquire_after_dispose_cannot_resurrect_lock(
        bool reacquire
    )
    {
        // Arrange
        using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);
        lockInstance.Dispose();

        // Act
        var exception = Record.Exception(() =>
        {
            if (reacquire)
            {
                lockInstance.ReacquireIfNeeded(connectionReopened: true, transactionRestarted: null);
            }
            else
            {
                lockInstance.AcquireLock();
            }
        });

        // Assert
        Assert.IsType<ObjectDisposedException>(exception);
    }

    /// <summary>
    /// Verifies async reacquire after dispose cannot resurrect lock.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_reacquire_after_dispose_cannot_resurrect_lock(
        bool reacquire
    )
    {
        // Arrange
        await using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);
        await lockInstance.DisposeAsync();

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            if (reacquire)
            {
                await lockInstance.ReacquireIfNeededAsync(connectionReopened: true, transactionRestarted: null);
            }
            else
            {
                await lockInstance.AcquireLockAsync();
            }
        });

        // Assert
        Assert.IsType<ObjectDisposedException>(exception);
    }

    /// <summary>
    /// Verifies dispose is idempotent on empty slot.
    /// </summary>
    [Fact]
    public void Dispose_is_idempotent_on_empty_slot()
    {
        // Arrange
        using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);

        // Act
        var exception = Record.Exception(() =>
        {
            lockInstance.Dispose();
            lockInstance.Dispose();
            lockInstance.Dispose();
        });

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies <c>DisposeAsync</c> is idempotent on empty slot.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_is_idempotent_on_empty_slot()
    {
        // Arrange
        await using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await lockInstance.DisposeAsync();
            await lockInstance.DisposeAsync();
            await lockInstance.DisposeAsync();
        });

        // Assert
        Assert.Null(exception);
    }

    private static DbContextOptions<StubContext> BuildOptions()
    {
        var builder = new DbContextOptionsBuilder<StubContext>();
        builder.UseMySql(
            "Server=127.0.0.1;Port=1;Database=stub;User ID=stub;Password=stub;Connection Timeout=1;Pooling=false;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private sealed class StubContext : DbContext
    {
        public StubContext(
            DbContextOptions<StubContext> options
        ) : base(options) { }
    }
}
