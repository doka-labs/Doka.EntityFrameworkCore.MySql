namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationDatabaseTestGroup : ICollectionFixture<IntegrationDatabaseFixture>
{
    public const string Name = "integration-database";
}

public sealed class IntegrationDatabaseFixture : IAsyncLifetime
{
    private TestDatabaseSession? _session;

    public async Task InitializeAsync()
    {
        var requests = IntegrationTestEnvironment
            .GetSelectedTargets()
            .Select(IntegrationTestEnvironment.CreateRequest)
            .ToArray();

        var session = await TestDatabaseSession
            .StartAsync(requests)
            .ConfigureAwait(false);

        try
        {
            IntegrationTestEnvironment.Initialize(session);
            _session = session;
        }
        catch
        {
            await session
                .DisposeAsync()
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_session is null)
        {
            return;
        }

        var session = _session;
        _session = null;

        try
        {
            IntegrationTestEnvironment.Reset(session);
        }
        finally
        {
            await session
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }
}
