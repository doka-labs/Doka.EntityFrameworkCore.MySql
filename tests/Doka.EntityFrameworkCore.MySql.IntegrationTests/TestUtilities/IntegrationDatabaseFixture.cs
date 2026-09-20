namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Marks a class that needs the shared integration server session during execution.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class IntegrationDatabaseTestAttribute : Attribute
{
}

/// <summary>
/// Owns the one server session shared by independently isolated test classes.
/// </summary>
public sealed class IntegrationDatabaseFixture : IAsyncLifetime
{
    private TestDatabaseSession? _session;

    /// <summary>Starts the selected server targets once for this test run.</summary>
    /// <returns>A value task that completes when all selected targets are ready.</returns>
    public async ValueTask InitializeAsync()
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

    /// <summary>Drops class databases and stops the test-owned servers.</summary>
    /// <returns>A value task that completes after cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_session is null)
        {
            return;
        }

        var session = _session;
        _session = null;

        try
        {
            await IntegrationTestEnvironment
                .ResetAsync(session)
                .ConfigureAwait(false);
        }
        finally
        {
            await session
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }
}
