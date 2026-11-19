namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Full fixture disposal drains its actual native pool while an independently owned pool remains reusable.</summary>
    /// <param name="target">The selected pinned engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Disposing_graph_store_drains_its_pool_and_preserves_an_independent_pool(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await GraphStore.CreateAsync(target, safety.Token);
        var selected = Enum.Parse<IntegrationDatabaseTarget>(target);
        var observerOptions =
            new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetBaseConnectionString(selected))
            {
                Database = string.Empty,
                Pooling = false,
                AutoEnlist = false,
            };

        await using var observer = new MySqlConnection(observerOptions.ConnectionString);
        await observer.OpenAsync(safety.Token);
        var controlOptions = new MySqlConnectionStringBuilder(observerOptions.ConnectionString)
        {
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 1,
            ConnectionIdleTimeout = 0,
            ApplicationName = "DokaPoolControl_"
                + Guid
                    .NewGuid()
                    .ToString("N", CultureInfo.InvariantCulture),
        };

        await using var control = new MySqlConnection(controlOptions.ConnectionString);

        try
        {
            await control.OpenAsync(safety.Token);
            var originalControlThread = control.ServerThread;
            await control.CloseAsync();
            int originalOwnedThread;

            await using (var first = store.CreateContext())
            {
                await first.Database.OpenConnectionAsync(safety.Token);
                originalOwnedThread = ((MySqlConnection)first.Database.GetDbConnection()).ServerThread;
                await first.Database.CloseConnectionAsync();
            }

            int reusedOwnedThread;

            await using (var repeated = store.CreateContext())
            {
                await repeated.Database.OpenConnectionAsync(safety.Token);
                reusedOwnedThread = ((MySqlConnection)repeated.Database.GetDbConnection()).ServerThread;
                await repeated.Database.CloseConnectionAsync();
            }

            await using var presence = new MySqlCommand(
                $"SELECT COUNT(*) FROM {MySqlNativeProcessMetadata.GetProcessListSource(observer)} WHERE ID=@worker;",
                observer);

            presence.Parameters.AddWithValue("@worker", originalOwnedThread);
            var ownedBefore = Convert.ToInt32(await presence.ExecuteScalarAsync(safety.Token),
                CultureInfo.InvariantCulture);

            // Act
            await store.DisposeAsync();
            int ownedAfter;

            do
            {
                ownedAfter = Convert.ToInt32(await presence.ExecuteScalarAsync(safety.Token),
                    CultureInfo.InvariantCulture);

                if (ownedAfter != 0)
                {
                    // WHY: Native disconnect processing is asynchronous. Readiness, not
                    // elapsed time, qualifies cleanup; disabled idle expiry cannot conceal a missed clear.
                    await Task.Delay(25, safety.Token);
                }
            } while (ownedAfter != 0);

            await control.OpenAsync(safety.Token);
            var retainedControlThread = control.ServerThread;
            await control.CloseAsync();

            // Assert
            Assert.Equal(originalOwnedThread, reusedOwnedThread);
            Assert.Equal(1, ownedBefore);
            Assert.Equal(0, ownedAfter);
            Assert.Equal(originalControlThread, retainedControlThread);
        }
        finally
        {
            // WHY: The control pool is independent fixture state and must be cleared
            // even after cancellation or an assertion failure, without touching other pools.
            try
            {
                await control.CloseAsync();
            }
            finally
            {
                await MySqlConnection.ClearPoolAsync(control, CancellationToken.None);
            }
        }
    }
}
