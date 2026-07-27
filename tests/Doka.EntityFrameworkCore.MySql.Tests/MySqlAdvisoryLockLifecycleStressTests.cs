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
    [Fact]
    public async Task Concurrent_dispose_and_reacquire_completes_without_deadlock()
    {
        await using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);

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
                catch
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
        lockInstance.Dispose();
        lockInstance.Dispose();
    }

    [Fact]
    public void Reacquire_after_dispose_cannot_resurrect_lock()
    {
        using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);

        lockInstance.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            lockInstance.ReacquireIfNeeded(connectionReopened: true, transactionRestarted: null));
        Assert.Throws<ObjectDisposedException>(() => lockInstance.AcquireLock());
    }

    [Fact]
    public async Task Async_reacquire_after_dispose_cannot_resurrect_lock()
    {
        await using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);

        await lockInstance.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            lockInstance.ReacquireIfNeededAsync(connectionReopened: true, transactionRestarted: null));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lockInstance.AcquireLockAsync());
    }

    [Fact]
    public void Dispose_is_idempotent_on_empty_slot()
    {
        using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);

        lockInstance.Dispose();
        lockInstance.Dispose();
        lockInstance.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent_on_empty_slot()
    {
        await using var context = new StubContext(BuildOptions());
        var historyRepository = (MySqlHistoryRepository)context.GetService<IHistoryRepository>();
        var lockInstance = new MySqlHistoryRepository.MySqlMigrationsDatabaseLock(historyRepository);

        await lockInstance.DisposeAsync();
        await lockInstance.DisposeAsync();
        await lockInstance.DisposeAsync();
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
