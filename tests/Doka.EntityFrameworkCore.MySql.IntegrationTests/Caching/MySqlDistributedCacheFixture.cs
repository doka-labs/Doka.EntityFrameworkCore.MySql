namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Shares one deployed cache table per selected engine across independent contract cases.
/// </summary>
public sealed class MySqlDistributedCacheFixture : IAsyncLifetime
{
    // WHY: Topic partials and the fixture must keep one schema owner so table
    // isolation tests compare separate deployments inside the same database.
    internal const string DatabaseOwner = nameof(MySqlDistributedCacheIntegrationTests);

    private readonly Dictionary<IntegrationDatabaseTarget, CacheIntegrationStore> _stores = new();
    private readonly Dictionary<IntegrationDatabaseTarget, CacheIntegrationStore> _expirationStores = new();

    /// <summary>Defers database setup until a selected target is actually exercised.</summary>
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Disposes the target-owned tables after the test class has completed.</summary>
    public async ValueTask DisposeAsync()
    {
        List<Exception>? failures = null;

        foreach (var store in _stores.Values.Concat(_expirationStores.Values))
        {
            try
            {
                await store
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                // WHY: One failed database teardown must not strand the other engine fixtures.
                (failures ??= new List<Exception>()).Add(failure);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("Cache fixture cleanup failed.", failures);
        }
    }

    /// <summary>Returns the one deployed store owned by this class for the target.</summary>
    internal Task<CacheIntegrationStore> GetStoreAsync(
        IntegrationDatabaseTarget target
    ) => GetStoreAsync(target, _stores);

    /// <summary>Returns the isolated expiration table for the target.</summary>
    internal Task<CacheIntegrationStore> GetExpirationStoreAsync(
        IntegrationDatabaseTarget target
    ) => GetStoreAsync(target, _expirationStores);

    private static async Task<CacheIntegrationStore> GetStoreAsync(
        IntegrationDatabaseTarget target,
        Dictionary<IntegrationDatabaseTarget, CacheIntegrationStore> stores
    )
    {
        // WHY: xUnit serializes tests in this class, so fixture setup needs no extra lock.
        if (stores.TryGetValue(target, out var existing))
        {
            return existing;
        }

        // WHY: Sharing schema setup avoids a CREATE/DROP cycle for every named contract test.
        // Ordinary and expiration cases use separate tables so expired rows cannot affect other contracts.
        var store = await CacheIntegrationStore
            .CreateAsync(target, DatabaseOwner)
            .ConfigureAwait(false);

        try
        {
            await store
                .ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName))
                .ConfigureAwait(false);

            stores.Add(target, store);

            return store;
        }
        catch
        {
            await store
                .DisposeAsync()
                .ConfigureAwait(false);

            throw;
        }
    }
}
