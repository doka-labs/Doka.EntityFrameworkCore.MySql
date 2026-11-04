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
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Disposes the target-owned tables after the test class has completed.</summary>
    public async Task DisposeAsync()
    {
        // WHY: Fixture teardown must release every engine dataset after a cancelled test.
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
    /// <param name="target">The database target used for this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    internal Task<CacheIntegrationStore> GetStoreAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    ) => GetStoreAsync(target, _stores, cancellationToken);

    /// <summary>Returns the isolated expiration table for the target.</summary>
    /// <param name="target">The database target used for this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    internal Task<CacheIntegrationStore> GetExpirationStoreAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    ) => GetStoreAsync(target, _expirationStores, cancellationToken);

    /// <summary>Creates and retains the target store unless its deployment already exists.</summary>
    /// <param name="target">The database target used for this operation.</param>
    /// <param name="stores">The stores used by this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    private static async Task<CacheIntegrationStore> GetStoreAsync(
        IntegrationDatabaseTarget target,
        Dictionary<IntegrationDatabaseTarget, CacheIntegrationStore> stores,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: xUnit serializes tests in this class, so fixture setup needs no extra lock.
        if (stores.TryGetValue(target, out var existing))
        {
            return existing;
        }

        // WHY: Sharing schema setup avoids a CREATE/DROP cycle for every named contract test.
        // Ordinary and expiration cases use separate tables so expired rows cannot affect other contracts.
        var store = await CacheIntegrationStore
            .CreateAsync(target, cancellationToken, DatabaseOwner)
            .ConfigureAwait(false);

        try
        {
            await store
                .ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName), cancellationToken)
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
